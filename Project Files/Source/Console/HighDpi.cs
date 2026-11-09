/*  HighDpi.cs - Jull1ano fork of Thetis

Native high-DPI rendering (for example 200 % on a 4K monitor). Upstream Thetis runs DPI-unaware, so
Windows draws it at 1/2 size and stretches the bitmap: soft text and jagged lines. With high DPI on,
Thetis is system DPI aware: WinForms scales the forms by the system DPI and the custom drawing
(Direct2D spectrum, meters, ...) uses HighDpi.Scale.

Switch: command line -hidpi, or environment variable THETIS_HIDPI=1 (0 forces it off).

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
            bool want = false;
            foreach (string a in args)
                if (string.Equals(a, "-hidpi", StringComparison.OrdinalIgnoreCase)) want = true;
            string env = Environment.GetEnvironmentVariable("THETIS_HIDPI");
            if (env == "1") want = true;
            else if (env == "0") want = false;
            if (!want) return;

            bool ok = false;
            try { ok = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_SYSTEM_AWARE); } catch { }
            if (!ok)
            {
                try { ok = SetProcessDPIAware(); } catch { }
            }
            if (!ok) return;

            uint dpi = 96;
            try { dpi = GetDpiForSystem(); } catch { }
            Enabled = true;
            Scale = Math.Max(1.0f, dpi / 96.0f);

            // WinForms builds Control.DefaultFont from the system GUI font, which a DPI-aware process gets
            // already scaled (15.75 pt at 200 %); point sizes scale again when drawn, so every control that
            // inherits it would be twice too big. Give it the 96 DPI size, as the forms were designed with.
            try
            {
                System.Drawing.Font cur = System.Windows.Forms.Control.DefaultFont;
                Log("DPI " + dpi + ", DefaultFont at start: " + cur);
                if (cur.SizeInPoints > 11f)
                {
                    var f = typeof(System.Windows.Forms.Control).GetField("defaultFont",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) f.SetValue(null, new System.Drawing.Font("Microsoft Sans Serif", 8.25f));
                }
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
