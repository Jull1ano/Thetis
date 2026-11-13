//=================================================================
// MemoryForm.Fork.cs - Jull1ano fork
//=================================================================
// The memory bar above the grid, as AltSDR's MEMORY tab (rc54 + rc55): back / play / next and "n / N".
//  - back / next: the previous / next row with a frequency becomes the current one and is tuned, as a click on
//    the row's header tunes it; round at the ends; the grid's order as shown (a sort reorders the list itself).
//  - play: a slow seek: from the row after the current one, each memory tuned in turn (the grid's current row
//    following), stopping on the first one with a signal (play lit); play again goes on to the next one with a
//    signal. While it steps play is a red stop, which stays on the memory it is on. Round at the end.
//  - "a signal": with SQL on (not FM) the RX1 meter at or over the squelch level; otherwise the passband SNR
//    (the info bar's PB SNR) at least SeekMarginDb over the noise. Each memory: SettleMs for the meter and the
//    AGC, then SeekSamples readings averaged.
//=================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Thetis
{
    public partial class MemoryForm
    {
        private enum ForkSeekState { Idle, Stepping, Held }

        private const int SettleMs = 400;     // after a memory is tuned: meter, AGC, the view re-centred
        private const int SampleMs = 60;      // the seek timer
        private const int SeekSamples = 4;    // readings averaged per memory
        private const int MaxWaitMs = 2500;   // no reading in this time (noise floor still settling): passed over
        private static double SeekMarginDb = 6.0;

        private Panel _forkBar;
        private ForkTransportButton _forkBack, _forkPlay, _forkNext;
        private Label _forkCount;
        private Timer _forkSeekTimer;
        private ForkSeekState _forkSeek = ForkSeekState.Idle;
        private int _forkSeekRow = -1;
        private DateTime _forkSeekTunedAt;
        private double _forkSeekVfo = double.NaN;
        private double _forkSeekSum;
        private int _forkSeekN;
        private string _forkSeekHow = "";
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
            _forkBar.Controls.Add(_forkBack);
            _forkBar.Controls.Add(_forkPlay);
            _forkBar.Controls.Add(_forkNext);
            _forkBar.Controls.Add(_forkCount);
            this.Controls.Add(_forkBar);

            _forkBack.Click += (s, e) => forkStep(-1);
            _forkNext.Click += (s, e) => forkStep(+1);
            _forkPlay.Click += (s, e) => forkPlayClick();

            _forkSeekTimer = new Timer();
            _forkSeekTimer.Interval = SampleMs;
            _forkSeekTimer.Tick += forkSeekTick;

            this.Layout += (s, e) => forkLayoutBar();
            this.VisibleChanged += (s, e) => { if (!Visible) forkSeekStop("the window closed"); };
            dataGridView1.CurrentCellChanged += (s, e) => forkUpdateCount();
            dataGridView1.RowsAdded += (s, e) => forkUpdateCount();
            dataGridView1.RowsRemoved += (s, e) => forkUpdateCount();
            dataGridView1.DataBindingComplete += (s, e) => forkUpdateCount();
            forkLayoutBar();
            forkUpdateButtons();
        }

        // the bar over the grid's width, at the top; the grid starts under it and keeps its bottom edge. Run on every
        // layout, so it comes out the same after the high-DPI passes rescale the form.
        private void forkLayoutBar()
        {
            if (_forkLayingOut || _forkBar == null) return;
            _forkLayingOut = true;
            try
            {
                int top = HighDpi.Px(3), barH = HighDpi.Px(24), gap = HighDpi.Px(3);
                int bw = HighDpi.Px(30), bh = HighDpi.Px(20);
                Rectangle g = dataGridView1.Bounds;
                Rectangle bar = new Rectangle(g.Left, top, Math.Max(1, g.Width), barH);
                if (_forkBar.Bounds != bar) _forkBar.Bounds = bar;
                int y = (barH - bh) / 2;
                _forkBack.Bounds = new Rectangle(0, y, bw, bh);
                _forkPlay.Bounds = new Rectangle(bw + gap, y, bw, bh);
                _forkNext.Bounds = new Rectangle(2 * (bw + gap), y, bw, bh);
                int lx = 3 * (bw + gap) + HighDpi.Px(6);
                _forkCount.Bounds = new Rectangle(lx, 0, Math.Max(1, bar.Width - lx), barH);
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

        private bool forkHasFreq(int row)
        {
            if (row < 0 || row >= dataGridView1.Rows.Count) return false;
            MemoryRecord r = dataGridView1.Rows[row].DataBoundItem as MemoryRecord;
            return r != null && r.RXFreq > 0;
        }

        // the next row with a frequency in that direction, round; -1 when no row has one
        private int forkNextRow(int from, int dir)
        {
            int n = dataGridView1.Rows.Count;
            if (n == 0) return -1;
            int start = from >= 0 ? from : (dir > 0 ? -1 : n);
            for (int k = 1; k <= n; k++)
            {
                int r = ((start + dir * k) % n + n) % n;
                if (forkHasFreq(r)) return r;
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

        private void forkStep(int dir)
        {
            forkSeekStop(null);
            int r = forkNextRow(forkCurrentRow(), dir);
            if (r >= 0) forkTuneRow(r);
        }

        private void forkPlayClick()
        {
            if (_forkSeek == ForkSeekState.Stepping)
            {
                forkSeekStop("stop");
                return;
            }
            forkSeekGo(forkNextRow(forkCurrentRow(), +1));
        }

        private void forkSeekGo(int row)
        {
            if (row < 0) { forkSeekStop("no memory with a frequency"); return; }
            if (console.MOX) { forkSeekStop("TX"); return; }
            if (!console.PowerOn && _forkFakeSignal == null)
            {
                // nothing to listen to: no seek (it would pass every memory over, one each 3 s)
                HighDpi.Log("memory seek: the radio is off, not started");
                forkSeekStop("the radio is off");
                return;
            }
            _forkSeek = ForkSeekState.Stepping;
            _forkSeekRow = row;
            _forkSeekTunedAt = DateTime.UtcNow;
            _forkSeekSum = 0;
            _forkSeekN = 0;
            forkTuneRow(row);
            _forkSeekVfo = console.VFOAFreq; // where the recall left it (a CW mode's pitch shift included)
            _forkSeekTimer.Start();
            forkUpdateButtons();
        }

        private void forkSeekStop(string why)
        {
            if (_forkSeek == ForkSeekState.Idle) return;
            _forkSeekTimer.Stop();
            _forkSeek = ForkSeekState.Idle;
            if (why != null) HighDpi.Log("memory seek stopped on row " + _forkSeekRow + ": " + why);
            forkUpdateButtons();
        }

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
            if (_forkSeek != ForkSeekState.Stepping) { _forkSeekTimer.Stop(); return; }
            if (console.MOX) { forkSeekStop("TX"); return; }
            // tuned by hand meanwhile (or another memory recalled): the seek ends there
            if (Math.Abs(console.VFOAFreq - _forkSeekVfo) > 0.0000005) { forkSeekStop("the VFO moved"); return; }
            double ms = (DateTime.UtcNow - _forkSeekTunedAt).TotalMilliseconds;
            if (ms < SettleMs) return;
            string how;
            double m = forkSignal(_forkSeekRow, out how);
            _forkSeekHow = how;
            if (!double.IsNaN(m)) { _forkSeekSum += m; _forkSeekN++; }
            if (_forkSeekN >= SeekSamples)
            {
                double mean = _forkSeekSum / _forkSeekN;
                HighDpi.Log("memory seek row " + _forkSeekRow + " " + console.VFOAFreq.ToString("0.000000", CultureInfo.InvariantCulture) +
                    ": " + mean.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " dB over the threshold (" + how + ")" + (mean >= 0 ? " -> held" : ""));
                if (mean >= 0)
                {
                    _forkSeekTimer.Stop();
                    _forkSeek = ForkSeekState.Held;
                    forkUpdateButtons();
                }
                else forkSeekGo(forkNextRow(_forkSeekRow, +1));
                return;
            }
            if (ms > SettleMs + MaxWaitMs)
            {
                HighDpi.Log("memory seek row " + _forkSeekRow + ": no reading in " + MaxWaitMs + " ms (" + how + "), passed over");
                forkSeekGo(forkNextRow(_forkSeekRow, +1));
            }
        }

        private void forkUpdateCount()
        {
            if (_forkCount == null) return;
            int n = dataGridView1.Rows.Count, cur = forkCurrentRow();
            _forkCount.Text = n == 0 ? "no memories" : (cur >= 0 ? (cur + 1).ToString() : "-") + " / " + n;
            bool any = forkNextRow(-1, +1) >= 0;
            _forkBack.Enabled = any;
            _forkNext.Enabled = any;
            _forkPlay.Enabled = any || _forkSeek != ForkSeekState.Idle;
        }

        private void forkUpdateButtons()
        {
            if (_forkPlay == null) return;
            bool stepping = _forkSeek == ForkSeekState.Stepping;
            _forkPlay.Glyph = stepping ? ForkTransportButton.GlyphKind.Stop : ForkTransportButton.GlyphKind.Play;
            _forkPlay.Lit = _forkSeek != ForkSeekState.Idle;
            string over = SeekMarginDb.ToString("0.#", CultureInfo.InvariantCulture) + " dB";
            toolTip1.SetToolTip(_forkPlay, stepping ? "STOP: stays on this memory (then play goes on)"
                : _forkSeek == ForkSeekState.Held ? "A signal here: play goes on to the next memory with a signal"
                : "SEEK: the memories one by one from the next one, stopping on the first with a signal\n" +
                  "(SQL on: at or over its level; SQL off: " + over + " over the noise floor)");
            toolTip1.SetToolTip(_forkBack, "The previous memory, tuned (from the first: the last)");
            toolTip1.SetToolTip(_forkNext, "The next memory, tuned (from the last: the first)");
            forkUpdateCount();
        }

        // test hook (THETIS_HIDPI_OPEN=memnav:<row>:<action>,<action>...): that row tuned, then one action a second
        // (next / back / play / stop / w = wait), the state logged after each
        public void ForkTestNav(int row, string[] actions)
        {
            forkSeekStop(null);
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
                else if (a == "vfo") console.VFOAFreq = console.VFOAFreq + 0.001; // as a hand tune: the seek ends
                forkLogState(a);
            };
            t.Start();
        }

        private void forkLogState(string tag)
        {
            int cr = forkCurrentRow();
            MemoryRecord rec = cr >= 0 ? dataGridView1.Rows[cr].DataBoundItem as MemoryRecord : null;
            HighDpi.Log("memnav " + tag + ": row " + cr + " (memory " + (rec != null ? rec.RXFreq.ToString("0.000000", CultureInfo.InvariantCulture) : "-") +
                ") count '" + _forkCount.Text + "' seek " + _forkSeek +
                " (seek row " + _forkSeekRow + ") play " + _forkPlay.Glyph + (_forkPlay.Lit ? " lit" : "") +
                " VFO " + console.VFOAFreq.ToString("0.000000", CultureInfo.InvariantCulture) + " " + console.RX1DSPMode +
                " | bar " + _forkBar.Bounds + " grid " + dataGridView1.Bounds + " form " + ClientSize);
        }
    }

    // A media-bar button as AltSDR's scan / memory transport: a dark bevelled face (play green, stop red) and a
    // light glyph; the border lights under the mouse
    public class ForkTransportButton : Control
    {
        public enum GlyphKind { Back, Play, Stop, Forward }

        private GlyphKind _glyph;
        private bool _lit, _hover;

        public ForkTransportButton(GlyphKind g)
        {
            _glyph = g;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false); // quick clicks: each one a step
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        public GlyphKind Glyph { get { return _glyph; } set { if (_glyph != value) { _glyph = value; Invalidate(); } } }
        public bool Lit { get { return _lit; } set { if (_lit != value) { _lit = value; Invalidate(); } } }

        public void ForkClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        private static Color rgb(int v) { return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            Rectangle r = ClientRectangle;
            if (r.Width < 4 || r.Height < 4) return;
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
            using (SolidBrush b = new SolidBrush(rgb(0x101010))) g.FillRectangle(b, r);
            int inset = Math.Max(1, HighDpi.Px(1));
            Rectangle face = Rectangle.Inflate(r, -inset, -inset);
            using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(face.X, face.Y - 1, face.Width, face.Height + 2), rgb(top), rgb(bottom), LinearGradientMode.Vertical))
                g.FillRectangle(lg, face);
            using (Pen p = new Pen(rgb(_hover && Enabled ? 0xBFBFBF : 0x505050), Math.Max(1, HighDpi.Px(1))))
            {
                p.Alignment = PenAlignment.Inset;
                g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
            // the glyph in AltSDR's logical pixels, times the DPI scale
            float s = HighDpi.Scale, cx = r.Width / 2f, cy = r.Height / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (SolidBrush b = new SolidBrush(rgb(Enabled ? 0xE6E6E6 : 0x6A6A6A)))
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
}
