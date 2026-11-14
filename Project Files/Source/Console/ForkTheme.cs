/*  ForkTheme.cs - Jull1ano fork of Thetis

    A skin's own extras, from a <ForkTheme> element in its xml (Skins\<name>\<name>.xml). Only a skin that has the
    element changes these; with any other skin (or none) Thetis looks as it always did.

    <ForkTheme>
      <GroupBorder>00000000</GroupBorder>   the frames' border: alpha 0 = no border (the title stays), or a colour
      <BoxBorder>FF304050</BoxBorder>       text, picture and number boxes: a 1 px border in this colour
      <BoxRadius>3</BoxRadius>               their corners (96 DPI px)
      <Combo>                                every drop-down drawn flat: a rounded face, a 1 px edge, a small arrow
        <Back/> <Border/> <BorderHover/> <Text/> <Arrow/> <ListBack/> <ListSelect/> <ListSelectText/> <Radius/>
      </Combo>
    </ForkTheme>

    Colours as in the rest of the skin xml: a colour name or AARRGGBB in hex.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;

namespace Thetis
{
    public static class ForkTheme
    {
        public static Color GroupBorder = Color.Empty;   // Empty: Thetis' own frame
        public static Color BoxBorder = Color.Empty;     // Empty: the boxes' own border
        public static int BoxRadius = 0;
        public static bool ComboModern = false;
        public static Color ComboBack, ComboBorder, ComboBorderHover, ComboText, ComboArrow, ComboListBack, ComboListSelect, ComboListSelectText;
        public static int ComboRadius = 2;

        public static bool GroupBorderHidden { get { return !GroupBorder.IsEmpty && GroupBorder.A == 0; } }

        private static Color parse(XmlNode n, string name, Color fallback)
        {
            XmlNode c = n == null ? null : n.SelectSingleNode(name);
            if (c == null || string.IsNullOrWhiteSpace(c.InnerText)) return fallback;
            string s = c.InnerText.Trim();
            Color k = Color.FromName(s);
            if (k.IsKnownColor) return k;
            int v;
            return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v) ? Color.FromArgb(v) : fallback;
        }

        private static int parseInt(XmlNode n, string name, int fallback)
        {
            XmlNode c = n == null ? null : n.SelectSingleNode(name);
            int v;
            return c != null && int.TryParse(c.InnerText.Trim(), out v) ? v : fallback;
        }

        // the skin's extras, or none (doc null or no <ForkTheme>)
        public static void Load(XmlDocument doc)
        {
            GroupBorder = Color.Empty;
            BoxBorder = Color.Empty;
            BoxRadius = 0;
            ComboModern = false;
            XmlNodeList l = doc == null ? null : doc.GetElementsByTagName("ForkTheme");
            if (l == null || l.Count == 0) { HighDpi.Log("fork theme: none"); return; }
            XmlNode t = l[0];
            GroupBorder = parse(t, "GroupBorder", Color.Empty);
            BoxBorder = parse(t, "BoxBorder", Color.Empty);
            BoxRadius = parseInt(t, "BoxRadius", 0);
            XmlNode c = t.SelectSingleNode("Combo");
            if (c != null)
            {
                ComboModern = true;
                ComboBack = parse(c, "Back", Color.FromArgb(27, 36, 51));
                ComboBorder = parse(c, "Border", Color.FromArgb(48, 64, 80));
                ComboBorderHover = parse(c, "BorderHover", Color.FromArgb(0, 144, 224));
                ComboText = parse(c, "Text", Color.FromArgb(200, 216, 232));
                ComboArrow = parse(c, "Arrow", Color.FromArgb(142, 168, 192));
                ComboListBack = parse(c, "ListBack", Color.FromArgb(26, 36, 51));
                ComboListSelect = parse(c, "ListSelect", Color.FromArgb(0, 112, 192));
                ComboListSelectText = parse(c, "ListSelectText", Color.White);
                ComboRadius = parseInt(c, "Radius", 2);
            }
            HighDpi.Log("fork theme: group border " + GroupBorder + ", box border " + BoxBorder + " r " + BoxRadius + ", combos " + (ComboModern ? "modern" : "Thetis"));
        }

        // hooks the form's controls (once each; they follow the settings above when they paint) and repaints them
        public static void Apply(Control root)
        {
            if (root == null) return;
            walk(root);
            root.Invalidate(true);
            foreach (Control c in allBoxes(root)) ForkBoxBorder.RedrawFrame(c);
        }

        private static void walk(Control p)
        {
            foreach (Control c in p.Controls)
            {
                if (c is ComboBox cb) ForkComboPainter.Attach(cb);
                else if ((c is TextBoxBase tb && tb.BorderStyle != BorderStyle.None) || (c is PictureBox pb && pb.BorderStyle != BorderStyle.None))
                    ForkBoxBorder.Attach(c);
                else if (c is UpDownBase ud) ForkUpDownBorder.Attach(ud);
                if (c.HasChildren && !(c is UpDownBase) && !(c is ComboBox)) walk(c);
            }
        }

        private static IEnumerable<Control> allBoxes(Control p)
        {
            foreach (Control c in p.Controls)
            {
                if ((c is TextBoxBase tb && tb.BorderStyle != BorderStyle.None) || (c is PictureBox pb && pb.BorderStyle != BorderStyle.None)) yield return c;
                if (c.HasChildren) foreach (Control k in allBoxes(c)) yield return k;
            }
        }

        // the colour behind a control (the first parent with a solid back colour)
        public static Color BehindColor(Control c)
        {
            for (Control p = c.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return SystemColors.Control;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath g = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { g.AddRectangle(r); return g; }
            g.AddArc(r.X, r.Y, d, d, 180, 90);
            g.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            g.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            g.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            g.CloseFigure();
            return g;
        }

        // a face with a 1 px (at 100 %) edge, the corners showing what is behind
        public static void PaintFrame(Graphics g, Rectangle r, Color behind, Color face, Color edge, int radius96)
        {
            float lw = Math.Max(1, HighDpi.Px(1)), rad = HighDpi.Px(radius96);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (SolidBrush b = new SolidBrush(behind)) g.FillRectangle(b, r);
            RectangleF o = new RectangleF(r.X, r.Y, r.Width, r.Height);
            using (GraphicsPath p = Rounded(o, rad))
            using (SolidBrush b = new SolidBrush(edge)) g.FillPath(b, p);
            RectangleF i = RectangleF.Inflate(o, -lw, -lw);
            using (GraphicsPath p = Rounded(i, Math.Max(0, rad - lw)))
            using (SolidBrush b = new SolidBrush(face)) g.FillPath(b, p);
        }

        // ---------------------------------------------------------------- Win32
        internal const int WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_NCPAINT = 0x0085, WM_ENABLE = 0x000A;
        internal const int WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_PRINTCLIENT = 0x0318;

        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct PAINTSTRUCT
        {
            public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore; public int fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct COMBOBOXINFO
        {
            public int cbSize; public RECT rcItem; public RECT rcButton; public int stateButton; public IntPtr hwndCombo, hwndItem, hwndList;
        }
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDC(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] internal static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] internal static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] internal static extern bool GetComboBoxInfo(IntPtr h, ref COMBOBOXINFO i);
        [DllImport("user32.dll")] internal static extern bool RedrawWindow(IntPtr h, IntPtr r, IntPtr rgn, uint flags);
        internal const uint RDW_INVALIDATE = 0x1, RDW_FRAME = 0x400, RDW_UPDATENOW = 0x100;
    }

    // Text and picture boxes: their border (the window's frame) redrawn as a thin rounded line in the theme's colour
    internal class ForkBoxBorder : NativeWindow
    {
        private static readonly HashSet<Control> _attached = new HashSet<Control>();
        private readonly Control _c;

        private ForkBoxBorder(Control c)
        {
            _c = c;
            if (c.IsHandleCreated) AssignHandle(c.Handle);
            c.HandleCreated += (s, e) => reassign(); // NativeWindow lets go of a destroyed window by itself
        }

        private void reassign()
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
            AssignHandle(_c.Handle);
        }

        public static void Attach(Control c)
        {
            if (_attached.Contains(c)) return;
            _attached.Add(c);
            new ForkBoxBorder(c);
        }

        public static void RedrawFrame(Control c)
        {
            if (c.IsHandleCreated) ForkTheme.RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, ForkTheme.RDW_INVALIDATE | ForkTheme.RDW_FRAME);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (ForkTheme.BoxBorder.IsEmpty) return;
            if (m.Msg == ForkTheme.WM_NCPAINT || m.Msg == ForkTheme.WM_SETFOCUS || m.Msg == ForkTheme.WM_KILLFOCUS || m.Msg == ForkTheme.WM_ENABLE)
                paintFrame();
        }

        private void paintFrame()
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return;
            ForkTheme.RECT wr, cr;
            ForkTheme.GetWindowRect(h, out wr);
            ForkTheme.GetClientRect(h, out cr);
            ForkTheme.POINT o = new ForkTheme.POINT();
            ForkTheme.ClientToScreen(h, ref o);
            int w = wr.Right - wr.Left, hh = wr.Bottom - wr.Top;
            Rectangle client = new Rectangle(o.X - wr.Left, o.Y - wr.Top, cr.Right, cr.Bottom);
            if (w <= 0 || hh <= 0 || (client.X <= 0 && client.Y <= 0)) return; // no frame
            IntPtr dc = ForkTheme.GetWindowDC(h);
            if (dc == IntPtr.Zero) return;
            try
            {
                using (Graphics g = Graphics.FromHdc(dc))
                {
                    g.ExcludeClip(client);
                    ForkTheme.PaintFrame(g, new Rectangle(0, 0, w, hh), ForkTheme.BehindColor(_c), _c.BackColor, ForkTheme.BoxBorder, ForkTheme.BoxRadius);
                }
            }
            catch { }
            finally { ForkTheme.ReleaseDC(h, dc); }
        }
    }

    // Number boxes draw their border themselves, inside: drawn over after they paint
    internal static class ForkUpDownBorder
    {
        private static readonly HashSet<Control> _attached = new HashSet<Control>();

        public static void Attach(UpDownBase u)
        {
            if (_attached.Contains(u)) return;
            _attached.Add(u);
            u.Paint += (s, e) =>
            {
                if (ForkTheme.BoxBorder.IsEmpty || u.BorderStyle == BorderStyle.None) return;
                Rectangle r = u.ClientRectangle;
                int lw = Math.Max(1, HighDpi.Px(1));
                using (Region keep = new Region(r))
                {
                    keep.Exclude(Rectangle.Inflate(r, -2 * lw, -2 * lw)); // only the edge ring: the edit and buttons are children
                    Region old = e.Graphics.Clip;
                    e.Graphics.Clip = keep;
                    ForkTheme.PaintFrame(e.Graphics, r, ForkTheme.BehindColor(u), u.BackColor, ForkTheme.BoxBorder, ForkTheme.BoxRadius);
                    e.Graphics.Clip = old;
                }
            };
        }
    }

    // Drop-downs, modern: a rounded flat face, a 1 px edge (lit under the mouse, when open or focused), the text,
    // a small arrow; no separate button. Drop-down lists are drawn whole; an editable one keeps its edit box.
    internal class ForkComboPainter : NativeWindow
    {
        private static readonly HashSet<ComboBox> _attached = new HashSet<ComboBox>();
        private readonly ComboBox _c;
        private bool _hover, _open;

        private ForkComboPainter(ComboBox c)
        {
            _c = c;
            if (c.IsHandleCreated) AssignHandle(c.Handle);
            c.HandleCreated += (s, e) => // a DrawMode change makes a new handle
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                AssignHandle(_c.Handle);
            };
            c.MouseEnter += (s, e) => { _hover = true; _c.Invalidate(); };
            c.MouseLeave += (s, e) => { _hover = false; _c.Invalidate(); };
            c.DropDown += (s, e) => { _open = true; _c.Invalidate(); };
            c.DropDownClosed += (s, e) => { _open = false; _c.Invalidate(); };
            c.EnabledChanged += (s, e) => _c.Invalidate();
            c.GotFocus += (s, e) => _c.Invalidate();
            c.LostFocus += (s, e) => _c.Invalidate();
        }

        public static void Attach(ComboBox c)
        {
            if (_attached.Contains(c)) return;
            _attached.Add(c);
            new ForkComboPainter(c);
        }

        protected override void WndProc(ref Message m)
        {
            if (!ForkTheme.ComboModern || Handle == IntPtr.Zero)
            {
                base.WndProc(ref m);
                return;
            }
            bool list = _c.DropDownStyle == ComboBoxStyle.DropDownList;
            if (list && m.Msg == ForkTheme.WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
            if (list && m.Msg == ForkTheme.WM_PAINT && m.WParam == IntPtr.Zero)
            {
                ForkTheme.PAINTSTRUCT ps;
                IntPtr dc = ForkTheme.BeginPaint(Handle, out ps);
                try { paint(dc, true); }
                finally { ForkTheme.EndPaint(Handle, ref ps); }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == ForkTheme.WM_PAINT || m.Msg == ForkTheme.WM_NCPAINT || m.Msg == ForkTheme.WM_ENABLE)
            {
                IntPtr dc = ForkTheme.GetDC(Handle);
                if (dc == IntPtr.Zero) return;
                try { paint(dc, list); }
                finally { ForkTheme.ReleaseDC(Handle, dc); }
            }
        }

        private void paint(IntPtr dc, bool whole)
        {
            ForkTheme.RECT cr;
            ForkTheme.GetClientRect(Handle, out cr);
            Rectangle r = new Rectangle(0, 0, cr.Right, cr.Bottom);
            if (r.Width <= 2 || r.Height <= 2) return;
            Rectangle item = Rectangle.Empty;
            if (!whole)
            {
                ForkTheme.COMBOBOXINFO ci = new ForkTheme.COMBOBOXINFO();
                ci.cbSize = Marshal.SizeOf(ci);
                if (ForkTheme.GetComboBoxInfo(Handle, ref ci))
                    item = Rectangle.FromLTRB(ci.rcItem.Left, ci.rcItem.Top, ci.rcItem.Right, ci.rcItem.Bottom);
            }
            using (Bitmap bmp = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    bool lit = _hover || _open || _c.Focused;
                    Color face = _c.Enabled ? ForkTheme.ComboBack : Color.FromArgb(ForkTheme.ComboBack.R * 3 / 4, ForkTheme.ComboBack.G * 3 / 4, ForkTheme.ComboBack.B * 3 / 4);
                    ForkTheme.PaintFrame(g, r, ForkTheme.BehindColor(_c), face, lit && _c.Enabled ? ForkTheme.ComboBorderHover : ForkTheme.ComboBorder, ForkTheme.ComboRadius);
                    int arrowW = HighDpi.Px(15); // AetherSDR: no button, a small arrow at the right
                    float cx = r.Right - HighDpi.Px(9), cy = r.Height / 2f, aw = HighDpi.Px(4), ah = HighDpi.Px(2) + 0.5f;
                    Color ac = _c.Enabled ? (lit ? ForkTheme.ComboText : ForkTheme.ComboArrow) : Color.FromArgb(86, 104, 122);
                    using (SolidBrush b = new SolidBrush(ac))
                        g.FillPolygon(b, new[] { new PointF(cx - aw, cy - ah), new PointF(cx + aw, cy - ah), new PointF(cx, cy + ah + 1) });
                    if (whole)
                    {
                        Rectangle tr = new Rectangle(HighDpi.Px(4), 0, Math.Max(1, r.Width - HighDpi.Px(4) - arrowW - HighDpi.Px(2)), r.Height);
                        Color tc = _c.Enabled ? ForkTheme.ComboText : Color.FromArgb(86, 104, 122);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        TextRenderer.DrawText(g, _c.Text, _c.Font, tr, tc, face,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine |
                            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    }
                }
                using (Graphics dg = Graphics.FromHdc(dc))
                {
                    if (!item.IsEmpty) dg.ExcludeClip(item); // an editable one: its edit box draws itself
                    dg.DrawImageUnscaled(bmp, 0, 0);
                }
            }
        }
    }
}
