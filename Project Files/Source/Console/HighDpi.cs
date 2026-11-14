/*  HighDpi.cs - Jull1ano fork of Thetis

Native high-DPI rendering (for example 200 % on a 4K monitor). Upstream Thetis runs DPI-unaware, so
Windows draws it at 1/2 size and stretches the bitmap: soft text and jagged lines. With high DPI on,
Thetis is system DPI aware: WinForms scales the forms by the system DPI and the custom drawing
(Direct2D spectrum, meters, ...) uses HighDpi.Scale.

On by default when Windows display scaling is above 100 %. Off: command line -nohidpi or environment
variable THETIS_HIDPI=0 (THETIS_HIDPI=1 or -hidpi force it on).

This program is free software; you can redistribute it and/or modify it under the terms of the GNU
General Public License as published by the Free Software Foundation; either version 2 of the License,
or (at your option) any later version.
*/

using System;
using System.Runtime.InteropServices;

namespace Thetis
{
    internal static class HighDpi
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);

        public static bool Enabled { get; private set; } = false;

        // system DPI / 96 when high DPI is on, else 1
        public static float Scale { get; private set; } = 1.0f;

        // must run before the first window is created
        public static void Init(string[] args)
        {
            bool want = true;   // default on; at 100 % scaling it changes nothing (Scale stays 1)
            foreach (string a in args)
            {
                if (string.Equals(a, "-hidpi", StringComparison.OrdinalIgnoreCase)) want = true;
                if (string.Equals(a, "-nohidpi", StringComparison.OrdinalIgnoreCase)) want = false;
            }
            string env = Environment.GetEnvironmentVariable("THETIS_HIDPI");
            if (env == "1") want = true;
            else if (env == "0") want = false;
            if (!want)
            {
                // pin the old (unaware, stretched) mode before WinForms reads its DpiAwareness setting (app.config)
                try { SetProcessDpiAwarenessContext(new IntPtr(-1)); } catch { }
                return;
            }

            bool ok = false;
            try { ok = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_SYSTEM_AWARE); } catch { }
            if (!ok)
            {
                try { ok = SetProcessDPIAware(); } catch { }
            }
            if (!ok) return;

            uint dpi = 96;
            try { dpi = GetDpiForSystem(); } catch { }
            if (dpi <= 96) return;  // 100 %: DPI awareness changes nothing, keep every code path as upstream
            Enabled = true;
            Scale = dpi / 96.0f;
            setTextStep(0); // the base text size (TEXT_BASE_BOOST) applies from the start

            // WinForms builds Control.DefaultFont from the system GUI font, which a DPI-aware process gets
            // already scaled (15.75 pt at 200 % when GDI+ started first, 7.875 pt otherwise). The forms were
            // designed with 8.25 pt Microsoft Sans Serif, and font-scaled forms size themselves from it.
            try
            {
                Log("DPI " + dpi + ", DefaultFont at start: " + System.Windows.Forms.Control.DefaultFont);
                var f = typeof(System.Windows.Forms.Control).GetField("defaultFont",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (f != null) f.SetValue(null, new System.Drawing.Font("Microsoft Sans Serif", 8.25f));
            }
            catch { }
        }

        public static int Px(int v) { return (int)Math.Round(v * Scale); }
        public static float Px(float v) { return v * Scale; }
        public static int Unpx(int v) { return (int)Math.Round(v / Scale); }

        // Direct2D stroke width: hairlines (1 px or less: grid, cursor) stay one device pixel, crisp as in
        // NereusSDR; wider lines (markers, filter edges) keep their designed weight
        public static float Stroke(float w) { return w <= 1f ? w : w * Scale; }

        // pixel rows per waterfall data row, so the waterfall covers the same time as at 96 DPI
        public static int WaterfallRowsPerUpdate { get { return Math.Max(1, (int)Math.Round(Scale)); } }

        // Move a control by dy pixels (used for the VFO small-digit boxes: at high DPI the big digits behind
        // them sit a few pixels higher, because edit-control margins do not scale, and their tops showed)
        public static void Nudge(System.Windows.Forms.Control c, int dy)
        {
            if (!Enabled || c == null || dy == 0) return;
            c.Top += dy;
        }

        // Keep control c inside box (same parent), at least 'gap' pixels above its bottom. Used for the VFO
        // digit-hover bars: auto-sized text boxes come out a bit shorter than the scaled design, and the bar
        // then covered the box's bottom border.
        public static void KeepAbove(System.Windows.Forms.Control c, System.Windows.Forms.Control box, int gap)
        {
            if (!Enabled || c == null || box == null || c.Parent != box.Parent) return;
            int maxBottom = box.Bottom - gap;
            if (c.Bottom > maxBottom) c.Top = maxBottom - c.Height;
        }

        // Grow control c to the left by dx pixels (its right edge stays)
        public static void GrowLeft(System.Windows.Forms.Control c, int dx)
        {
            if (!Enabled || c == null || dx <= 0) return;
            c.Left -= dx;
            c.Width += dx;
        }

        // ---------------------------------------------------------------- text size option (Setup > Appearance)

        private const string TEXT_SIZE_FILE = "hidpi_text_size.txt";

        // 0 = default, 1 = +1, 2 = +2 (one step = one point on the 8.25 pt base font, every font scales alike)
        public static int TextStep { get; private set; } = 0;
        public static float TextScale { get; private set; } = 1.0f;

        // Direct2D / custom drawn text: point size -> pixels uses this (DPI and text size)
        public static float FontScale { get { return Scale * TextScale; } }

        // pixel sizes of things that hold text (status bar fields, list rows)
        public static int PxText(int v) { return (int)Math.Round(v * Scale * TextScale); }

        public static void LoadTextStep(string appDataPath)
        {
            if (!Enabled) return;
            try
            {
                string f = System.IO.Path.Combine(appDataPath ?? "", TEXT_SIZE_FILE);
                if (System.IO.File.Exists(f) && int.TryParse(System.IO.File.ReadAllText(f).Trim(), out int n))
                    setTextStep(n);
            }
            catch { }
        }

        public static void SaveTextStep(string appDataPath, int step)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(appDataPath ?? "", TEXT_SIZE_FILE), Math.Max(0, Math.Min(2, step)).ToString());
            }
            catch { }
        }

        // smooth spectrum line (Display.SmoothTrace): on unless the file says 0; THETIS_SMOOTH_TRACE=0/1 overrides
        private const string SMOOTH_TRACE_FILE = "hidpi_smooth_trace.txt";

        public static bool LoadSmoothTrace(string appDataPath)
        {
            string env = Environment.GetEnvironmentVariable("THETIS_SMOOTH_TRACE");
            if (env == "0") return false;
            if (env == "1") return true;
            try
            {
                string f = System.IO.Path.Combine(appDataPath ?? "", SMOOTH_TRACE_FILE);
                if (System.IO.File.Exists(f)) return System.IO.File.ReadAllText(f).Trim() != "0";
            }
            catch { }
            return true;
        }

        public static void SaveSmoothTrace(string appDataPath, bool on)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(appDataPath ?? "", SMOOTH_TRACE_FILE), on ? "1" : "0");
            }
            catch { }
        }

        // a small whole-number fork setting kept in its own file in the settings folder
        public static int LoadForkInt(string appDataPath, string file, int def, int min, int max)
        {
            try
            {
                string f = System.IO.Path.Combine(appDataPath ?? "", file);
                if (System.IO.File.Exists(f) && int.TryParse(System.IO.File.ReadAllText(f).Trim(), out int n))
                    return Math.Max(min, Math.Min(max, n));
            }
            catch { }
            return def;
        }

        public static void SaveForkInt(string appDataPath, string file, int value)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(appDataPath ?? "", file), value.ToString());
            }
            catch { }
        }

        public static float LoadForkFloat(string appDataPath, string file, float def, float min, float max)
        {
            try
            {
                string f = System.IO.Path.Combine(appDataPath ?? "", file);
                if (System.IO.File.Exists(f) && float.TryParse(System.IO.File.ReadAllText(f).Trim(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)
                    && !float.IsNaN(v))
                    return Math.Max(min, Math.Min(max, v));
            }
            catch { }
            return def;
        }

        public static void SaveForkFloat(string appDataPath, string file, float value)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(appDataPath ?? "", file),
                    value.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
        }

        public const string FFT_SMOOTH_FILE = "hidpi_fft_smooth.txt";
        public const string FFT_FLATTEN_FILE = "hidpi_fft_flatten.txt"; // Jull1ano fork: Flatten %

        // The original Thetis at 200 % is drawn at 96 DPI and stretched by Windows: the stretch blurs the text,
        // which then looks heavier and about 2 px taller than the same font drawn sharp at 192 DPI (measured on
        // the console labels: 18 vs 16 px high, 16-20 % more ink). The default text size here is therefore this
        // much larger (points on the 8.25 pt base), so Default looks like the original; +1 / +2 add to it.
        // THETIS_TEXT_BOOST=<points> overrides it (for comparisons).
        public const float TEXT_BASE_BOOST = 0.75f;
        public static float BaseBoost
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("THETIS_TEXT_BOOST");
                if (!string.IsNullOrEmpty(env) && float.TryParse(env, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float b) && b >= 0f && b <= 4f)
                    return b;
                return TEXT_BASE_BOOST;
            }
        }

        private static float _boost = TEXT_BASE_BOOST;

        private static void setTextStep(int n)
        {
            TextStep = Math.Max(0, Math.Min(2, n));
            _boost = BaseBoost;
            TextScale = ScaledFontSize(8.25f) / 8.25f;
        }

        // the size a font of pt points gets here: Default / +1 / +2 scale every font alike; the base boost is for
        // the small UI text only - full at 8.25 pt, tapering to none at 11 pt: Windows' stretch of the original
        // makes small text look heavier, a 12 pt list (Finder) the original shows about as it is
        public static float StepScale { get { return (8.25f + TextStep) / 8.25f; } }
        public static float BoostFor(float pt) { return _boost * Math.Max(0f, Math.Min(1f, (11f - pt) / (11f - 8.25f))); }
        public static float ScaledFontSize(float pt) { return pt * StepScale + BoostFor(pt); }

        // pixel sizes that hold text of a given font size (list rows ...)
        public static int PxFont(int v, float pt) { return (int)Math.Round(v * Scale * ScaledFontSize(pt) / pt); }

        // controls whose fonts the program sets itself and that must keep their size (VFO digits, meter text)
        private static readonly System.Collections.Generic.HashSet<string> _keepFont = new System.Collections.Generic.HashSet<string>
        {
            "txtVFOAFreq", "txtVFOAMSD", "txtVFOALSD", "txtVFOBFreq", "txtVFOBMSD", "txtVFOBLSD", "txtMultiText", "txtRX2Meter",
            "lblPS", "lblFB" // info bar: it swaps these fonts itself (9 / 6.75 pt) as its width changes
        };

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Form, object> _prepared =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Form, object>();

        // Everything a form needs at high DPI beyond WinForms' own scaling: list row heights, text size.
        // Runs once per form; the console and Setup call it before they are shown, other forms get it
        // from the watcher when they open.
        public static void PrepareForm(System.Windows.Forms.Form form)
        {
            if (!Enabled || form == null) return;
            if (_prepared.TryGetValue(form, out object _)) return;
            _prepared.Add(form, null);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var t = new System.Collections.Generic.List<string>();
                normalizeFontScaledForm(form); t.Add("normalize " + sw.ElapsedMilliseconds);
                normalizeNestedFontControls(form); t.Add("nested " + sw.ElapsedMilliseconds);
                applyTextScale(form); t.Add("text " + sw.ElapsedMilliseconds);
                FixOwnerDrawLists(form); t.Add("lists " + sw.ElapsedMilliseconds);
                FixGrids(form, form); t.Add("grids " + sw.ElapsedMilliseconds);
                ScaleButtonImages(form); t.Add("icons " + sw.ElapsedMilliseconds);
                FixAutoSizeBounds(form); t.Add("autosize " + sw.ElapsedMilliseconds);
                ApplyEditPadding(form); t.Add("padding " + sw.ElapsedMilliseconds);
                if (sw.ElapsedMilliseconds > 40) Log("PrepareForm " + form.Name + " " + sw.ElapsedMilliseconds + " ms: " + string.Join(", ", t));
            }
            catch (Exception ex) { Log("PrepareForm " + form.Name + ": " + ex); }
        }

        // A font-scaled form (AutoScaleMode.Font, most dialogs) lays out at its font's height ratio: about 2.17
        // at 200 % instead of 2, so it came out 8 % bigger than the original Thetis shows it, with the text
        // relatively smaller in it. Its layout is brought to exactly the DPI scale here (bounds only, fonts stay),
        // the same as the original's 96 DPI layout stretched by Windows; sizes saved later use this scale.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.ContainerControl, float[]> _layoutScale =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.ContainerControl, float[]>();

        // A font-scaled user control inside a window (Setup's gradient preset picker ...) gets its own size from
        // the window (exactly the DPI scale) but lays its contents out at about 2.17, so anchored contents no
        // longer fit and get squeezed (the preset list vanished, "Set" was a thin strip). When the contents
        // overflow the control, they are brought to the DPI scale too (the control's own size stays).
        private static void normalizeNestedFontControls(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
            {
                // (the info bar places its labels itself: left alone)
                if (c is System.Windows.Forms.UserControl uc && uc.AutoScaleMode == System.Windows.Forms.AutoScaleMode.Font &&
                    !(uc is ucInfoBar) && !_layoutScale.TryGetValue(uc, out float[] _))
                {
                    float fx = FormScale(uc), fy = FormScale(uc, true);
                    _layoutScale.Add(uc, new float[] { Scale, Scale });
                    int right = 0, bottom = 0;
                    foreach (System.Windows.Forms.Control ch in uc.Controls) { right = Math.Max(right, ch.Right); bottom = Math.Max(bottom, ch.Bottom); }
                    bool overflows = right > uc.ClientSize.Width + Px(1) || bottom > uc.ClientSize.Height + Px(1);
                    // The QuickRecall pad (< V >) sizes itself to its buttons, so they never overflow it: it grew with
                    // them instead (3 x 43 + 1 px at 200 % instead of 3 x 40 + 1) and ran past the right border of the
                    // group box around it. Its buttons go back on the designer's grid at the DPI scale (edges rounded:
                    // all three the same size, touching), then the pad fits itself around them again.
                    ucQuickRecall pad = uc as ucQuickRecall;
                    if ((overflows || pad != null) && (Math.Abs(fx - Scale) > 0.02f || Math.Abs(fy - Scale) > 0.02f))
                    {
                        Log("normalize nested " + uc.Name + " (" + uc.GetType().Name + ") " + fx.ToString("0.000") + " x " + fy.ToString("0.000"));
                        uc.SuspendLayout();
                        try
                        {
                            foreach (System.Windows.Forms.Control ch in uc.Controls)
                                if (pad != null) snapToDpiGrid(ch, fx, fy);
                                else ch.Scale(new System.Drawing.SizeF(Scale / fx, Scale / fy));
                        }
                        finally { uc.ResumeLayout(true); }
                        if (pad != null)
                        {
                            pad.ForkFitToButtons();
                            Log("  pad " + pad.Width + " x " + pad.Height + " at " + pad.Left + "," + pad.Top + " in " +
                                (pad.Parent != null ? pad.Parent.Name + " " + pad.Parent.Width + " x " + pad.Parent.Height : "-"));
                        }
                    }
                }
                if (c.HasChildren) normalizeNestedFontControls(c);
            }
        }

        // a control laid out at factor f: its edges back in designer units, then at the DPI scale
        private static void snapToDpiGrid(System.Windows.Forms.Control c, float fx, float fy)
        {
            int l = (int)Math.Round(c.Left / fx), t = (int)Math.Round(c.Top / fy);
            int r = (int)Math.Round(c.Right / fx), b = (int)Math.Round(c.Bottom / fy);
            c.Bounds = new System.Drawing.Rectangle(Px(l), Px(t), Math.Max(1, Px(r) - Px(l)), Math.Max(1, Px(b) - Px(t)));
        }

        private static void normalizeFontScaledForm(System.Windows.Forms.Form form)
        {
            if (form.AutoScaleMode != System.Windows.Forms.AutoScaleMode.Font) return;
            if (_layoutScale.TryGetValue(form, out float[] _)) return;
            float fx = FormScale(form), fy = FormScale(form, true);
            _layoutScale.Add(form, new float[] { Scale, Scale });
            if (Math.Abs(fx - Scale) < 0.02f && Math.Abs(fy - Scale) < 0.02f) return;
            form.SuspendLayout();
            try
            {
                Log("normalize " + form.Name + " " + fx.ToString("0.000") + " x " + fy.ToString("0.000") + " -> " + Scale);
                form.Scale(new System.Drawing.SizeF(Scale / fx, Scale / fy));
            }
            finally { form.ResumeLayout(true); }
        }

        private delegate IntPtr CbtProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, CbtProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        private static CbtProc _cbtProc; // kept alive for the hook
        private static IntPtr _cbtHook = IntPtr.Zero;

        // HCBT_ACTIVATE comes while a window is being shown, before its first paint: a form prepared there
        // never shows at its unprepared size
        private static IntPtr cbtHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == 5) // HCBT_ACTIVATE
            {
                try
                {
                    if (System.Windows.Forms.Control.FromHandle(wParam) is System.Windows.Forms.Form f) PrepareForm(f);
                }
                catch { }
            }
            return CallNextHookEx(_cbtHook, code, wParam, lParam);
        }

        private static bool _watching = false;
        public static void StartFormWatcher()
        {
            if (!Enabled || _watching) return;
            _watching = true;
            try
            {
                _cbtProc = cbtHook;
                _cbtHook = SetWindowsHookEx(5 /* WH_CBT */, _cbtProc, IntPtr.Zero, GetCurrentThreadId());
            }
            catch (Exception ex) { Log("CBT hook: " + ex.Message); }
            System.Windows.Forms.Application.Idle += (s, e) =>
            {
                try
                {
                    foreach (System.Windows.Forms.Form f in System.Windows.Forms.Application.OpenForms)
                        PrepareForm(f);
                }
                catch { }
            };
        }

        // owner-drawn fixed-height lists: WinForms does not scale their row height, the text then overlaps
        public static void FixOwnerDrawLists(System.Windows.Forms.Control root)
        {
            if (!Enabled || root == null) return;
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                if (c is System.Windows.Forms.ListBox lb && lb.DrawMode == System.Windows.Forms.DrawMode.OwnerDrawFixed)
                {
                    int want = (int)Math.Round(lb.ItemHeight * Scale * TextScale);
                    if (lb.ItemHeight < lb.Font.Height + Px(2) && want > lb.ItemHeight)
                        lb.ItemHeight = Math.Min(255, want);
                }
                if (c.HasChildren) FixOwnerDrawLists(c);
            }
        }

        // the scale WinForms gave a form: a font-scaled one is about 2.17 at 200 % (its font's height ratio), a
        // DPI-scaled one exactly 2; code that sets fixed sizes must use the form's own factor, or it cuts the form
        public static float FormScale(System.Windows.Forms.ContainerControl c, bool vertical = false)
        {
            if (!Enabled || c == null) return 1f;
            if (_layoutScale.TryGetValue(c, out float[] rec)) return vertical ? rec[1] : rec[0];
            System.Drawing.SizeF cur = c.CurrentAutoScaleDimensions;
            if (c.AutoScaleMode == System.Windows.Forms.AutoScaleMode.Font && cur.Width > 0 && cur.Height > 0)
                return vertical ? cur.Height / 13f : cur.Width / 6f; // designer base: 6 x 13 (8.25 pt MS Sans Serif)
            return Scale;
        }

        public static int PxForm(System.Windows.Forms.ContainerControl c, int v, bool vertical = false)
        {
            return (int)Math.Round(v * FormScale(c, vertical));
        }

        public static int UnpxForm(System.Windows.Forms.ContainerControl c, int v, bool vertical = false)
        {
            return (int)Math.Round(v / FormScale(c, vertical));
        }

        // a restored window never larger than the working area of the screen it opens on
        public static void FitOnScreen(System.Windows.Forms.Form f)
        {
            if (!Enabled || f == null) return;
            try
            {
                System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromRectangle(f.Bounds).WorkingArea;
                if (f.Width > wa.Width) f.Width = wa.Width;
                if (f.Height > wa.Height) f.Height = wa.Height;
            }
            catch { }
        }

        // Auto-sized check boxes, radio buttons and labels: WinForms works out their size here as if the text were
        // twice as large ("RX1" radio 166 x 64 px instead of about 92 x 34). The text draws at its real size, but
        // the oversized box covers the neighbours ("RX1 Sub" -> ":1 Sub"). They get the size their text needs
        // (measured the way they draw it), and keep it up to date when their text changes.
        public static void FixAutoSizeBounds(System.Windows.Forms.Control root)
        {
            if (!Enabled || root == null) return;
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                if (c.AutoSize && !string.IsNullOrEmpty(c.Text) && (plainCheck(c) || c is System.Windows.Forms.Label))
                {
                    try
                    {
                        System.Drawing.Size want = autoSizeFor(c);
                        if (c.Width > want.Width * 1.2f || c.Height > want.Height * 1.3f)
                        {
                            applyAutoSize(c, want);
                            c.TextChanged += autoSizeOnText;
                            c.FontChanged += autoSizeOnText;
                        }
                    }
                    catch (Exception ex) { Log("FixAutoSizeBounds " + c.Name + ": " + ex.Message); }
                }
                if (c.HasChildren) FixAutoSizeBounds(c);
            }
        }

        private static System.Drawing.Size autoSizeFor(System.Windows.Forms.Control c)
        {
            System.Drawing.SizeF t = measure(c, c.Font, 0);
            bool check = plainCheck(c);
            int w = (check ? Px(20) : 0) + (int)Math.Ceiling(t.Width) + c.Padding.Horizontal;
            int h = (int)Math.Ceiling(t.Height) + (check ? Px(4) : 0);
            if (check) h = Math.Max(h, Px(17));
            return new System.Drawing.Size(w, h + c.Padding.Vertical);
        }

        private static void applyAutoSize(System.Windows.Forms.Control c, System.Drawing.Size want)
        {
            System.Windows.Forms.AnchorStyles a = c.Anchor;
            bool growLeft = (a & System.Windows.Forms.AnchorStyles.Right) != 0 && (a & System.Windows.Forms.AnchorStyles.Left) == 0;
            int right = c.Right;
            c.AutoSize = false;
            c.Size = want;
            if (growLeft) c.Left = right - want.Width;
        }

        private static void autoSizeOnText(object sender, EventArgs e)
        {
            if (sender is System.Windows.Forms.Control c && !string.IsNullOrEmpty(c.Text))
            {
                try
                {
                    System.Drawing.Size want = autoSizeFor(c);
                    if (want != c.Size) applyAutoSize(c, want);
                }
                catch { }
            }
        }

        // ---------------------------------------------------------------- inner padding of edit fields
        // Numbers in spin boxes sat right against the arrows, combo texts against the left border. Edit fields
        // get a margin of 3 units on each side where the text has the room (the widest value a spin box can
        // show), drop-down lists draw their text with the same left padding. Never less than Windows gave.

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct COMBOBOXINFO
        {
            public int cbSize;
            public int rcItemL, rcItemT, rcItemR, rcItemB;
            public int rcButtonL, rcButtonT, rcButtonR, rcButtonB;
            public int stateButton;
            public IntPtr hwndCombo, hwndItem, hwndList;
        }
        [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO pcbi);

        private const int EM_SETMARGINS = 0xD3, EM_GETMARGINS = 0xD4, EC_LEFTMARGIN = 1, EC_RIGHTMARGIN = 2;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object> _padded =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object>();
        // spin boxes widened once for their padding
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object> _grown =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object>();
        // the margins Windows gave each field (never less than these)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, int[]> _baseMargins =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, int[]>();

        public static int EditPad { get { return Px(3); } }        // left (and drop-down lists)
        public static int EditPadRight { get { return Px(4); } }   // right: numbers sit there

        public static void ApplyEditPadding(System.Windows.Forms.Control root)
        {
            if (!Enabled || root == null) return;
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                try
                {
                    if (_keepFont.Contains(c.Name)) { }
                    else if (c is System.Windows.Forms.UpDownBase ud)
                    {
                        foreach (System.Windows.Forms.Control ch in ud.Controls)
                            if (ch is System.Windows.Forms.TextBox tb) registerPad(tb, ud);
                        continue; // its own children are done
                    }
                    else if (c is System.Windows.Forms.TextBox tb && !tb.Multiline) registerPad(tb, null);
                    else if (c is System.Windows.Forms.ComboBox cb)
                    {
                        if (cb.DropDownStyle == System.Windows.Forms.ComboBoxStyle.DropDownList) registerComboListPad(cb);
                        else if (cb.DropDownStyle == System.Windows.Forms.ComboBoxStyle.DropDown) registerPad(cb, null);
                        continue;
                    }
                }
                catch (Exception ex) { Log("ApplyEditPadding " + c.Name + ": " + ex.Message); }
                if (c.HasChildren) ApplyEditPadding(c);
            }
        }

        private static void registerPad(System.Windows.Forms.Control edit, System.Windows.Forms.UpDownBase owner)
        {
            if (_padded.TryGetValue(edit, out object _)) return;
            _padded.Add(edit, owner);
            EventHandler again = (s, e) => padEdit(edit);
            edit.HandleCreated += again;
            edit.FontChanged += again;
            edit.SizeChanged += again;
            edit.TextChanged += again; // the room follows the text shown now
            if (edit.IsHandleCreated) padEdit(edit);
        }

        // the text the field shows now (re-checked on every change; a spin box's widest value left no room at all)
        private static string widestText(System.Windows.Forms.Control edit)
        {
            return edit.Text ?? "";
        }

        private static void padEdit(System.Windows.Forms.Control edit)
        {
            if (!edit.IsHandleCreated) return;
            try
            {
                IntPtr h = edit.Handle;
                if (edit is System.Windows.Forms.ComboBox cbx)
                {
                    COMBOBOXINFO ci = new COMBOBOXINFO(); ci.cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO));
                    if (!GetComboBoxInfo(cbx.Handle, ref ci) || ci.hwndItem == IntPtr.Zero) return;
                    h = ci.hwndItem;
                }
                int textW = System.Windows.Forms.TextRenderer.MeasureText(widestText(edit), edit.Font, System.Drawing.Size.Empty,
                    System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.SingleLine).Width;
                int room = (edit is System.Windows.Forms.ComboBox ? edit.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth : edit.ClientSize.Width) - textW;
                room -= Px(1); // the caret
                long cur = SendMessage(h, EM_GETMARGINS, IntPtr.Zero, IntPtr.Zero).ToInt64();
                int l = (int)(cur & 0xFFFF), r = (int)((cur >> 16) & 0xFFFF);
                if (!_baseMargins.TryGetValue(edit, out int[] baseM)) { baseM = new int[] { l, r }; _baseMargins.Add(edit, baseM); }
                // the padding goes on the side the text sits on; the other side keeps what Windows gave, or less when
                // the room is short (Windows ignores margins that do not fit together: "2800" got none)
                bool rightAligned = edit is System.Windows.Forms.TextBox tbx && tbx.TextAlign == System.Windows.Forms.HorizontalAlignment.Right;
                // a spin box too narrow for its number plus the padding ("2800" in the TX filter High box) grows
                // to the left by up to 4 units, once, where nothing else sits
                if (rightAligned && room < EditPadRight && _padded.TryGetValue(edit, out object ow) &&
                    ow is System.Windows.Forms.UpDownBase udb && udb.Parent != null && !_grown.TryGetValue(udb, out object _))
                {
                    _grown.Add(udb, null);
                    int grow = Math.Min(Px(4), EditPadRight - Math.Max(0, room));
                    System.Drawing.Rectangle nb = new System.Drawing.Rectangle(udb.Left - grow, udb.Top, udb.Width + grow, udb.Height);
                    bool free = nb.Left >= 0;
                    foreach (System.Windows.Forms.Control sib in udb.Parent.Controls)
                        if (free && sib != udb && sib.Bounds.IntersectsWith(nb)) free = false;
                    if (free)
                    {
                        udb.Bounds = nb; // SizeChanged pads it again with the new room
                        return;
                    }
                }
                int nl, nr;
                if (room <= 0) { nl = baseM[0]; nr = baseM[1]; }
                else if (rightAligned)
                {
                    nr = Math.Min(Math.Max(EditPadRight, baseM[1]), room);
                    nl = Math.Max(0, Math.Min(baseM[0], room - nr));
                }
                else
                {
                    nl = Math.Min(Math.Max(EditPad, baseM[0]), room);
                    nr = Math.Max(0, Math.Min(Math.Max(baseM[1], edit is System.Windows.Forms.TextBox ? EditPad : 0), room - nl));
                }
                if (nl != l || nr != r)
                    SendMessage(h, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), (IntPtr)((nl & 0xFFFF) | (nr << 16)));
                if (_logFit)
                {
                    long after = SendMessage(h, EM_GETMARGINS, IntPtr.Zero, IntPtr.Zero).ToInt64();
                    Log("pad " + (edit.Parent != null ? edit.Parent.Name + "/" : "") + edit.Name + " '" + edit.Text + "' client " + edit.ClientSize.Width +
                        " text " + textW + " room " + room + " base " + baseM[0] + "," + baseM[1] + " want " + nl + "," + nr +
                        " now " + (after & 0xFFFF) + "," + ((after >> 16) & 0xFFFF) + (rightAligned ? " right" : ""));
                }
            }
            catch (Exception ex) { Log("padEdit " + edit.Name + ": " + ex.Message); }
        }

        // a drop-down list has no edit field: its text is drawn here, with the padding
        private static void registerComboListPad(System.Windows.Forms.ComboBox cb)
        {
            if (cb.DrawMode != System.Windows.Forms.DrawMode.Normal || _padded.TryGetValue(cb, out object _)) return;
            _padded.Add(cb, null);
            if (cb.IsHandleCreated) toOwnerDraw(cb);
            else
            {
                EventHandler once = null;
                once = (s, e) => { cb.HandleCreated -= once; cb.BeginInvoke(new Action(() => toOwnerDraw(cb))); };
                cb.HandleCreated += once;
            }
        }

        private static void toOwnerDraw(System.Windows.Forms.ComboBox cb)
        {
            try
            {
                if (cb.DrawMode != System.Windows.Forms.DrawMode.Normal) return;
                int before = cb.Height, itemH = cb.ItemHeight;
                cb.DrawItem += drawPaddedComboItem;
                cb.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
                cb.ItemHeight = itemH;
                if (cb.Height != before) cb.ItemHeight = Math.Max(1, itemH + (before - cb.Height)); // the same height as before
            }
            catch (Exception ex) { Log("toOwnerDraw " + cb.Name + ": " + ex.Message); }
        }

        private static void drawPaddedComboItem(object sender, System.Windows.Forms.DrawItemEventArgs e)
        {
            System.Windows.Forms.ComboBox cb = sender as System.Windows.Forms.ComboBox;
            if (cb == null) return;
            bool modern = Thetis.ForkTheme.ComboModern; // a skin's modern drop-downs (ForkTheme.cs): its list colours
            bool selItem = (e.State & System.Windows.Forms.DrawItemState.Selected) != 0;
            if (modern)
                using (System.Drawing.SolidBrush bb = new System.Drawing.SolidBrush(selItem ? Thetis.ForkTheme.ComboListSelect : Thetis.ForkTheme.ComboListBack))
                    e.Graphics.FillRectangle(bb, e.Bounds);
            else e.DrawBackground();
            if (e.Index >= 0 && e.Index < cb.Items.Count)
            {
                string t = cb.GetItemText(cb.Items[e.Index]);
                bool sel = selItem;
                System.Drawing.Color fore = !cb.Enabled ? System.Drawing.SystemColors.GrayText : sel ? (modern ? Thetis.ForkTheme.ComboListSelectText : System.Drawing.SystemColors.HighlightText) : (modern ? Thetis.ForkTheme.ComboText : cb.ForeColor);
                int textW = System.Windows.Forms.TextRenderer.MeasureText(t, cb.Font, System.Drawing.Size.Empty,
                    System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.SingleLine).Width;
                int pad = Math.Max(Px(1), Math.Min(EditPad, e.Bounds.Width - textW - Px(1)));
                System.Drawing.Rectangle r = new System.Drawing.Rectangle(e.Bounds.X + pad, e.Bounds.Y, Math.Max(1, e.Bounds.Width - pad), e.Bounds.Height);
                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, t, cb.Font, r, fore,
                    System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.Left |
                    System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.NoPrefix |
                    System.Windows.Forms.TextFormatFlags.EndEllipsis | System.Windows.Forms.TextFormatFlags.NoPadding);
            }
            e.DrawFocusRectangle();
        }

        // ---------------------------------------------------------------- readable text on coloured fields
        // The VFO band text ("60M General RX") is green on the out-of-band grey: 4.0:1 for the active VFO, about
        // 1.1:1 for the dimmed one (the same in the original). A field registered here keeps at least 4.5:1
        // (WCAG AA): its background is darkened to half (the hue stays, so out-of-band still shows), then the
        // text is lightened only as far as needed.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object> _readable =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object>();
        private static bool _fixingContrast = false;
        public const double MIN_CONTRAST = 4.5;

        public static void EnsureReadable(System.Windows.Forms.Control c)
        {
            if (c == null || _readable.TryGetValue(c, out object _)) return;
            _readable.Add(c, null);
            c.ForeColorChanged += (s, e) => fixContrast(c);
            c.BackColorChanged += (s, e) => fixContrast(c);
            fixContrast(c);
        }

        private static double lin(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        public static double Luminance(System.Drawing.Color c) { return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B); }
        public static double Contrast(System.Drawing.Color a, System.Drawing.Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
        private static System.Drawing.Color scaleColor(System.Drawing.Color c, double k)
        {
            return System.Drawing.Color.FromArgb(c.A, (int)Math.Max(0, Math.Min(255, Math.Round(c.R * k))),
                (int)Math.Max(0, Math.Min(255, Math.Round(c.G * k))), (int)Math.Max(0, Math.Min(255, Math.Round(c.B * k))));
        }
        private static System.Drawing.Color mixColor(System.Drawing.Color a, System.Drawing.Color b, double t)
        {
            return System.Drawing.Color.FromArgb(a.A, (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // the text colour moved toward light (or dark) just far enough for MIN_CONTRAST on back
        private static System.Drawing.Color readableFore(System.Drawing.Color fore, System.Drawing.Color back)
        {
            bool lighter = Luminance(fore) >= Luminance(back);
            System.Drawing.Color target = System.Drawing.Color.White;
            if (lighter)
            {
                int max = Math.Max(fore.R, Math.Max(fore.G, fore.B));
                // first brighten in its own hue, then toward white
                System.Drawing.Color bright = max > 0 ? scaleColor(fore, 255.0 / max) : System.Drawing.Color.White;
                if (Contrast(bright, back) >= MIN_CONTRAST) { target = bright; }
                else { fore = bright; target = System.Drawing.Color.White; }
            }
            else target = System.Drawing.Color.Black;
            double lo = 0, hi = 1;
            for (int i = 0; i < 14; i++)
            {
                double mid = (lo + hi) / 2;
                if (Contrast(mixColor(fore, target, mid), back) >= MIN_CONTRAST) hi = mid; else lo = mid;
            }
            return mixColor(fore, target, hi);
        }

        private static void fixContrast(System.Windows.Forms.Control c)
        {
            if (_fixingContrast) return;
            System.Drawing.Color f = c.ForeColor, b = c.BackColor;
            if (f.A < 255 || b.A < 255 || Contrast(f, b) >= MIN_CONTRAST - 0.01) return;
            _fixingContrast = true;
            try
            {
                if (Luminance(f) >= Luminance(b) && Luminance(b) > 0.02) b = scaleColor(b, 0.5); // a darker surface first
                if (Contrast(f, b) < MIN_CONTRAST) f = readableFore(f, b);
                if (b != c.BackColor) c.BackColor = b;
                if (f != c.ForeColor) c.ForeColor = f;
            }
            catch (Exception ex) { Log("fixContrast " + c.Name + ": " + ex.Message); }
            finally { _fixingContrast = false; }
        }

        // Jull1ano fork: sharp skin arrows. A skin's small arrow buttons (QuickRecall < V >: 20 x 20 px pictures)
        // stretched to their 200 % buttons came out blurry. The picture is split into its button face and its light
        // triangle: the triangle's pixels (those near the lightest grey inside the face, plus 2 px around) are filled
        // in from the face on either side, along the face's gradient; the face is then drawn smoothly at the button's
        // size and the triangle on top as a sharp anti-aliased shape in its own colour. The triangle's place and size
        // come from the picture's coverage moments (area kept), so another skin's arrows keep their own look. A
        // picture without a light glyph is left as it was (null).
        private sealed class ArrowScan
        {
            public int W, H;
            public System.Drawing.Color Glyph;
            public System.Drawing.Color[,] Face;
            public float[,] Cover;
        }

        private static double colorDist(System.Drawing.Color a, System.Drawing.Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static ArrowScan scanArrow(System.Drawing.Image src)
        {
            if (src == null) return null;
            int sw = src.Width, sh = src.Height;
            if (sw < 8 || sh < 8 || sw > 128 || sh > 128) return null;
            System.Drawing.Color[,] p = new System.Drawing.Color[sw, sh];
            using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(src))
                for (int y = 0; y < sh; y++) for (int x = 0; x < sw; x++) p[x, y] = b.GetPixel(x, y);

            // the glyph's colour: the lightest, most grey pixel inside the face
            int ring = Math.Max(2, Math.Min(sw, sh) / 7);
            double best = -1;
            System.Drawing.Color g = System.Drawing.Color.Empty;
            for (int y = ring; y < sh - ring; y++)
                for (int x = ring; x < sw - ring; x++)
                {
                    System.Drawing.Color c = p[x, y];
                    if (c.A < 200) continue;
                    int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
                    double sc = (c.R + c.G + c.B) / 3.0 - 1.5 * (mx - mn);
                    if (sc > best) { best = sc; g = c; }
                }
            if (best < 120) return null;

            // the glyph's pixels and 3 px around them (its faint anti-aliased tips)
            bool[,] r = new bool[sw, sh];
            int cores = 0;
            for (int y = ring; y < sh - ring; y++)
                for (int x = ring; x < sw - ring; x++)
                {
                    if (p[x, y].A < 200 || colorDist(p[x, y], g) >= 24) continue;
                    cores++;
                    for (int yy = Math.Max(ring, y - 3); yy < Math.Min(sh - ring, y + 4); yy++)
                        for (int xx = Math.Max(ring, x - 3); xx < Math.Min(sw - ring, x + 4); xx++) r[xx, yy] = true;
                }
            if (cores < 4) return null;

            // the face under the glyph: filled in from the nearest face pixels across or down, whichever pair is
            // closer in colour (that is the direction along the face's gradient)
            ArrowScan a = new ArrowScan { W = sw, H = sh, Glyph = g, Face = new System.Drawing.Color[sw, sh], Cover = new float[sw, sh] };
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    a.Face[x, y] = p[x, y];
                    if (!r[x, y]) continue;
                    int xl = x, xr = x, ya = y, yb = y;
                    while (xl >= 0 && r[xl, y]) xl--;
                    while (xr < sw && r[xr, y]) xr++;
                    while (ya >= 0 && r[x, ya]) ya--;
                    while (yb < sh && r[x, yb]) yb++;
                    System.Drawing.Color c0, c1;
                    float t;
                    bool hOk = xl >= 0 && xr < sw, vOk = ya >= 0 && yb < sh;
                    if (hOk && (!vOk || colorDist(p[xl, y], p[xr, y]) <= colorDist(p[x, ya], p[x, yb])))
                    { c0 = p[xl, y]; c1 = p[xr, y]; t = (x - xl) / (float)(xr - xl); }
                    else if (vOk)
                    { c0 = p[x, ya]; c1 = p[x, yb]; t = (y - ya) / (float)(yb - ya); }
                    else continue;
                    a.Face[x, y] = System.Drawing.Color.FromArgb(255,
                        (int)Math.Round(c0.R + (c1.R - c0.R) * t), (int)Math.Round(c0.G + (c1.G - c0.G) * t), (int)Math.Round(c0.B + (c1.B - c0.B) * t));

                    // how much of this pixel the glyph covers
                    System.Drawing.Color f = a.Face[x, y], q = p[x, y];
                    double vr = g.R - f.R, vg = g.G - f.G, vb = g.B - f.B, n2 = vr * vr + vg * vg + vb * vb;
                    if (n2 < 1) continue;
                    double cov = ((q.R - f.R) * vr + (q.G - f.G) * vg + (q.B - f.B) * vb) / n2;
                    a.Cover[x, y] = (float)Math.Max(0, Math.Min(1, cov));
                }
            return a;
        }

        // A skin arrow button's shape, measured once from its first picture and used for all its pictures, so
        // nothing moves when the button changes state: the triangle (x0 y0 x1 y1 x2 y2, in the picture's pixels) and
        // how much of each picture pixel is button face (0 = the picture's outside colour .. 1 = face).
        public sealed class ArrowGeom
        {
            public float[] Tri;
            public int W, H;
            public bool HasFace;             // the face's rounded rectangle, in the picture's pixels
            public float L, T, R, B, Radius;
        }

        private static float arrowMedian(System.Collections.Generic.List<float> v)
        {
            v.Sort();
            return v.Count == 0 ? float.NaN : (v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2);
        }

        // where the 0.5 line falls between two neighbouring pixel centres (0 = at the first, 1 = at the second)
        private static float arrowCross(float a, float b)
        {
            return Math.Abs(b - a) < 1e-6f ? 0.5f : Math.Max(0f, Math.Min(1f, (0.5f - a) / (b - a)));
        }

        // signed distance from the face's rounded rectangle (negative inside), in the picture's pixels
        private static float arrowRectDist(ArrowGeom g, float x, float y)
        {
            float hx = (g.R - g.L) / 2, hy = (g.B - g.T) / 2, rr = g.Radius;
            float qx = Math.Abs(x - (g.L + g.R) / 2) - (hx - rr), qy = Math.Abs(y - (g.T + g.B) / 2) - (hy - rr);
            float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
            return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - rr;
        }

        private static float arrowSample(float[,] m, int w, int h, float x, float y)
        {
            float u = Math.Max(0, Math.Min(w - 1, x - 0.5f)), v = Math.Max(0, Math.Min(h - 1, y - 0.5f));
            int x0 = (int)u, y0 = (int)v, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
            float tx = u - x0, ty = v - y0;
            return (m[x0, y0] * (1 - tx) + m[x1, y0] * tx) * (1 - ty) + (m[x0, y1] * (1 - tx) + m[x1, y1] * tx) * ty;
        }

        // The face as a rounded rectangle: each side where the face mask crosses 0.5 (median over the side's middle
        // half), the corner radius from where the face begins along each corner's diagonal (a radius r starts
        // r * (1 - 1/sqrt 2) in from the corner; median of the four). Fitting one clean shape keeps the outline
        // smooth where the skin's soft edge is uneven.
        private static bool arrowFaceRect(float[,] m, int W, int H, ArrowGeom g)
        {
            System.Collections.Generic.List<float> ls = new System.Collections.Generic.List<float>(), rs = new System.Collections.Generic.List<float>(),
                ts = new System.Collections.Generic.List<float>(), bs = new System.Collections.Generic.List<float>(), rads = new System.Collections.Generic.List<float>();
            for (int y = H / 4; y < H - H / 4; y++)
            {
                if (m[0, y] >= 0.5f) ls.Add(0);
                else for (int x = 0; x + 1 < W; x++) if (m[x + 1, y] >= 0.5f) { ls.Add(x + 0.5f + arrowCross(m[x, y], m[x + 1, y])); break; }
                if (m[W - 1, y] >= 0.5f) rs.Add(W);
                else for (int x = W - 1; x - 1 >= 0; x--) if (m[x - 1, y] >= 0.5f) { rs.Add(x + 0.5f - arrowCross(m[x, y], m[x - 1, y])); break; }
            }
            for (int x = W / 4; x < W - W / 4; x++)
            {
                if (m[x, 0] >= 0.5f) ts.Add(0);
                else for (int y = 0; y + 1 < H; y++) if (m[x, y + 1] >= 0.5f) { ts.Add(y + 0.5f + arrowCross(m[x, y], m[x, y + 1])); break; }
                if (m[x, H - 1] >= 0.5f) bs.Add(H);
                else for (int y = H - 1; y - 1 >= 0; y--) if (m[x, y - 1] >= 0.5f) { bs.Add(y + 0.5f - arrowCross(m[x, y], m[x, y - 1])); break; }
            }
            if (ls.Count == 0 || rs.Count == 0 || ts.Count == 0 || bs.Count == 0) return false;
            g.L = arrowMedian(ls); g.R = arrowMedian(rs); g.T = arrowMedian(ts); g.B = arrowMedian(bs);
            if (g.R - g.L < 4 || g.B - g.T < 4) return false;
            float half = Math.Min(g.R - g.L, g.B - g.T) / 2;
            float[,] corners = { { g.L, g.T, 1, 1 }, { g.R, g.T, -1, 1 }, { g.L, g.B, 1, -1 }, { g.R, g.B, -1, -1 } };
            for (int c = 0; c < 4; c++)
                for (float d = 0; d < half; d += 0.05f)
                    if (arrowSample(m, W, H, corners[c, 0] + corners[c, 2] * d, corners[c, 1] + corners[c, 3] * d) >= 0.5f)
                    { rads.Add(d / 0.29289f); break; }
            g.Radius = rads.Count > 0 ? Math.Max(0, Math.Min(half, arrowMedian(rads))) : 0;
            g.HasFace = true;
            return true;
        }

        // the picture's outside colour: its four corners
        private static System.Drawing.Color arrowOutside(ArrowScan a)
        {
            System.Drawing.Color[] c = { a.Face[0, 0], a.Face[a.W - 1, 0], a.Face[0, a.H - 1], a.Face[a.W - 1, a.H - 1] };
            int r = 0, g = 0, b = 0;
            foreach (System.Drawing.Color k in c) { r += k.R; g += k.G; b += k.B; }
            return System.Drawing.Color.FromArgb(255, r / 4, g / 4, b / 4);
        }

        // face share per pixel: distance from the outside colour against the face's typical distance (median inside)
        private static float[,] arrowFaceMask(ArrowScan a)
        {
            System.Drawing.Color o = arrowOutside(a);
            int ring = Math.Max(2, Math.Min(a.W, a.H) / 7);
            System.Collections.Generic.List<double> d = new System.Collections.Generic.List<double>();
            for (int y = ring; y < a.H - ring; y++) for (int x = ring; x < a.W - ring; x++) d.Add(colorDist(a.Face[x, y], o));
            d.Sort();
            double D = Math.Max(16, d.Count > 0 ? d[d.Count / 2] : 16);
            float[,] m = new float[a.W, a.H];
            for (int y = 0; y < a.H; y++) for (int x = 0; x < a.W; x++) m[x, y] = (float)Math.Min(1, colorDist(a.Face[x, y], o) / D);
            return m;
        }

        // The triangle from the glyph's coverage moments: for a triangle with base B and height T the spread across
        // the base is B^2/24 and along it T^2/18, its centroid a third of the way from the base; then sized to the
        // glyph's area. dir: 0 left, 1 down, 2 right.
        public static ArrowGeom ArrowShape(System.Drawing.Image src, int dir)
        {
            try
            {
                ArrowScan a = scanArrow(src);
                if (a == null) return null;
                double A = 0, sx = 0, sy = 0, sxx = 0, syy = 0;
                for (int y = 0; y < a.H; y++)
                    for (int x = 0; x < a.W; x++)
                    {
                        double c = a.Cover[x, y];
                        if (c <= 0) continue;
                        double cx = x + 0.5, cy = y + 0.5;
                        A += c; sx += c * cx; sy += c * cy; sxx += c * cx * cx; syy += c * cy * cy;
                    }
                if (A < 4) return null;
                double mx = sx / A, my = sy / A;
                double vx = Math.Max(0.1, sxx / A - mx * mx + 1.0 / 12), vy = Math.Max(0.1, syy / A - my * my + 1.0 / 12);
                bool across = dir == 0 || dir == 2;              // the base is vertical
                double baseLen = Math.Sqrt(24 * (across ? vy : vx)), len = Math.Sqrt(18 * (across ? vx : vy));
                double k = Math.Sqrt(A / Math.Max(1e-6, baseLen * len / 2)); // the glyph's own area
                k = Math.Max(0.75, Math.Min(1.1, k));
                baseLen *= k; len *= k;
                double[] t;
                if (dir == 2) t = new double[] { mx - len / 3, my - baseLen / 2, mx - len / 3, my + baseLen / 2, mx + 2 * len / 3, my };
                else if (dir == 0) t = new double[] { mx + len / 3, my - baseLen / 2, mx + len / 3, my + baseLen / 2, mx - 2 * len / 3, my };
                else t = new double[] { mx - baseLen / 2, my - len / 3, mx + baseLen / 2, my - len / 3, mx, my + 2 * len / 3 };
                ArrowGeom geo = new ArrowGeom { Tri = new float[6], W = a.W, H = a.H };
                for (int i = 0; i < 6; i++) geo.Tri[i] = (float)t[i];
                arrowFaceRect(arrowFaceMask(a), a.W, a.H, geo);
                Log("arrow shape dir " + dir + " from " + a.W + "x" + a.H + ": " + string.Join(" ", Array.ConvertAll(geo.Tri, v => v.ToString("0.00"))) +
                    " area " + A.ToString("0.0") + " k " + k.ToString("0.00") + (geo.HasFace ? " face " + geo.L.ToString("0.00") + " " + geo.T.ToString("0.00") + " " +
                    geo.R.ToString("0.00") + " " + geo.B.ToString("0.00") + " r " + geo.Radius.ToString("0.00") : " no face"));
                return geo;
            }
            catch (Exception ex) { Log("ArrowShape: " + ex.Message); return null; }
        }

        // The picture redrawn at w x h, sharp: the face's rounded rectangle (ArrowGeom) with a one screen pixel
        // anti-aliased edge, inside it the skin's own face shading scaled smoothly, outside it the picture's outside
        // colour, and the triangle on top. Without a measured face the scaled face shading fills the button.
        public static System.Drawing.Bitmap CrispArrowImage(System.Drawing.Image src, int w, int h, ArrowGeom geo)
        {
            try
            {
                if (geo == null || geo.Tri == null || geo.Tri.Length != 6 || w <= 0 || h <= 0) return null;
                ArrowScan a = scanArrow(src);
                if (a == null) return null;
                float[,] m = arrowFaceMask(a);
                System.Drawing.Color o = arrowOutside(a);

                // the face's colours carried out over its edge, so no outside colour is mixed into the scaled face (a
                // pixel is face when its centre is half a pixel inside the measured outline, so a skin's rim stays)
                bool[,] inside = new bool[a.W, a.H];
                for (int y = 0; y < a.H; y++)
                    for (int x = 0; x < a.W; x++)
                        inside[x, y] = geo.HasFace ? arrowRectDist(geo, x + 0.5f, y + 0.5f) <= -0.5f : m[x, y] >= 0.85f;
                System.Drawing.Color[,] fc = new System.Drawing.Color[a.W, a.H];
                for (int y = 0; y < a.H; y++)
                    for (int x = 0; x < a.W; x++)
                    {
                        fc[x, y] = a.Face[x, y];
                        if (inside[x, y]) continue;
                        int bestD = int.MaxValue, n = 0, r = 0, g = 0, b = 0;
                        for (int yy = Math.Max(0, y - 4); yy <= Math.Min(a.H - 1, y + 4); yy++)
                            for (int xx = Math.Max(0, x - 4); xx <= Math.Min(a.W - 1, x + 4); xx++)
                            {
                                if (!inside[xx, yy]) continue;
                                int dd = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                                if (dd > bestD) continue;
                                if (dd < bestD) { bestD = dd; n = r = g = b = 0; }
                                System.Drawing.Color c = a.Face[xx, yy];
                                n++; r += c.R; g += c.G; b += c.B;
                            }
                        if (n > 0) fc[x, y] = System.Drawing.Color.FromArgb(255, r / n, g / n, b / n);
                    }

                System.Drawing.Bitmap outB = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (System.Drawing.Bitmap face = new System.Drawing.Bitmap(a.W, a.H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (System.Drawing.Bitmap faceUp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    for (int y = 0; y < a.H; y++) for (int x = 0; x < a.W; x++) face.SetPixel(x, y, fc[x, y]);
                    using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(faceUp))
                    using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY); // no see-through rim at the edges
                        gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        gr.DrawImage(face, new System.Drawing.Rectangle(0, 0, w, h), 0, 0, a.W, a.H, System.Drawing.GraphicsUnit.Pixel, ia);
                    }

                    // the face's rounded rectangle: signed distance from its outline, in screen pixels -> coverage
                    float fx = w / (float)a.W, fy = h / (float)a.H, fs = (fx + fy) / 2;
                    for (int Y = 0; Y < h; Y++)
                        for (int X = 0; X < w; X++)
                        {
                            float k = 1f;
                            if (geo.HasFace) k = Math.Max(0, Math.Min(1, 0.5f - arrowRectDist(geo, (X + 0.5f) / fx, (Y + 0.5f) / fy) * fs));
                            System.Drawing.Color f = faceUp.GetPixel(X, Y);
                            outB.SetPixel(X, Y, System.Drawing.Color.FromArgb(255,
                                (int)Math.Round(o.R + (f.R - o.R) * k), (int)Math.Round(o.G + (f.G - o.G) * k), (int)Math.Round(o.B + (f.B - o.B) * k)));
                        }
                }

                using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(outB))
                {
                    gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    float fx = w / (float)a.W, fy = h / (float)a.H;
                    float[] t = geo.Tri;
                    System.Drawing.PointF[] pts = {
                        new System.Drawing.PointF(t[0] * fx, t[1] * fy),
                        new System.Drawing.PointF(t[2] * fx, t[3] * fy),
                        new System.Drawing.PointF(t[4] * fx, t[5] * fy) };
                    using (System.Drawing.SolidBrush br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, a.Glyph.R, a.Glyph.G, a.Glyph.B)))
                        gr.FillPolygon(br, pts);
                }
                return outB;
            }
            catch (Exception ex) { Log("CrispArrowImage: " + ex.Message); return null; }
        }

        // the face's rounded rectangle at w x h (the hover's shape, ForkHover); null without a measured face
        public static System.Drawing.Drawing2D.GraphicsPath ArrowFacePath(ArrowGeom g, int w, int h)
        {
            if (g == null || !g.HasFace || g.W <= 0 || g.H <= 0) return null;
            float sx = w / (float)g.W, sy = h / (float)g.H;
            float l = g.L * sx, t = g.T * sy, r = g.R * sx, b = g.B * sy;
            float rad = Math.Min(g.Radius * (sx + sy) / 2, Math.Min(r - l, b - t) / 2);
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            if (rad < 0.5f) { p.AddRectangle(new System.Drawing.RectangleF(l, t, r - l, b - t)); return p; }
            float d = 2 * rad;
            p.AddArc(l, t, d, d, 180, 90);
            p.AddArc(r - d, t, d, d, 270, 90);
            p.AddArc(r - d, b - d, d, d, 0, 90);
            p.AddArc(l, b - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // button icons (Database Manager ...): WinForms draws a button's Image at its own pixel size, so a 96 DPI
        // icon came out half size in its 200 % button. Redrawn once at the DPI scale when it still fits the button.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Drawing.Image, object> _scaledImages =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Drawing.Image, object>();

        public static void ScaleButtonImages(System.Windows.Forms.Control root)
        {
            if (!Enabled || root == null) return;
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                System.Drawing.Image img0 = null;
                if (c is System.Windows.Forms.ButtonBase bb0) img0 = bb0.Image;
                else if (c is System.Windows.Forms.PictureBox pb0 &&
                         (pb0.SizeMode == System.Windows.Forms.PictureBoxSizeMode.Normal || pb0.SizeMode == System.Windows.Forms.PictureBoxSizeMode.CenterImage))
                    img0 = pb0.Image;
                if (img0 != null && !_scaledImages.TryGetValue(img0, out object _))
                {
                    try
                    {
                        System.Drawing.Image img = img0;
                        int w = Px(img.Width), h = Px(img.Height);
                        if (w <= c.Width && h <= c.Height)
                        {
                            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h);
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                g.DrawImage(img, new System.Drawing.Rectangle(0, 0, w, h));
                            }
                            _scaledImages.Add(bmp, null);
                            if (c is System.Windows.Forms.ButtonBase bb1) bb1.Image = bmp;
                            else if (c is System.Windows.Forms.PictureBox pb1) pb1.Image = bmp;
                        }
                    }
                    catch (Exception ex) { Log("ScaleButtonImages " + c.Name + ": " + ex.Message); }
                }
                if (c.HasChildren) ScaleButtonImages(c);
            }
        }

        // A grid whose form sets it up before binding its rows (Memory): the row height goes on the row template, so
        // the rows come in at it. Setting it row by row afterwards re-lays the whole grid out for every row (about
        // 40 ms each: 3 s for the 76 memories); this grid is left alone when its form opens.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.DataGridView, object> _gridsDone =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.DataGridView, object>();

        public static void PrepareGridEarly(System.Windows.Forms.DataGridView g)
        {
            if (!Enabled || g == null || _gridsDone.TryGetValue(g, out object _)) return;
            _gridsDone.Add(g, null);
            float pt = g.Font.SizeInPoints;
            int fh = (int)Math.Ceiling(g.Font.Height * ScaledFontSize(pt) / pt);
            int before = g.RowTemplate.Height;
            // the template already follows the DPI here (about font height + 8); the original shows 22 units
            g.RowTemplate.Height = Math.Max(Px(22), fh + Px(6));
            Log("PrepareGridEarly " + g.Name + ": template " + before + " -> " + g.RowTemplate.Height + ", font height " + g.Font.Height + " -> " + fh);
        }

        // grids: WinForms scales neither their column widths nor their row heights here (System DPI aware), so
        // the text is 2x in 1x cells. Fixed-size columns and rows get the form's scale, once.
        public static void FixGrids(System.Windows.Forms.Form form, System.Windows.Forms.Control root)
        {
            if (!Enabled || root == null) return;
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                if (c is System.Windows.Forms.DataGridView g0 && _gridsDone.TryGetValue(g0, out object _)) { }
                else if (c is System.Windows.Forms.DataGridView g)
                {
                    try
                    {
                        float sx = FormScale(form), sy = FormScale(form, true);
                        if (g.AutoSizeColumnsMode == System.Windows.Forms.DataGridViewAutoSizeColumnsMode.None)
                            foreach (System.Windows.Forms.DataGridViewColumn col in g.Columns)
                                if (col.AutoSizeMode == System.Windows.Forms.DataGridViewAutoSizeColumnMode.NotSet ||
                                    col.AutoSizeMode == System.Windows.Forms.DataGridViewAutoSizeColumnMode.None)
                                    col.Width = (int)Math.Round(col.Width * sx);
                        if (g.RowHeadersWidthSizeMode == System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.EnableResizing ||
                            g.RowHeadersWidthSizeMode == System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing)
                            g.RowHeadersWidth = (int)Math.Round(g.RowHeadersWidth * sx);
                        if (g.AutoSizeRowsMode == System.Windows.Forms.DataGridViewAutoSizeRowsMode.None)
                        {
                            int h = Math.Max((int)Math.Round(g.RowTemplate.Height * sy), g.Font.Height + Px(6));
                            g.RowTemplate.Height = h;
                            foreach (System.Windows.Forms.DataGridViewRow r in g.Rows) if (r.Height < h) r.Height = h; // each change re-lays the whole grid out
                        }
                        if (g.ColumnHeadersHeightSizeMode != System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize)
                            g.ColumnHeadersHeight = Math.Max(g.ColumnHeadersHeight, g.ColumnHeadersDefaultCellStyle.Font != null
                                ? g.ColumnHeadersDefaultCellStyle.Font.Height + Px(8) : g.Font.Height + Px(8));
                    }
                    catch (Exception ex) { Log("FixGrids " + g.Name + ": " + ex.Message); }
                }
                if (c is System.Windows.Forms.ListView lv && lv.View == System.Windows.Forms.View.Details)
                {
                    try
                    {
                        float sx = FormScale(form);
                        foreach (System.Windows.Forms.ColumnHeader ch in lv.Columns)
                            if (ch.Width > 0) ch.Width = (int)Math.Round(ch.Width * sx);
                    }
                    catch (Exception ex) { Log("FixGrids " + lv.Name + ": " + ex.Message); }
                }
                if (c.HasChildren) FixGrids(form, c);
            }
        }

        // a form that still uses the .NET 1.x AutoScaleBaseSize scaling, which does not happen here (CWX)
        public static void ScaleLegacyForm(System.Windows.Forms.Form f)
        {
            if (!Enabled || f == null) return;
#pragma warning disable 618
            f.AutoScale = false;
#pragma warning restore 618
            f.Scale(new System.Drawing.SizeF(Scale, Scale));
        }

        private static System.Drawing.Font sized(System.Drawing.Font f, float size)
        {
            return new System.Drawing.Font(f.FontFamily, size, f.Style, f.Unit, f.GdiCharSet, f.GdiVerticalFont);
        }

        private static void collectOwnFonts(System.Windows.Forms.Control c,
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<System.Windows.Forms.Control, System.Drawing.Font>> list)
        {
            foreach (System.Windows.Forms.Control ch in c.Controls)
            {
                // a control without its own font returns its parent's Font object
                if (!ReferenceEquals(ch.Font, c.Font))
                    list.Add(new System.Collections.Generic.KeyValuePair<System.Windows.Forms.Control, System.Drawing.Font>(ch, ch.Font));
                if (ch.HasChildren) collectOwnFonts(ch, list);
            }
        }

        private static void collectContainers(System.Windows.Forms.Control c, System.Collections.Generic.List<System.Windows.Forms.Control> list)
        {
            list.Add(c);
            foreach (System.Windows.Forms.Control ch in c.Controls)
                if (ch.HasChildren) collectContainers(ch, list);
        }

        private static void applyTextScale(System.Windows.Forms.Control root)
        {
            if (TextScale <= 1.001f) return;
            var own = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<System.Windows.Forms.Control, System.Drawing.Font>>();
            collectOwnFonts(root, own);
            // no layout pass while the fonts change: some Layout handlers need a later start-up stage
            // (the console's panelButtonBar reads the Andromeda tables before they are loaded)
            var containers = new System.Collections.Generic.List<System.Windows.Forms.Control>();
            collectContainers(root, containers);
            foreach (var c in containers) c.SuspendLayout();
            // A container that scales by font (AutoScaleMode.Font: the info bar and the other uc* controls)
            // would grow with its larger text and no longer fit where the console or Setup places it. Those
            // keep their size; their text is fitted like the rest. Only the pending DPI scale is carried over.
            var frozen = new System.Collections.Generic.List<System.Windows.Forms.ContainerControl>();
            var savedDims = new System.Collections.Generic.List<System.Drawing.SizeF>();
            var fontDims = new System.Collections.Generic.List<System.Drawing.SizeF>();
            // (the root too: a dialog that grew with its text kept the text relatively small in it)
            foreach (var c in containers)
                if (c is System.Windows.Forms.ContainerControl cc && cc.AutoScaleMode == System.Windows.Forms.AutoScaleMode.Font)
                {
                    frozen.Add(cc);
                    savedDims.Add(cc.AutoScaleDimensions);
                    fontDims.Add(cc.CurrentAutoScaleDimensions);
                    cc.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
                }
            _origBounds = new System.Collections.Generic.Dictionary<System.Windows.Forms.Control, System.Drawing.Rectangle>();
            _origSize = new System.Collections.Generic.Dictionary<System.Windows.Forms.Control, float>();
            recordBounds(root);
            try
            {
                setFont(root, sized(root.Font, ScaledFontSize(root.Font.Size)));
                foreach (var kv in own)
                {
                    if (_keepFont.Contains(kv.Key.Name)) continue;
                    setFont(kv.Key, sized(kv.Value, ScaledFontSize(kv.Value.Size)));
                }
                fitText(root);
            }
            finally
            {
                for (int k = 0; k < frozen.Count; k++)
                {
                    try
                    {
                        frozen[k].AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
                        System.Drawing.SizeF now = frozen[k].CurrentAutoScaleDimensions;
                        System.Drawing.SizeF was = fontDims[k], saved = savedDims[k];
                        if (!saved.IsEmpty && was.Width > 0 && was.Height > 0)
                            frozen[k].AutoScaleDimensions = new System.Drawing.SizeF(saved.Width * now.Width / was.Width, saved.Height * now.Height / was.Height);
                    }
                    catch (Exception ex) { Log("AutoScale " + frozen[k].Name + ": " + ex.Message); }
                }
                for (int k = containers.Count - 1; k >= 0; k--) containers[k].ResumeLayout(false);
                _origBounds = null;
                _origSize = null;
            }
            // a form that is already on screen (the watcher's case) lays itself out again now
            if (root.Visible)
                foreach (var c in containers)
                {
                    try { c.PerformLayout(); }
                    catch (Exception ex) { Log("Layout " + c.Name + ": " + ex.Message); }
                }
        }

        // a font change runs the program's own handlers (FontChanged, Resize ...), some of which expect
        // a later stage of start-up: one failing control must not stop the rest
        private static void setFont(System.Windows.Forms.Control c, System.Drawing.Font f)
        {
            try { c.Font = f; }
            catch (Exception ex) { Log("Font " + c.Name + ": " + ex); }
        }

        // the larger text must still fit: buttons, check boxes and fixed-size labels take the largest size
        // that fits (never below the original); edit boxes, combos and spin boxes grow at most one step
        private static void fitText(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
            {
                if (_keepFont.Contains(c.Name)) { }
                else if (c is System.Windows.Forms.TextBoxBase || c is System.Windows.Forms.ComboBox || c is System.Windows.Forms.UpDownBase ||
                         c is System.Windows.Forms.ListBox || c is System.Windows.Forms.ListView)
                {
                    float baseSize = origSize(c);
                    float cap = baseSize * (8.25f + 1f) / 8.25f;
                    if (c.Font.Size > cap + 0.01f) setFont(c, sized(c.Font, cap));
                }
                else if ((c is System.Windows.Forms.ButtonBase || c is System.Windows.Forms.Label) && c.AutoSize)
                {
                    fitAutoSize(c);
                }
                else if (c is System.Windows.Forms.ButtonBase || c is System.Windows.Forms.Label)
                {
                    if (!string.IsNullOrEmpty(c.Text) && c.Width > 0 && c.Height > 0)
                    {
                        float size = c.Font.Size;
                        float min = Math.Min(size, origSize(c));
                        while (size - 0.25f >= min - 0.001f && !textFits(c, size, min)) size -= 0.25f;
                        // the text must not run under a neighbour that overlaps the control (a button over the
                        // end of a check box, a check box over the start of a right-aligned label)
                        if (c is System.Windows.Forms.Label || plainCheck(c))
                        {
                            long before = overlapArea(c, textRect(c, min));
                            while (size - 0.25f >= min - 0.001f && overlapArea(c, textRect(c, size)) > before) size -= 0.25f;
                        }
                        if (_logFit) logFit(c, size, min);
                        float target = c.Font.Size;
                        if (Math.Abs(size - c.Font.Size) > 0.001f) setFont(c, sized(c.Font, size));
                        registerRefit(c, min, target);
                    }
                }
                else if (c is System.Windows.Forms.GroupBox gb)
                {
                    fitGroupTitle(gb);
                }
                if (c.HasChildren) fitText(c);
            }
        }

        // bounds and font sizes before the text grew
        private static System.Collections.Generic.Dictionary<System.Windows.Forms.Control, System.Drawing.Rectangle> _origBounds;
        private static System.Collections.Generic.Dictionary<System.Windows.Forms.Control, float> _origSize;

        private static void recordBounds(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
            {
                _origBounds[c] = c.Bounds;
                _origSize[c] = c.Font.Size;
                if (c.HasChildren) recordBounds(c);
            }
        }

        private static System.Drawing.Rectangle origBounds(System.Windows.Forms.Control c)
        {
            return _origBounds != null && _origBounds.TryGetValue(c, out System.Drawing.Rectangle r) ? r : c.Bounds;
        }

        private static float origSize(System.Windows.Forms.Control c)
        {
            return _origSize != null && _origSize.TryGetValue(c, out float z) ? z : c.Font.Size / TextScale;
        }

        private static bool autoText(System.Windows.Forms.Control c)
        {
            return (c is System.Windows.Forms.ButtonBase || c is System.Windows.Forms.Label) && c.AutoSize && !string.IsNullOrEmpty(c.Text);
        }

        // The room an auto-sized label or check box takes with its text at this size, worked out from the
        // text: while the form is being built its Bounds and GetPreferredSize can be stale (a 302 px check
        // box in Setup reads 618 px). It grows to the right, or to the left when anchored to the right only.
        private static System.Drawing.Rectangle autoRect(System.Windows.Forms.Control c, float size)
        {
            System.Drawing.Rectangle o = origBounds(c);
            System.Drawing.SizeF t;
            using (System.Drawing.Font f = sized(c.Font, size)) t = measure(c, f, 0);
            int w = (plainCheck(c) ? Px(20) : 0) + (int)Math.Ceiling(t.Width) + c.Padding.Horizontal;
            int h = Math.Max((int)Math.Ceiling(t.Height), plainCheck(c) ? Px(17) : 0) + c.Padding.Vertical;
            System.Windows.Forms.AnchorStyles a = c.Anchor;
            bool growLeft = (a & System.Windows.Forms.AnchorStyles.Right) != 0 && (a & System.Windows.Forms.AnchorStyles.Left) == 0;
            return new System.Drawing.Rectangle(growLeft ? o.Right - w : o.Left, o.Top, w, h);
        }

        // what a neighbour covers: an auto-sized one by its text at its original size
        private static System.Drawing.Rectangle occupied(System.Windows.Forms.Control s)
        {
            return autoText(s) ? autoRect(s, origSize(s)) : origBounds(s);
        }

        // how much of r lies on the control's siblings or outside its parent
        private static long overlapArea(System.Windows.Forms.Control c, System.Drawing.Rectangle r)
        {
            long sum = 0;
            System.Windows.Forms.Control p = c.Parent;
            if (p == null) return 0;
            foreach (System.Windows.Forms.Control s in p.Controls)
            {
                if (s == c) continue;
                System.Drawing.Rectangle i = System.Drawing.Rectangle.Intersect(r, occupied(s));
                if (!i.IsEmpty) sum += (long)i.Width * i.Height;
            }
            System.Drawing.Rectangle inside = System.Drawing.Rectangle.Intersect(r, new System.Drawing.Rectangle(System.Drawing.Point.Empty, p.ClientSize));
            sum += (long)r.Width * r.Height - (long)inside.Width * inside.Height;
            return sum;
        }

        // an auto-sized label or check box takes the largest size that covers no more of its neighbours
        // (or of the space outside its parent) than it did at the original size
        private static void fitAutoSize(System.Windows.Forms.Control c)
        {
            if (!autoText(c) || c.Parent == null) return;
            float min = origSize(c), size = c.Font.Size;
            long before = overlapArea(c, autoRect(c, min));
            while (size - 0.25f >= min - 0.001f && overlapArea(c, autoRect(c, size)) > before) size -= 0.25f;
            if (_logFit) Log("autosize " + c.GetType().Name + " " + c.Name + " at " + autoRect(c, min) + " -> " + size + " (base " + min + ")");
            if (Math.Abs(size - c.Font.Size) > 0.001f) setFont(c, sized(c.Font, size));
        }

        private static bool plainCheck(System.Windows.Forms.Control c)
        {
            return (c is System.Windows.Forms.CheckBox cb && cb.Appearance != System.Windows.Forms.Appearance.Button) ||
                   (c is System.Windows.Forms.RadioButton rb && rb.Appearance != System.Windows.Forms.Appearance.Button);
        }

        // the part of a fixed-size label or check box its text covers at this size (in the parent's coordinates)
        private static System.Drawing.Rectangle textRect(System.Windows.Forms.Control c, float size)
        {
            System.Drawing.Rectangle orig = origBounds(c);
            float tw;
            using (System.Drawing.Font f = sized(c.Font, size)) tw = measure(c, f, 0).Width;
            int glyph = plainCheck(c) ? Px(20) : 0;
            int width = Math.Min(orig.Width, glyph + (int)Math.Ceiling(tw));
            System.Drawing.ContentAlignment ta = c is System.Windows.Forms.Label l ? l.TextAlign
                : c is System.Windows.Forms.ButtonBase b ? b.TextAlign : System.Drawing.ContentAlignment.MiddleLeft;
            const System.Drawing.ContentAlignment right = System.Drawing.ContentAlignment.TopRight | System.Drawing.ContentAlignment.MiddleRight | System.Drawing.ContentAlignment.BottomRight;
            const System.Drawing.ContentAlignment center = System.Drawing.ContentAlignment.TopCenter | System.Drawing.ContentAlignment.MiddleCenter | System.Drawing.ContentAlignment.BottomCenter;
            int x = orig.Left;
            if (!plainCheck(c) && (ta & right) != 0) x = orig.Right - width;
            else if (!plainCheck(c) && (ta & center) != 0) x = orig.Left + (orig.Width - width) / 2;
            return new System.Drawing.Rectangle(x, orig.Top, width, orig.Height);
        }

        // a group box title stays on one line: GDI+ wraps it inside the frame ("Cursor/Peak" / "Readout")
        private static void fitGroupTitle(System.Windows.Forms.GroupBox gb)
        {
            if (string.IsNullOrEmpty(gb.Text)) return;
            int w = gb.Width - Px(8) - Px(4);
            float size = gb.Font.Size, min = Math.Min(size, origSize(gb));
            float fit = size;
            while (fit - 0.25f >= min - 0.001f)
            {
                using (System.Drawing.Font f = sized(gb.Font, fit))
                    if (measure(gb, f, 0).Width <= w) break;
                fit -= 0.25f;
            }
            if (Math.Abs(fit - size) < 0.001f) return;
            // the children keep the size they have now; only the title gets smaller
            foreach (System.Windows.Forms.Control ch in gb.Controls)
                if (ReferenceEquals(ch.Font, gb.Font)) setFont(ch, sized(gb.Font, gb.Font.Size));
            setFont(gb, sized(gb.Font, fit));
        }

        // A fixed-size label whose text the program changes later ("AGC Gain: 12" -> "AGC Gain: 120") is fitted
        // again on every change: the largest size up to the scaled one that fits, never below the original.
        private class FitInfo { public float Base; public float Target; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, FitInfo> _fit =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, FitInfo>();

        private static void registerRefit(System.Windows.Forms.Control c, float baseSize, float target)
        {
            if (target <= baseSize + 0.001f || _fit.TryGetValue(c, out FitInfo _)) return;
            _fit.Add(c, new FitInfo { Base = baseSize, Target = target });
            c.TextChanged += refitOnText;
        }

        private static void refitOnText(object sender, EventArgs e)
        {
            System.Windows.Forms.Control c = sender as System.Windows.Forms.Control;
            if (c == null || !_fit.TryGetValue(c, out FitInfo fi)) return;
            if (string.IsNullOrEmpty(c.Text) || c.Width <= 0 || c.Height <= 0) return;
            try
            {
                float cur = c.Font.Size;
                if (Math.Abs(cur - fi.Target) < 0.001f && textFits(c, cur, fi.Base)) return; // the usual case: one measure
                float size = fi.Target;
                while (size - 0.25f >= fi.Base - 0.001f && !textFits(c, size, fi.Base)) size -= 0.25f;
                if (Math.Abs(size - cur) > 0.001f) setFont(c, sized(c.Font, size));
            }
            catch (Exception ex) { Log("refit " + c.Name + ": " + ex.Message); }
        }

        private static readonly bool _logFit = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("THETIS_HIDPI_LOGFIT"));
        private static void logFit(System.Windows.Forms.Control c, float size, float baseSize)
        {
            using (System.Drawing.Font f0 = sized(c.Font, baseSize))
            using (System.Drawing.Font f1 = sized(c.Font, size))
            {
                System.Drawing.SizeF one0 = measure(c, f0, 0), one1 = measure(c, f1, 0);
                string extra = "";
                if (c is System.Windows.Forms.ButtonBase bb) extra = " flat=" + bb.FlatStyle + " pad=" + bb.Padding.All + " ta=" + bb.TextAlign;
                Log("fit " + c.GetType().Name + " " + c.Name + " '" + c.Text.Replace("\r", "").Replace("\n", "|") + "' client=" + c.ClientSize.Width + "x" + c.ClientSize.Height +
                    " gdip=" + gdiPlusText(c) + " base=" + baseSize + " -> " + size + " one0=" + one0.Width.ToString("0.0") + "x" + one0.Height.ToString("0.0") +
                    " one1=" + one1.Width.ToString("0.0") + "x" + one1.Height.ToString("0.0") + extra);
            }
        }

        // Thetis does not call SetCompatibleTextRenderingDefault(false): labels and buttons draw their text
        // with GDI+, which wraps inside a word when the word does not fit ("Zoom:" -> "Zoom" / ":")
        private static bool gdiPlusText(System.Windows.Forms.Control c)
        {
            if (c is System.Windows.Forms.ButtonBase b) return b.FlatStyle != System.Windows.Forms.FlatStyle.System && b.UseCompatibleTextRendering;
            if (c is System.Windows.Forms.Label l) return l.FlatStyle != System.Windows.Forms.FlatStyle.System && l.UseCompatibleTextRendering;
            if (c is System.Windows.Forms.GroupBox g) return g.FlatStyle != System.Windows.Forms.FlatStyle.System && g.UseCompatibleTextRendering;
            return false;
        }

        private static System.Drawing.Graphics _measureG;

        // the size the control's text takes in font f: on one line (w <= 0) or wrapped at width w
        private static System.Drawing.SizeF measure(System.Windows.Forms.Control c, System.Drawing.Font f, int w)
        {
            if (gdiPlusText(c))
            {
                if (_measureG == null) _measureG = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                using (System.Drawing.StringFormat sf = new System.Drawing.StringFormat())
                {
                    if (w <= 0) sf.FormatFlags |= System.Drawing.StringFormatFlags.NoWrap;
                    return _measureG.MeasureString(c.Text, f, w > 0 ? w : 100000, sf);
                }
            }
            System.Windows.Forms.TextFormatFlags flags = System.Windows.Forms.TextFormatFlags.NoPadding |
                (w > 0 ? System.Windows.Forms.TextFormatFlags.WordBreak : System.Windows.Forms.TextFormatFlags.SingleLine);
            return System.Windows.Forms.TextRenderer.MeasureText(c.Text, f, new System.Drawing.Size(w > 0 ? w : int.MaxValue, int.MaxValue), flags);
        }

        // the text at this size must not need more lines than at the original size, nor more room than the
        // control has (or than the original text already took)
        private static bool textFits(System.Windows.Forms.Control c, float size, float baseSize)
        {
            // the room the text gets, measured on the captures at 200 %: a label wraps or clips once the GDI+
            // width passes its client width ("Low" 56.3 px in 56 px -> "Lo"), a flat button about 9 px earlier
            // ("A <> B" 92.0 px in 100 px wraps, "0 Beat" 90.7 px does not); a margin of one more pixel on top
            int w = c.ClientSize.Width, h = c.ClientSize.Height;
            if (c is System.Windows.Forms.CheckBox cb && cb.Appearance != System.Windows.Forms.Appearance.Button) w -= Px(20);
            else if (c is System.Windows.Forms.RadioButton rb && rb.Appearance != System.Windows.Forms.Appearance.Button) w -= Px(20);
            else if (c is System.Windows.Forms.ButtonBase) { w -= Px(5); h -= Px(2); }
            else w -= Px(1);
            if (w <= 0 || h <= 0) return true;
            using (System.Drawing.Font f0 = sized(c.Font, baseSize))
            using (System.Drawing.Font f1 = sized(c.Font, size))
            {
                System.Drawing.SizeF one0 = measure(c, f0, 0), one1 = measure(c, f1, 0);
                if (one0.Width <= w + Px(2)) // one line at the original size: it stays on one line
                    return one1.Width <= w && one1.Height <= Math.Max(h, one0.Height);
                System.Drawing.SizeF wrap0 = measure(c, f0, w), wrap1 = measure(c, f1, w);
                int lines0 = (int)Math.Round(wrap0.Height / Math.Max(1f, one0.Height));
                int lines1 = (int)Math.Round(wrap1.Height / Math.Max(1f, one1.Height));
                return lines1 <= lines0 && wrap1.Width <= w && wrap1.Height <= Math.Max(h, wrap0.Height);
            }
        }

        // ---------------------------------------------------------------- screen captures for comparisons

        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        // a picture of a window as it is drawn (PrintWindow, PW_RENDERFULLCONTENT), in this process' own scale
        public static void CaptureForm(System.Windows.Forms.Form f, string file)
        {
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height)))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(f.Handle, hdc, 2); }
                    finally { g.ReleaseHdc(hdc); }
                }
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // every leaf tab page of a form in turn (the pages that hold no further tabs), one picture each:
        // <dir>\<n|o>_<page>-<page>.png (n = this high-DPI mode, o = the original look)
        public static void SweepTabs(System.Windows.Forms.Form f, string dir, string[] skip)
        {
            if (f == null || string.IsNullOrEmpty(dir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                var leaves = new System.Collections.Generic.List<System.Windows.Forms.TabPage[]>();
                collectLeafPages(f, new System.Collections.Generic.List<System.Windows.Forms.TabPage>(), leaves, skip);
                string mode = Enabled ? "n" : "o";
                var report = new System.Text.StringBuilder();
                var overlaps = new System.Collections.Generic.List<string>();
                foreach (var chain in leaves)
                {
                    foreach (var tp in chain)
                        if (tp.Parent is System.Windows.Forms.TabControl tc) tc.SelectedTab = tp;
                    for (int k = 0; k < 4; k++) System.Windows.Forms.Application.DoEvents();
                    f.Refresh();
                    string name = string.Join("-", Array.ConvertAll(chain, t => t.Name));
                    CaptureForm(f, System.IO.Path.Combine(dir, mode + "_" + name + ".png"));
                    overlaps.Clear();
                    collectOverlaps(chain[chain.Length - 1], overlaps);
                    foreach (string o in overlaps) report.AppendLine(name + "\t" + o);
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "overlaps_" + mode + ".txt"), report.ToString());
                Log("SweepTabs: " + leaves.Count + " pages");
                // THETIS_HIDPI_DUMP=<name>: bounds of that control and its children, after the sweep
                string dump = Environment.GetEnvironmentVariable("THETIS_HIDPI_DUMP");
                if (!string.IsNullOrEmpty(dump))
                    foreach (System.Windows.Forms.Control c in f.Controls.Find(dump, true)) dumpTree(c, "");
            }
            catch (Exception ex) { Log("SweepTabs: " + ex); }
        }

        private static void collectLeafPages(System.Windows.Forms.Control c, System.Collections.Generic.List<System.Windows.Forms.TabPage> chain,
            System.Collections.Generic.List<System.Windows.Forms.TabPage[]> leaves, string[] skip)
        {
            foreach (System.Windows.Forms.Control ch in c.Controls)
            {
                if (ch is System.Windows.Forms.TabControl tc)
                {
                    foreach (System.Windows.Forms.TabPage tp in tc.TabPages)
                    {
                        if (Array.IndexOf(skip, tp.Name) >= 0) continue;
                        chain.Add(tp);
                        int before = leaves.Count;
                        collectLeafPages(tp, chain, leaves, skip);
                        if (leaves.Count == before && !hasTabControl(tp)) leaves.Add(chain.ToArray());
                        chain.RemoveAt(chain.Count - 1);
                    }
                }
                else if (ch.HasChildren) collectLeafPages(ch, chain, leaves, skip);
            }
        }

        // pairs of visible sibling controls whose bounds overlap, in 96 DPI units so both modes compare:
        // "<parent>\t<a>\t<b>\t<overlap width>x<height>"
        private static void collectOverlaps(System.Windows.Forms.Control parent, System.Collections.Generic.List<string> outList)
        {
            float u = Enabled ? Scale : 1f;
            var kids = new System.Collections.Generic.List<System.Windows.Forms.Control>();
            foreach (System.Windows.Forms.Control c in parent.Controls) if (c.Visible && c.Width > 0 && c.Height > 0) kids.Add(c);
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                {
                    System.Drawing.Rectangle r = System.Drawing.Rectangle.Intersect(kids[i].Bounds, kids[j].Bounds);
                    if (r.Width > 1 && r.Height > 1)
                    {
                        // a control wholly inside a bigger one (a label on a panel ...) is a design choice, not a clash
                        System.Drawing.Rectangle a = kids[i].Bounds, b = kids[j].Bounds;
                        if (r == a || r == b) continue;
                        outList.Add(parent.Name + "\t" + kids[i].Name + "\t" + kids[j].Name + "\t" +
                            Math.Round(r.Width / u) + "x" + Math.Round(r.Height / u) + "\t" + kids[i].Bounds + " " + kids[j].Bounds +
                            " font " + kids[i].Font.SizeInPoints + " auto " + kids[i].AutoSize);
                    }
                }
            foreach (var c in kids) if (c.HasChildren && !(c is System.Windows.Forms.TabControl)) collectOverlaps(c, outList);
        }

        private static void dumpTree(System.Windows.Forms.Control c, string indent)
        {
            string extra = c is System.Windows.Forms.ContainerControl cc
                ? " mode " + cc.AutoScaleMode + " dims " + cc.AutoScaleDimensions + " cur " + cc.CurrentAutoScaleDimensions : "";
            Log("dump " + indent + c.Name + " " + c.GetType().Name + " " + c.Bounds + " client " + c.ClientSize + " anchor " + c.Anchor +
                " font " + c.Font.SizeInPoints + " visible " + c.Visible + extra);
            foreach (System.Windows.Forms.Control ch in c.Controls) dumpTree(ch, indent + "  ");
        }

        private static bool hasTabControl(System.Windows.Forms.Control c)
        {
            foreach (System.Windows.Forms.Control ch in c.Controls)
                if (ch is System.Windows.Forms.TabControl || (ch.HasChildren && hasTabControl(ch))) return true;
            return false;
        }

        // diagnostics while porting: THETIS_HIDPI_LOG=<file>
        public static void Log(string s)
        {
            try
            {
                string f = Environment.GetEnvironmentVariable("THETIS_HIDPI_LOG");
                if (string.IsNullOrEmpty(f)) return;
                System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine);
            }
            catch { }
        }
    }

    // Jull1ano fork: a hover for every button: 10 % white over a button (a Button, or a check / radio box drawn as a
    // button) while the mouse is on it and it is enabled, on top of what the skin draws (some skins give it only a
    // 1 px light edge). The button under the mouse comes from the mouse messages, so buttons made later and every
    // window get it; a button with a rounded face (QuickRecall at high DPI) gets it inside its face only.
    public static class ForkHover
    {
        public const int ALPHA = 26; // 10 % of 255 (the user's choice; 20 % was too strong)
        private static System.Windows.Forms.ButtonBase _hot;
        private static bool _installed;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object> _hooked =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, object>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, System.Drawing.Drawing2D.GraphicsPath> _shape =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, System.Drawing.Drawing2D.GraphicsPath>();

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            System.Windows.Forms.Application.AddMessageFilter(new MouseFilter());
        }

        // the hover only inside this shape (button pixels); null: the whole button
        public static void SetShape(System.Windows.Forms.Control c, System.Drawing.Drawing2D.GraphicsPath p)
        {
            if (c == null) return;
            _shape.Remove(c);
            if (p != null) _shape.Add(c, p);
            c.Invalidate();
        }

        // test hook (THETIS_HIDPI_OPEN=quickrecall:<n>:<button>): the hover shown as if the mouse were on c
        public static void TestHover(System.Windows.Forms.Control c)
        {
            hover(c);
            Log("hover test " + (c != null ? c.Name : "-") + ": " + (_hot != null ? "on" : "not a button"));
        }

        private static bool isButton(System.Windows.Forms.Control c)
        {
            if (c is System.Windows.Forms.CheckBox cb)
                return cb.Appearance == System.Windows.Forms.Appearance.Button && cb.FlatStyle != System.Windows.Forms.FlatStyle.System;
            if (c is System.Windows.Forms.RadioButton rb)
                return rb.Appearance == System.Windows.Forms.Appearance.Button && rb.FlatStyle != System.Windows.Forms.FlatStyle.System;
            return c is System.Windows.Forms.Button b && b.FlatStyle != System.Windows.Forms.FlatStyle.System;
        }

        private sealed class MouseFilter : System.Windows.Forms.IMessageFilter
        {
            public bool PreFilterMessage(ref System.Windows.Forms.Message m)
            {
                if (m.Msg == 0x0200) // WM_MOUSEMOVE
                {
                    try { hover(System.Windows.Forms.Control.FromHandle(m.HWnd)); }
                    catch { }
                }
                return false;
            }
        }

        private static void hover(System.Windows.Forms.Control c)
        {
            System.Windows.Forms.ButtonBase b = (c is System.Windows.Forms.ButtonBase bb && isButton(bb)) ? bb : null;
            if (ReferenceEquals(b, _hot)) return;
            System.Windows.Forms.ButtonBase old = _hot;
            _hot = b;
            if (old != null && !old.IsDisposed) old.Invalidate();
            if (b == null) return;
            if (!_hooked.TryGetValue(b, out object _))
            {
                _hooked.Add(b, null);
                b.Paint += onPaint;
                b.MouseLeave += onLeave;
            }
            b.Invalidate();
        }

        private static void onLeave(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, _hot)) return;
            _hot = null;
            ((System.Windows.Forms.Control)sender).Invalidate();
        }

        private static void onPaint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            System.Windows.Forms.Control c = sender as System.Windows.Forms.Control;
            if (c == null || !ReferenceEquals(c, _hot) || !c.Enabled) return;
            using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(ALPHA, 255, 255, 255)))
            {
                if (_shape.TryGetValue(c, out System.Drawing.Drawing2D.GraphicsPath p))
                {
                    var sm = e.Graphics.SmoothingMode;
                    var po = e.Graphics.PixelOffsetMode;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    e.Graphics.FillPath(br, p);
                    e.Graphics.SmoothingMode = sm;
                    e.Graphics.PixelOffsetMode = po;
                }
                else e.Graphics.FillRectangle(br, c.ClientRectangle);
            }
        }

        private static void Log(string s) { HighDpi.Log(s); }
    }

    // Jull1ano fork: NNR (WDSP 2.11 neural noise reduction) settings, as AetherSDR offers them (Setup > DSP > AetherRX > NNR):
    // Strength 0..100 sets the mask floor from -10 dB (0, the least) to -50 dB (100, the most); WDSP's own -25 dB is
    // 37.5, so 38 (-25.2 dB) is the default. Model 0 = Standard, 1 = Premium. The rest are WDSP's tuning values, each
    // starting at WDSP's own. Kept in fork_nnr.txt in the data folder; the same for every receiver.
    public static class ForkNnr
    {
        public const string FILE = "fork_nnr.txt";
        public const int DefaultStrength = 38;
        public static int Strength = DefaultStrength;
        public static int Model = 0;
        public static double Alpha = 1.0, Knee = 10.0, Tau = 2.0, MaxGain = 12.0, Attack = 0.0, Release = 0.0;
        private static string _path;

        public static double MaskFloorDb
        {
            get { return -10.0 - 40.0 * Math.Max(0, Math.Min(100, Strength)) / 100.0; }
        }

        public static void Defaults()
        {
            Strength = DefaultStrength;
            Model = 0;
            Alpha = 1.0;
            Knee = 10.0;
            Tau = 2.0;
            MaxGain = 12.0;
            Attack = 0.0;
            Release = 0.0;
        }

        private static double clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public static void Load(string dataPath)
        {
            _path = System.IO.Path.Combine(dataPath ?? "", FILE);
            try
            {
                if (!System.IO.File.Exists(_path)) return;
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (string line in System.IO.File.ReadAllLines(_path))
                {
                    string[] p = line.Split('\t');
                    double v;
                    if (p.Length != 2 || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out v)) continue;
                    switch (p[0])
                    {
                        case "strength": Strength = (int)Math.Round(clamp(v, 0, 100)); break;
                        case "model": Model = v >= 1 ? 1 : 0; break;
                        case "alpha": Alpha = clamp(v, 0, 4); break;
                        case "knee": Knee = clamp(v, 0, 40); break;
                        case "tau": Tau = clamp(v, 0.05, 30); break;
                        case "max_gain": MaxGain = clamp(v, 0, 24); break;
                        case "attack": Attack = clamp(v, 0, 500); break;
                        case "release": Release = clamp(v, 0, 500); break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                string nl = "\r\n";
                System.IO.File.WriteAllText(_path,
                    "strength\t" + Strength.ToString(ci) + nl +
                    "model\t" + Model.ToString(ci) + nl +
                    "alpha\t" + Alpha.ToString("0.###", ci) + nl +
                    "knee\t" + Knee.ToString("0.###", ci) + nl +
                    "tau\t" + Tau.ToString("0.###", ci) + nl +
                    "max_gain\t" + MaxGain.ToString("0.###", ci) + nl +
                    "attack\t" + Attack.ToString("0.###", ci) + nl +
                    "release\t" + Release.ToString("0.###", ci) + nl);
            }
            catch { }
        }

        public static void ApplyTo(RadioDSPRX r)
        {
            r.RXANNRModel = Model;
            r.RXANNRMaskFloor = MaskFloorDb;
            r.RXANNRAlpha = Alpha;
            r.RXANNRKnee = Knee;
            r.RXANNRTau = Tau;
            r.RXANNRMaxGain = MaxGain;
            r.SetRXANNRSmooth(Attack, Release);
        }
    }

    // Jull1ano fork: NR4 settings as AetherSDR offers them (Setup > DSP > AetherRX > NR4): libspecbleach's adaptive
    // noise estimation (0 SPP-MMSE, 1 Brandt, 2 Martin), reduction 0..40 dB, smoothing / whitening 0..100 %,
    // masking depth and suppression 0..1; AetherSDR's defaults. Kept in fork_nr4.txt, the same for every receiver.
    public static class ForkNr4
    {
        public const string FILE = "fork_nr4.txt";
        public static double Reduction = 10.0, Smoothing = 0.0, Whitening = 0.0, Masking = 0.5, Suppression = 0.5;
        public static bool Adaptive = true;
        public static int Method = 0;
        private static string _path;

        public static void Defaults()
        {
            Reduction = 10.0; Smoothing = 0.0; Whitening = 0.0; Masking = 0.5; Suppression = 0.5; Adaptive = true; Method = 0;
        }

        private static double clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public static void Load(string dataPath)
        {
            _path = System.IO.Path.Combine(dataPath ?? "", FILE);
            try
            {
                if (!System.IO.File.Exists(_path)) return;
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (string line in System.IO.File.ReadAllLines(_path))
                {
                    string[] p = line.Split('\t');
                    double v;
                    if (p.Length != 2 || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out v)) continue;
                    switch (p[0])
                    {
                        case "reduction": Reduction = clamp(v, 0, 40); break;
                        case "smoothing": Smoothing = clamp(v, 0, 100); break;
                        case "whitening": Whitening = clamp(v, 0, 100); break;
                        case "masking": Masking = clamp(v, 0, 1); break;
                        case "suppression": Suppression = clamp(v, 0, 1); break;
                        case "adaptive": Adaptive = v != 0; break;
                        case "method": Method = (int)clamp(Math.Round(v), 0, 2); break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                string nl = "\r\n";
                System.IO.File.WriteAllText(_path,
                    "reduction\t" + Reduction.ToString("0.###", ci) + nl +
                    "smoothing\t" + Smoothing.ToString("0.###", ci) + nl +
                    "whitening\t" + Whitening.ToString("0.###", ci) + nl +
                    "masking\t" + Masking.ToString("0.###", ci) + nl +
                    "suppression\t" + Suppression.ToString("0.###", ci) + nl +
                    "adaptive\t" + (Adaptive ? "1" : "0") + nl +
                    "method\t" + Method.ToString(ci) + nl);
            }
            catch { }
        }

        public static void ApplyTo(RadioDSPRX r)
        {
            r.RXASBNRreductionAmount = (float)Reduction;
            r.RXASBNRsmoothingFactor = (float)Smoothing;
            r.RXASBNRwhiteningFactor = (float)Whitening;
            r.RXASBNRadaptiveNoise = Adaptive ? 1 : 0;
            r.RXASBNRnoiseEstimationMethod = Method;
            r.RXASBNRmaskingDepth = (float)Masking;
            r.RXASBNRsuppressionStrength = (float)Suppression;
        }
    }

    // Jull1ano fork: the Final Output trim as AetherSDR has it (Setup > DSP > AetherRX > Final Output): a gain of
    // -12..+12 dB at the end of the receive chain (WDSP's output gain, before the volume), the same for every
    // receiver. Kept in fork_output.txt.
    public static class ForkOutput
    {
        public const string FILE = "fork_output.txt";
        public static double TrimDb = 0.0;
        private static string _path;

        public static double TrimGain { get { return Math.Pow(10.0, TrimDb / 20.0); } }

        public static void Load(string dataPath)
        {
            _path = System.IO.Path.Combine(dataPath ?? "", FILE);
            try
            {
                if (!System.IO.File.Exists(_path)) return;
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (string line in System.IO.File.ReadAllLines(_path))
                {
                    string[] p = line.Split('\t');
                    double v;
                    if (p.Length == 2 && p[0] == "trim_db" && double.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out v))
                        TrimDb = Math.Max(-12.0, Math.Min(12.0, v));
                }
            }
            catch { }
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                System.IO.File.WriteAllText(_path, "trim_db\t" + TrimDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            }
            catch { }
        }
    }
}
