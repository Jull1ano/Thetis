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

        private static void setTextStep(int n)
        {
            TextStep = Math.Max(0, Math.Min(2, n));
            TextScale = (8.25f + TextStep) / 8.25f;
        }

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
                applyTextScale(form);
                FixOwnerDrawLists(form);
            }
            catch (Exception ex) { Log("PrepareForm " + form.Name + ": " + ex); }
        }

        private static bool _watching = false;
        public static void StartFormWatcher()
        {
            if (!Enabled || _watching) return;
            _watching = true;
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
            // A dialog that scales by font itself grows as a whole, which keeps its layout.
            var frozen = new System.Collections.Generic.List<System.Windows.Forms.ContainerControl>();
            var savedDims = new System.Collections.Generic.List<System.Drawing.SizeF>();
            var fontDims = new System.Collections.Generic.List<System.Drawing.SizeF>();
            bool rootByFont = root is System.Windows.Forms.ContainerControl rcc && rcc.AutoScaleMode == System.Windows.Forms.AutoScaleMode.Font;
            if (!rootByFont)
                foreach (var c in containers)
                    if (c != root && c is System.Windows.Forms.ContainerControl cc && cc.AutoScaleMode == System.Windows.Forms.AutoScaleMode.Font)
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
                setFont(root, sized(root.Font, root.Font.Size * TextScale));
                foreach (var kv in own)
                {
                    if (_keepFont.Contains(kv.Key.Name)) continue;
                    setFont(kv.Key, sized(kv.Value, kv.Value.Size * TextScale));
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
                    float baseSize = c.Font.Size / TextScale;
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
                        float min = size / TextScale;
                        while (size - 0.25f >= min - 0.001f && !textFits(c, size, min)) size -= 0.25f;
                        // the text must not run under a neighbour that overlaps the control (a button over the
                        // end of a check box, a check box over the start of a right-aligned label)
                        if (c is System.Windows.Forms.Label || plainCheck(c))
                        {
                            long before = overlapArea(c, textRect(c, min));
                            while (size - 0.25f >= min - 0.001f && overlapArea(c, textRect(c, size)) > before) size -= 0.25f;
                        }
                        if (_logFit) logFit(c, size, min);
                        if (Math.Abs(size - c.Font.Size) > 0.001f) setFont(c, sized(c.Font, size));
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
            float size = gb.Font.Size, min = size / TextScale;
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
}
