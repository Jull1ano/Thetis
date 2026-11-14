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
        // sliders: the handle drawn as a vector circle (<Slider><Knob/><KnobHover/></Slider>); Empty: the skin's picture
        public static Color SliderKnob = Color.Empty, SliderKnobHover = Color.Empty;

        // after the skin's extras are applied to a window (the console lines up its band boxes)
        public static event Action<Control> Applied;

        public static bool GroupBorderHidden { get { return !GroupBorder.IsEmpty && GroupBorder.A == 0; } }

        // the windows the skin themes (the console, Andromeda's pop-ups): Setup and the other windows keep their look
        private static readonly HashSet<Form> _themed = new HashSet<Form>();
        public static bool Themes(Control c)
        {
            Form f = c == null ? null : c.FindForm();
            return f != null && _themed.Contains(f);
        }
        public static bool HidesBorderOf(Control groupBox) { return GroupBorderHidden && Themes(groupBox); }

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
            SliderKnob = Color.Empty;
            SliderKnobHover = Color.Empty;
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
            XmlNode sl = t.SelectSingleNode("Slider");
            if (sl != null)
            {
                SliderKnob = parse(sl, "Knob", Color.FromArgb(200, 216, 232));
                SliderKnobHover = parse(sl, "KnobHover", Color.White);
            }
            HighDpi.Log("fork theme: group border " + GroupBorder + ", box border " + BoxBorder + " r " + BoxRadius + ", combos " + (ComboModern ? "modern" : "Thetis"));
        }

        // hooks the form's controls (once each; they follow the settings above when they paint) and repaints them
        public static void Apply(Control root)
        {
            if (root == null) return;
            if (root is Form f) _themed.Add(f);
            walk(root);
            root.Invalidate(true);
            foreach (Control c in allBoxes(root)) ForkBoxBorder.RedrawFrame(c);
            try { Applied?.Invoke(root); } catch (Exception ex) { HighDpi.Log("fork theme applied: " + ex.Message); }
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
        [DllImport("user32.dll", EntryPoint = "SendMessage")] internal static extern IntPtr SendMessageRect(IntPtr h, int msg, IntPtr w, ref RECT r);
        internal const int EM_SETRECT = 0x00B3;
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
            // the up / down buttons (a child control Windows draws light): drawn over as the drop-downs' arrow - the
            // box's face, two small arrows, the one under the mouse lit, a pressed one on the theme's blue
            foreach (Control b in u.Controls)
                if (!(b is TextBoxBase)) attachButtons(u, b);
        }

        private static void attachButtons(UpDownBase u, Control b)
        {
            int hover = 0, down = 0; // 1 up, 2 down
            Func<Point, int> half = p => p.Y < b.Height / 2 ? 1 : 2;
            b.MouseMove += (s, e) => { int h = half(e.Location); if (h != hover) { hover = h; b.Invalidate(); } };
            b.MouseLeave += (s, e) => { hover = 0; down = 0; b.Invalidate(); };
            b.MouseDown += (s, e) => { down = half(e.Location); b.Invalidate(); };
            b.MouseUp += (s, e) => { down = 0; b.Invalidate(); };
            b.Paint += (s, e) =>
            {
                if (ForkTheme.BoxBorder.IsEmpty || !ForkTheme.ComboModern || !ForkTheme.Themes(u)) return;
                Graphics g = e.Graphics;
                Rectangle r = b.ClientRectangle;
                g.SmoothingMode = SmoothingMode.None;
                using (SolidBrush f = new SolidBrush(u.BackColor)) g.FillRectangle(f, r);
                int hh = r.Height / 2;
                for (int k = 1; k <= 2; k++)
                {
                    Rectangle part = k == 1 ? new Rectangle(0, 0, r.Width, hh) : new Rectangle(0, hh, r.Width, r.Height - hh);
                    if (down == k)
                        using (SolidBrush f = new SolidBrush(ForkTheme.ComboListSelect)) g.FillRectangle(f, part);
                    else if (hover == k)
                        using (SolidBrush f = new SolidBrush(Color.FromArgb(40, ForkTheme.ComboBorderHover))) g.FillRectangle(f, part);
                    float cx = part.X + part.Width / 2f, cy = part.Y + part.Height / 2f, aw = HighDpi.Px(4), ah = HighDpi.Px(2) + 0.5f;
                    Color ac = !u.Enabled ? Color.FromArgb(86, 104, 122) : (hover == k || down == k) ? ForkTheme.ComboText : ForkTheme.ComboArrow;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush a = new SolidBrush(ac))
                        g.FillPolygon(a, k == 1
                            ? new[] { new PointF(cx - aw, cy + ah), new PointF(cx + aw, cy + ah), new PointF(cx, cy - ah - 1) }
                            : new[] { new PointF(cx - aw, cy - ah), new PointF(cx + aw, cy - ah), new PointF(cx, cy + ah + 1) });
                    g.SmoothingMode = SmoothingMode.None;
                }
                using (Pen p = new Pen(ForkTheme.BoxBorder, Math.Max(1, HighDpi.Px(1)))) // a thin line between the edit and the buttons
                    g.DrawLine(p, 0, 0, 0, r.Height);
            };
            b.Invalidate();
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

        public static bool IsThemed(ComboBox c) { return c != null && _attached.Contains(c); }

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

    // The pause button (P) above Power, as AetherSDR's: running - grey bars on the bar face, a subtle edge, blue under
    // the mouse; paused - an amber face and edge with a play triangle
    public class ForkPauseButton : Control
    {
        private bool _paused, _hover;

        public ForkPauseButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
        }

        public bool Paused { get { return _paused; } set { if (_paused != value) { _paused = value; Invalidate(); } } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        private static Color rgba(int r, int g, int b, int a, Color under)
        {
            float k = a / 255f;
            return Color.FromArgb((int)(r * k + under.R * (1 - k)), (int)(g * k + under.G * (1 - k)), (int)(b * k + under.B * (1 - k)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color behind = ForkTheme.BehindColor(this);
            Rectangle r = ClientRectangle;
            if (r.Width < 4 || r.Height < 4) return;
            bool lit = _hover && Enabled;
            Color face, edge, glyph;
            if (_paused)
            {
                face = Color.FromArgb(0x5a, 0x3a, 0x0a);
                edge = lit ? Color.FromArgb(0xff, 0xc0, 0x30) : Color.FromArgb(0xe0, 0xa0, 0x00);
                glyph = Color.FromArgb(0xff, 0xd0, 0x70);
            }
            else
            {
                face = lit ? rgba(0, 112, 192, 180, behind) : rgba(30, 40, 55, 220, behind);
                edge = lit ? Color.FromArgb(0x00, 0x90, 0xe0) : Color.FromArgb(0x30, 0x40, 0x50);
                glyph = !Enabled ? Color.FromArgb(0x4a, 0x5a, 0x6a) : lit ? Color.FromArgb(0xe6, 0xf0, 0xfa) : Color.FromArgb(0x8f, 0xa8, 0xc0);
            }
            ForkTheme.PaintFrame(g, r, behind, face, edge, 2);
            float cx = r.Width / 2f, cy = r.Height / 2f, s = HighDpi.Scale;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(glyph))
            {
                if (_paused) // play: resume
                    g.FillPolygon(b, new[] { new PointF(cx - 4.5f * s, cy - 6f * s), new PointF(cx - 4.5f * s, cy + 6f * s), new PointF(cx + 6f * s, cy) });
                else // pause: two bars
                {
                    g.FillRectangle(b, cx - 5.5f * s, cy - 6f * s, 4f * s, 12f * s);
                    g.FillRectangle(b, cx + 1.5f * s, cy - 6f * s, 4f * s, 12f * s);
                }
            }
        }
    }
}
