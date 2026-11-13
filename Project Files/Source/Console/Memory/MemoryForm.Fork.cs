﻿//=================================================================
// MemoryForm.Fork.cs - Jull1ano fork
//=================================================================
// The memory bar above the grid, as AltSDR's MEMORY tab (rc54 - rc56): back / play / next, "n / N" and SLOW.
//  - back / next: the previous / next row with a frequency becomes the current one and is tuned, as a click on
//    the row's header tunes it; round at the ends; the grid's order as shown (a sort reorders the list itself).
//  - play: a slow seek: from the row after the current one, each memory with Scan ticked is tuned in turn (the
//    grid's current row following), stopping on the first one with a signal (play lit); play again goes on to the
//    next one with a signal. While it steps play is a red stop, which stays on the memory it is on (play then goes
//    on from there). While a seek is on, back is the seek's BACK (the previous memory, stopped there) and next its
//    FORWARD (on to the next memory). Round at the end.
//  - "a signal": with SQL on (not FM) the RX1 meter at or over the squelch level; otherwise the passband SNR
//    (the info bar's PB SNR) at least SeekMarginDb over the noise. Each memory: SettleMs for the meter and the
//    AGC, then readings for ReadMs (SLOW: ReadSlowMs), all averaged.
//=================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Thetis
{
    public partial class MemoryForm
    {
        // Idle: no seek. Stepping: tuning memory after memory. Held: on a memory with a signal. Stopped: stopped by
        // hand (stop, back) on a memory; play goes on from it.
        private enum ForkSeekState { Idle, Stepping, Held, Stopped }

        private const int SettleMs = 400;     // after a memory is tuned: meter, AGC, the view re-centred
        private const int SampleMs = 60;      // the seek timer
        private const int MinSamples = 4;     // readings averaged per memory, at least
        private const int ReadMs = 240;       // readings for this long (normal): about 0.65 s per memory
        private const int ReadSlowMs = 1000;  // SLOW: about 1.4 s per memory
        private const int MaxWaitMs = 2500;   // no reading in this time (noise floor still settling): passed over
        private static double SeekMarginDb = 6.0;

        private Panel _forkBar;
        private ForkTransportButton _forkBack, _forkPlay, _forkNext;
        private Label _forkCount;
        private ForkCheck _forkSlow;
        private Timer _forkSeekTimer;
        private ForkSeekState _forkSeek = ForkSeekState.Idle;
        private int _forkSeekRow = -1;
        private DateTime _forkSeekTunedAt;
        private double _forkSeekVfo = double.NaN;
        private double _forkSeekSum;
        private int _forkSeekN;
        private bool _forkLayingOut;
        private System.Collections.Generic.HashSet<int> _forkFakeSignal; // test: THETIS_SEEK_FAKE=<row>,<row>...

        private void forkCreateBar()
        {
            double m;
            if (double.TryParse(Environment.GetEnvironmentVariable("THETIS_SEEK_MARGIN"), NumberStyles.Float, CultureInfo.InvariantCulture, out m))
                SeekMarginDb = m;
            string fake = Environment.GetEnvironmentVariable("THETIS_SEEK_FAKE");
            if (fake != null)
            {
                _forkFakeSignal = new System.Collections.Generic.HashSet<int>();
                foreach (string p in fake.Split(','))
                {
                    int r;
                    if (int.TryParse(p.Trim(), out r)) _forkFakeSignal.Add(r);
                }
            }

            _forkBar = new Panel();
            _forkBar.Name = "forkMemoryBar";
            _forkBack = new ForkTransportButton(ForkTransportButton.GlyphKind.Back) { Name = "forkMemBack" };
            _forkPlay = new ForkTransportButton(ForkTransportButton.GlyphKind.Play) { Name = "forkMemPlay" };
            _forkNext = new ForkTransportButton(ForkTransportButton.GlyphKind.Forward) { Name = "forkMemNext" };
            _forkCount = new Label();
            _forkCount.Name = "forkMemCount";
            _forkCount.AutoSize = false;
            _forkCount.TextAlign = ContentAlignment.MiddleLeft;
            _forkSlow = new ForkCheck("SLOW") { Name = "forkMemSlow" };
            _forkSlow.Checked = forkLoadSlow();
            _forkBar.Controls.Add(_forkBack);
            _forkBar.Controls.Add(_forkPlay);
            _forkBar.Controls.Add(_forkNext);
            _forkBar.Controls.Add(_forkCount);
            _forkBar.Controls.Add(_forkSlow);
            this.Controls.Add(_forkBar);

            _forkBack.Click += (s, e) => forkBackClick();
            _forkNext.Click += (s, e) => forkNextClick();
            _forkPlay.Click += (s, e) => forkPlayClick();
            _forkSlow.CheckedChanged += (s, e) => { forkSaveSlow(); HighDpi.Log("memory seek: SLOW " + (_forkSlow.Checked ? "on" : "off")); };
            toolTip1.SetToolTip(_forkSlow, "Slow: the seek (play) stays longer on each memory (about 1.4 s instead of 0.65 s)\n" +
                "and averages a weak signal longer before it decides; a change applies to a running seek at once");

            _forkSeekTimer = new Timer();
            _forkSeekTimer.Interval = SampleMs;
            _forkSeekTimer.Tick += forkSeekTick;

            this.Layout += (s, e) => forkLayoutBar();
            _forkBar.Layout += (s, e) => forkLayoutBar(); // a text-size pass changes the bar's fonts
            this.VisibleChanged += (s, e) => { if (!Visible) forkSeekEnd("the window closed"); };
            dataGridView1.CurrentCellChanged += (s, e) => forkUpdateCount();
            dataGridView1.RowsAdded += (s, e) => forkUpdateCount();
            dataGridView1.RowsRemoved += (s, e) => forkUpdateCount();
            dataGridView1.DataBindingComplete += (s, e) => forkUpdateCount();
            forkLayoutBar();
            forkUpdateButtons();
        }

        // SLOW, saved in the data folder (fork_memory_bar.txt: "seek_slow 1")
        private string forkBarFile()
        {
            try { return Path.Combine(console.AppDataPath, "fork_memory_bar.txt"); }
            catch { return null; }
        }

        private bool forkLoadSlow()
        {
            try
            {
                string f = forkBarFile();
                if (f == null || !File.Exists(f)) return false;
                foreach (string line in File.ReadAllLines(f))
                {
                    string[] p = line.Trim().Split(new[] { ' ', '\t', '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 2 && p[0] == "seek_slow") return p[1] == "1" || p[1].Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        private void forkSaveSlow()
        {
            try
            {
                string f = forkBarFile();
                if (f != null) File.WriteAllText(f, "seek_slow " + (_forkSlow.Checked ? "1" : "0") + Environment.NewLine);
            }
            catch { }
        }

        // as AltSDR: the bar 23 px high right under the window's top, the buttons 24 x 17 with 3 px between them at
        // the grid's left edge, "n / N" 8 px after them, SLOW 14 px after the counter's room; the grid starts under the
        // bar and keeps its bottom edge. Run on every layout, so it comes out the same after the high-DPI passes.
        private void forkLayoutBar()
        {
            if (_forkLayingOut || _forkBar == null) return;
            _forkLayingOut = true;
            try
            {
                int barH = HighDpi.Px(23), gap = HighDpi.Px(3);
                int bw = HighDpi.Px(24), bh = HighDpi.Px(17);
                Rectangle g = dataGridView1.Bounds;
                Rectangle bar = new Rectangle(g.Left, 0, Math.Max(1, g.Width), barH);
                if (_forkBar.Bounds != bar) _forkBar.Bounds = bar;
                int y = Math.Max(0, (barH - bh) / 2 - HighDpi.Px(1));
                _forkBack.Bounds = new Rectangle(0, y, bw, bh);
                _forkPlay.Bounds = new Rectangle(bw + gap, y, bw, bh);
                _forkNext.Bounds = new Rectangle(2 * (bw + gap), y, bw, bh);
                int lx = 3 * bw + 2 * gap + HighDpi.Px(8);
                int tw;
                using (Graphics gr = Graphics.FromHwnd(IntPtr.Zero)) // no handle needed (the form may not have one yet)
                    tw = (int)Math.Ceiling(Math.Max(gr.MeasureString(_forkCount.Text ?? "", _forkCount.Font).Width, gr.MeasureString("88 / 88", _forkCount.Font).Width));
                _forkCount.Bounds = new Rectangle(lx, y, tw, bh);
                Size cs = _forkSlow.GetPreferredSize(Size.Empty);
                _forkSlow.Bounds = new Rectangle(lx + tw + HighDpi.Px(14), y, cs.Width, bh);
                int gt = bar.Bottom + HighDpi.Px(3);
                if (g.Top != gt)
                    dataGridView1.Bounds = new Rectangle(g.Left, gt, g.Width, Math.Max(HighDpi.Px(40), g.Bottom - gt));
            }
            finally { _forkLayingOut = false; }
        }

        private int forkCurrentRow()
        {
            DataGridViewCell c = dataGridView1.CurrentCell;
            return c != null && c.RowIndex >= 0 && c.RowIndex < dataGridView1.Rows.Count ? c.RowIndex : -1;
        }

        private MemoryRecord forkRecord(int row)
        {
            return row >= 0 && row < dataGridView1.Rows.Count ? dataGridView1.Rows[row].DataBoundItem as MemoryRecord : null;
        }

        private bool forkHasFreq(int row)
        {
            MemoryRecord r = forkRecord(row);
            return r != null && r.RXFreq > 0;
        }

        // the seek's memories: a frequency and Scan ticked (AltSDR: S = Y)
        private bool forkSeekable(int row)
        {
            MemoryRecord r = forkRecord(row);
            return r != null && r.RXFreq > 0 && r.Scan;
        }

        // the next row that passes the test in that direction, round; -1 when none does
        private int forkNextRow(int from, int dir, Func<int, bool> ok)
        {
            int n = dataGridView1.Rows.Count;
            if (n == 0) return -1;
            int start = from >= 0 ? from : (dir > 0 ? -1 : n);
            for (int k = 1; k <= n; k++)
            {
                int r = ((start + dir * k) % n + n) % n;
                if (ok(r)) return r;
            }
            return -1;
        }

        // that row made the current one (in view) and tuned, as a click on its header, without "Close after selection"
        private void forkTuneRow(int row)
        {
            foreach (DataGridViewCell cell in dataGridView1.Rows[row].Cells)
                if (cell.Visible) { dataGridView1.CurrentCell = cell; break; }
            RIndex = row;
            console.changeComboFMMemory(row); // the grid shows console.MemoryList.List itself: row = list index
            console.BeginInvoke(new MethodInvoker(console.CentreViewOnVfo));
            ScheduleUpdate();
            forkUpdateCount();
        }

        private void forkBackClick()
        {
            if (_forkSeek == ForkSeekState.Idle)
            {
                int r = forkNextRow(forkCurrentRow(), -1, forkHasFreq);
                if (r >= 0) forkTuneRow(r);
                return;
            }
            // the seek's BACK: the memory before, and it stays there
            int b = forkNextRow(forkCurrentRow(), -1, forkSeekable);
            if (b < 0) { forkSeekEnd("no memory to seek"); return; }
            forkTuneRow(b);
            forkSeekStopOn(b, "back");
        }

        private void forkNextClick()
        {
            switch (_forkSeek)
            {
                case ForkSeekState.Idle:
                    int r = forkNextRow(forkCurrentRow(), +1, forkHasFreq);
                    if (r >= 0) forkTuneRow(r);
                    return;
                case ForkSeekState.Stepping:
                case ForkSeekState.Held:
                    forkSeekGo(forkNextRow(forkCurrentRow(), +1, forkSeekable)); // on, from the next memory
                    return;
                case ForkSeekState.Stopped:
                    int f = forkNextRow(forkCurrentRow(), +1, forkSeekable); // the next one, still stopped
                    if (f < 0) { forkSeekEnd("no memory to seek"); return; }
                    forkTuneRow(f);
                    forkSeekStopOn(f, null);
                    return;
            }
        }

        private void forkPlayClick()
        {
            switch (_forkSeek)
            {
                case ForkSeekState.Stepping:
                    forkSeekStopOn(_forkSeekRow, "stop");
                    return;
                case ForkSeekState.Stopped:
                    // on from the memory it stopped on (as AltSDR's resume)
                    int cur = forkCurrentRow();
                    forkSeekGo(forkSeekable(cur) ? cur : forkNextRow(cur, +1, forkSeekable));
                    return;
                default: // Idle, Held: from the next memory
                    forkSeekGo(forkNextRow(forkCurrentRow(), +1, forkSeekable));
                    return;
            }
        }

        private void forkSeekGo(int row)
        {
            if (row < 0) { forkSeekEnd("no memory to seek (a frequency and Scan ticked)"); return; }
            if (console.MOX) { forkSeekEnd("TX"); return; }
            if (!console.PowerOn && _forkFakeSignal == null)
            {
                // nothing to listen to: no seek (it would pass every memory over, one each 3 s)
                forkSeekEnd("the radio is off");
                return;
            }
            _forkSeek = ForkSeekState.Stepping;
            _forkSeekRow = row;
            _forkSeekTunedAt = DateTime.UtcNow;
            _forkSeekSum = 0;
            _forkSeekN = 0;
            if (forkCurrentRow() != row || !forkVfoOn(row)) forkTuneRow(row);
            _forkSeekVfo = console.VFOAFreq; // where the recall left it (a CW mode's pitch shift included)
            _forkSeekTimer.Start();
            forkUpdateButtons();
        }

        // the VFO already on that memory (play after stop resumes on it without tuning it again)
        private bool forkVfoOn(int row)
        {
            MemoryRecord r = forkRecord(row);
            return r != null && Math.Abs(console.VFOAFreq - r.RXFreq) < 0.0000005 && console.RX1DSPMode == r.DSPMode;
        }

        // stopped by hand on that memory; the seek goes on from it with play
        private void forkSeekStopOn(int row, string why)
        {
            _forkSeek = ForkSeekState.Stopped;
            _forkSeekRow = row;
            _forkSeekVfo = console.VFOAFreq;
            if (!_forkSeekTimer.Enabled) _forkSeekTimer.Start(); // watches for a hand tune
            if (why != null) HighDpi.Log("memory seek stopped on row " + row + ": " + why);
            forkUpdateButtons();
        }

        // no seek any more (a hand tune, a memory picked, TX, the window closed)
        private void forkSeekEnd(string why)
        {
            if (_forkSeek == ForkSeekState.Idle) return;
            _forkSeekTimer.Stop();
            _forkSeek = ForkSeekState.Idle;
            if (why != null) HighDpi.Log("memory seek ended on row " + _forkSeekRow + ": " + why);
            forkUpdateButtons();
        }

        // kept for MemoryForm.cs: a memory picked by hand ends the seek
        private void forkSeekStop(string why) { forkSeekEnd(why); }

        private double forkSignal(int row, out string how)
        {
            if (_forkFakeSignal != null)
            {
                how = "test";
                return _forkFakeSignal.Contains(row) ? 10 : -10;
            }
            return console.ForkSignalMargin(SeekMarginDb, out how);
        }

        private void forkSeekTick(object sender, EventArgs e)
        {
            if (_forkSeek == ForkSeekState.Idle) { _forkSeekTimer.Stop(); return; }
            // tuned by hand meanwhile (or another memory recalled): the seek ends there
            if (Math.Abs(console.VFOAFreq - _forkSeekVfo) > 0.0000005) { forkSeekEnd("the VFO moved"); return; }
            if (_forkSeek != ForkSeekState.Stepping) return;
            if (console.MOX) { forkSeekStopOn(_forkSeekRow, "TX"); return; }
            double ms = (DateTime.UtcNow - _forkSeekTunedAt).TotalMilliseconds;
            if (ms < SettleMs) return;
            string how;
            double m = forkSignal(_forkSeekRow, out how);
            if (!double.IsNaN(m)) { _forkSeekSum += m; _forkSeekN++; }
            int readMs = _forkSlow.Checked ? ReadSlowMs : ReadMs; // SLOW ticked or not: at once
            if (_forkSeekN >= MinSamples && ms >= SettleMs + readMs - SampleMs / 2)
            {
                double mean = _forkSeekSum / _forkSeekN;
                HighDpi.Log("memory seek row " + _forkSeekRow + " " + console.VFOAFreq.ToString("0.000000", CultureInfo.InvariantCulture) +
                    ": " + mean.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " dB over the threshold, " + _forkSeekN + " readings in " +
                    Math.Round(ms) + " ms" + (_forkSlow.Checked ? " (SLOW)" : "") + " (" + how + ")" + (mean >= 0 ? " -> held" : ""));
                if (mean >= 0)
                {
                    _forkSeek = ForkSeekState.Held; // the timer stays on: it watches for a hand tune
                    forkUpdateButtons();
                }
                else forkSeekGo(forkNextRow(_forkSeekRow, +1, forkSeekable));
                return;
            }
            if (ms > SettleMs + readMs + MaxWaitMs)
            {
                HighDpi.Log("memory seek row " + _forkSeekRow + ": no reading in " + (readMs + MaxWaitMs) + " ms (" + how + "), passed over");
                forkSeekGo(forkNextRow(_forkSeekRow, +1, forkSeekable));
            }
        }

        private void forkUpdateCount()
        {
            if (_forkCount == null) return;
            int n = dataGridView1.Rows.Count, cur = forkCurrentRow();
            string t = n == 0 ? "no memories" : (cur >= 0 ? (cur + 1).ToString() : "-") + " / " + n;
            if (_forkCount.Text != t)
            {
                _forkCount.Text = t;
                forkLayoutBar(); // the counter's room (at least "88 / 88"), SLOW after it
            }
            bool any = forkNextRow(-1, +1, forkHasFreq) >= 0;
            bool seek = _forkSeek != ForkSeekState.Idle;
            _forkBack.Enabled = any || seek;
            _forkNext.Enabled = any || seek;
            _forkPlay.Enabled = any || seek;
        }

        private void forkUpdateButtons()
        {
            if (_forkPlay == null) return;
            bool stepping = _forkSeek == ForkSeekState.Stepping, seek = _forkSeek != ForkSeekState.Idle;
            _forkPlay.Glyph = stepping ? ForkTransportButton.GlyphKind.Stop : ForkTransportButton.GlyphKind.Play;
            _forkPlay.Lit = stepping || _forkSeek == ForkSeekState.Held;
            string over = SeekMarginDb.ToString("0.#", CultureInfo.InvariantCulture) + " dB";
            toolTip1.SetToolTip(_forkPlay, stepping ? "STOP: stays on this memory (then play goes on)"
                : _forkSeek == ForkSeekState.Held ? "A signal here: play goes on to the next memory with a signal"
                : _forkSeek == ForkSeekState.Stopped ? "Go on to the next memory with a signal"
                : "SEEK: the memories one by one (Scan ticked), stopping on the next one with a signal\n" +
                  "(a signal: SQL on - at or over its level; SQL off - " + over + " over the noise floor)");
            toolTip1.SetToolTip(_forkBack, seek ? "BACK: the previous memory of the seek, stopped there"
                : "The previous memory in the list, tuned (from the first: the last)");
            toolTip1.SetToolTip(_forkNext, seek ? "FORWARD: the next memory of the seek"
                : "The next memory in the list, tuned (from the last: the first)");
            forkUpdateCount();
        }

        // test hook (THETIS_HIDPI_OPEN=memnav:<row>:<action>,<action>...): that row tuned, then one action a second
        // (next / back / play / stop / slow (SLOW ticked or unticked) / vfo (1 kHz up, as a hand tune) / w = wait),
        // the state logged after each
        public void ForkTestNav(int row, string[] actions)
        {
            forkSeekEnd(null);
            if (row >= 0 && row < dataGridView1.Rows.Count) forkTuneRow(row);
            forkLogState("start");
            int k = 0;
            Timer t = new Timer();
            t.Interval = 1000;
            t.Tick += (s, e) =>
            {
                if (k >= actions.Length) { t.Stop(); t.Dispose(); forkLogState("end"); return; }
                string a = actions[k++];
                if (a == "next") _forkNext.ForkClick();
                else if (a == "back") _forkBack.ForkClick();
                else if (a == "play" || a == "stop") _forkPlay.ForkClick();
                else if (a == "slow") _forkSlow.ForkClick();
                else if (a == "vfo") console.VFOAFreq = console.VFOAFreq + 0.001;
                forkLogState(a);
            };
            t.Start();
        }

        private void forkLogState(string tag)
        {
            int cr = forkCurrentRow();
            MemoryRecord rec = forkRecord(cr);
            HighDpi.Log("memnav " + tag + ": row " + cr + " (memory " + (rec != null ? rec.RXFreq.ToString("0.000000", CultureInfo.InvariantCulture) : "-") +
                ") count '" + _forkCount.Text + "' seek " + _forkSeek + " (seek row " + _forkSeekRow + ") play " + _forkPlay.Glyph + (_forkPlay.Lit ? " lit" : "") +
                (_forkSlow.Checked ? " SLOW" : "") + " VFO " + console.VFOAFreq.ToString("0.000000", CultureInfo.InvariantCulture) + " " + console.RX1DSPMode +
                " | bar " + _forkBar.Bounds + " back " + _forkBack.Bounds + " count " + _forkCount.Bounds + " slow " + _forkSlow.Bounds +
                " grid " + dataGridView1.Bounds + " form " + ClientSize);
        }
    }

    // A media-bar button as AltSDR's scan / memory transport: a 1 px border (lighter under the mouse), a dark ring,
    // a dark bevelled face (play green, stop red) and a light glyph
    public class ForkTransportButton : Control
    {
        public enum GlyphKind { Back, Play, Stop, Forward }

        private GlyphKind _glyph;
        private bool _lit, _hover;

        public ForkTransportButton(GlyphKind g)
        {
            _glyph = g;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false); // quick clicks: each one a step
            TabStop = false;
        }

        public GlyphKind Glyph { get { return _glyph; } set { if (_glyph != value) { _glyph = value; Invalidate(); } } }
        public bool Lit { get { return _lit; } set { if (_lit != value) { _lit = value; Invalidate(); } } }

        public void ForkClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        internal static Color Rgb(int v) { return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            Rectangle r = ClientRectangle;
            if (r.Width < 4 || r.Height < 4) return;
            // AltSDR's colours (its glyphButton)
            int top = 0x3A3A3A, bottom = 0x1A1A1A;
            if (_glyph == GlyphKind.Play)
            {
                top = !Enabled ? 0x2E3A2E : _lit ? 0x3CB43C : 0x1F6E1F;
                bottom = !Enabled ? 0x1A221A : _lit ? 0x1E781E : 0x0E3A0E;
            }
            else if (_glyph == GlyphKind.Stop)
            {
                top = _lit ? 0xA02020 : 0x6E1414;
                bottom = _lit ? 0x601010 : 0x3A0A0A;
            }
            float s = HighDpi.Scale;
            int line = Math.Max(1, HighDpi.Px(1));
            int inset = Math.Max(line + 1, (int)Math.Round(1.5f * s)); // the face 1.5 px in: a dark ring inside the border
            using (SolidBrush b = new SolidBrush(Rgb(0x101010))) g.FillRectangle(b, r);
            Rectangle face = Rectangle.Inflate(r, -inset, -inset);
            using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(face.X, face.Y - 1, face.Width, face.Height + 2), Rgb(top), Rgb(bottom), LinearGradientMode.Vertical))
                g.FillRectangle(lg, face);
            using (SolidBrush b = new SolidBrush(Rgb(_hover && Enabled ? 0xBFBFBF : 0x505050)))
            {
                g.FillRectangle(b, 0, 0, r.Width, line);
                g.FillRectangle(b, 0, r.Height - line, r.Width, line);
                g.FillRectangle(b, 0, 0, line, r.Height);
                g.FillRectangle(b, r.Width - line, 0, line, r.Height);
            }
            // the glyph in AltSDR's logical pixels, times the DPI scale
            float cx = r.Width / 2f, cy = r.Height / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (SolidBrush b = new SolidBrush(Rgb(Enabled ? 0xE6E6E6 : 0x6A6A6A)))
            {
                switch (_glyph)
                {
                    case GlyphKind.Stop:
                        g.FillRectangle(b, cx - 4.5f * s, cy - 4.5f * s, 9f * s, 9f * s);
                        break;
                    case GlyphKind.Play:
                        g.FillPolygon(b, new[] { new PointF(cx - 3.5f * s, cy - 5.5f * s), new PointF(cx - 3.5f * s, cy + 5.5f * s), new PointF(cx + 5f * s, cy) });
                        break;
                    case GlyphKind.Forward:
                        g.FillPolygon(b, new[] { new PointF(cx - 6f * s, cy - 4.5f * s), new PointF(cx - 6f * s, cy + 4.5f * s), new PointF(cx - 0.5f * s, cy) });
                        g.FillPolygon(b, new[] { new PointF(cx - 0.5f * s, cy - 4.5f * s), new PointF(cx - 0.5f * s, cy + 4.5f * s), new PointF(cx + 5f * s, cy) });
                        break;
                    case GlyphKind.Back:
                        g.FillPolygon(b, new[] { new PointF(cx + 6f * s, cy - 4.5f * s), new PointF(cx + 6f * s, cy + 4.5f * s), new PointF(cx + 0.5f * s, cy) });
                        g.FillPolygon(b, new[] { new PointF(cx + 0.5f * s, cy - 4.5f * s), new PointF(cx + 0.5f * s, cy + 4.5f * s), new PointF(cx - 5f * s, cy) });
                        break;
                }
            }
        }
    }

    // A check box as AltSDR's dialog check box (its dlgCheck, the scan control's FAST): an 11 px box (white, a
    // dark edge; under the mouse light blue with a blue edge), a black tick, the caption 9 px after it
    public class ForkCheck : Control
    {
        private bool _checked, _hover;
        public event EventHandler CheckedChanged;

        public ForkCheck(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked != value) { _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
        }

        public void ForkClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnFontChanged(EventArgs e) { Invalidate(); base.OnFontChanged(e); }

        private int box { get { return HighDpi.Px(11); } }

        public override Size GetPreferredSize(Size proposed)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                SizeF t = g.MeasureString(Text ?? "", Font);
                return new Size(box + HighDpi.Px(9) + (int)Math.Ceiling(t.Width) + HighDpi.Px(2), Math.Max(box, (int)Math.Ceiling(t.Height)));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            int b = box, y = (Height - b) / 2, line = Math.Max(1, HighDpi.Px(1));
            Rectangle r = new Rectangle(0, y, b, b);
            using (SolidBrush f = new SolidBrush(ForkTransportButton.Rgb(!Enabled ? 0x9A9A9A : _hover ? 0xE5F1FB : 0xFFFFFF))) g.FillRectangle(f, r);
            using (SolidBrush f = new SolidBrush(ForkTransportButton.Rgb(_hover && Enabled ? 0x0078D7 : 0x333333)))
            {
                g.FillRectangle(f, r.X, r.Y, b, line);
                g.FillRectangle(f, r.X, r.Bottom - line, b, line);
                g.FillRectangle(f, r.X, r.Y, line, b);
                g.FillRectangle(f, r.Right - line, r.Y, line, b);
            }
            if (_checked)
            {
                float s = HighDpi.Scale;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.Black, 1.6f * s))
                {
                    p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new[] { new PointF(r.X + 2.3f * s, y + 5.6f * s), new PointF(r.X + 4.6f * s, y + 8.0f * s), new PointF(r.X + 8.9f * s, y + 2.8f * s) });
                }
                g.SmoothingMode = SmoothingMode.None;
            }
            using (SolidBrush tb = new SolidBrush(Enabled ? ForeColor : SystemColors.GrayText))
            using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Text ?? "", Font, tb, new RectangleF(b + HighDpi.Px(9), 0, Width - b - HighDpi.Px(9), Height), sf);
        }
    }
}
