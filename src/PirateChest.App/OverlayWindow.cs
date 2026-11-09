using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using static PirateChest.App.NativeMethods;

namespace PirateChest.App;

/// <summary>
/// The chest counter overlay: a small separate always-on-top window with per-pixel transparency. It never touches the
/// game (no injection, no hooks, no memory reading). Clicks pass through it, except in "move" mode where it can be
/// dragged. The number rolls like a mechanical counter when it changes.
/// </summary>
public sealed class OverlayWindow : Form
{
    private static readonly PrivateFontCollection Fonts = LoadFonts();
    private static readonly Image? Chest = LoadImage("overlay-chest.png");

    private OverlaySettings _s;
    private bool _moving;
    private bool _placed;
    private int _delta;
    private double _shown;
    private double _from;
    private double _to;
    private DateTime _animStart;
    private static readonly TimeSpan AnimLength = TimeSpan.FromMilliseconds(1400);
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _topmost = new() { Interval = 4000 };

    /// <summary>Raised after the user dragged the overlay to a new place.</summary>
    public event Action<int, int>? Moved;

    public OverlayWindow(OverlaySettings s, int value)
    {
        _s = s;
        _shown = _from = _to = value;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Pirate Chest Counter overlay";
        _anim.Tick += (_, _) => Step();
        _topmost.Tick += (_, _) => SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _topmost.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _anim.Dispose();
            _topmost.Dispose();
        }
        base.Dispose(disposing);
    }

    public void Apply(OverlaySettings s)
    {
        _s = s;
        Render();
    }

    /// <summary>Shows a new number; it rolls from the old one.</summary>
    public void SetValue(int value, bool animate)
    {
        if (Math.Abs(_to - value) < 0.5)
        {
            return;
        }
        if (!animate || !IsHandleCreated)
        {
            _anim.Stop();
            _shown = _from = _to = value;
            Render();
            return;
        }
        _from = _shown;
        _to = value;
        _animStart = DateTime.UtcNow;
        _anim.Start();
    }

    /// <summary>"(+N)" after the number: what the last match added (0 hides it).</summary>
    public void SetDelta(int delta)
    {
        if (delta == _delta)
        {
            return;
        }
        _delta = delta;
        Render();
    }

    public void SetMoving(bool on)
    {
        _moving = on;
        if (IsHandleCreated)
        {
            var ex = (long)GetWindowLongPtr(Handle, GWL_EXSTYLE);
            ex = on ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex));
        }
        Render();
    }

    private void Step()
    {
        var t = Math.Clamp((DateTime.UtcNow - _animStart) / AnimLength, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        _shown = _from + (_to - _from) * eased;
        if (t >= 1)
        {
            _shown = _to;
            _anim.Stop();
        }
        Render();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCHITTEST when _moving:
                m.Result = HTCAPTION;
                return;
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_EXITSIZEMOVE:
                base.WndProc(ref m);
                Moved?.Invoke(Left, Top);
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Default place: top-right corner of the main screen, below where most HUD elements sit.</summary>
    public static Point DefaultLocation(Size size)
    {
        var a = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        return new Point(a.Right - size.Width - 32, a.Top + 140);
    }

    private string LabelText => _s.Show switch { "session" => "SESSION", "today" => "TODAY", _ => "MY CHESTS" };

    private void Render()
    {
        if (!IsHandleCreated)
        {
            return;
        }
        var k = _s.Scale / 100.0f;
        var em = 44 * k;
        using var num = MakeFont(false, em); // Burbank Small Bold
        using var small = MakeFont(false, 13 * k);
        var fmt = StringFormat.GenericTypographic;
        using var probe = new Bitmap(1, 1);
        using var pg = Graphics.FromImage(probe);
        pg.TextRenderingHint = TextRenderingHint.AntiAlias;

        // Without a visible plate the number needs a stronger outline to read over the game.
        var bare = _s.Background == "none" || _s.Opacity < 15;
        var outlineW = (bare ? 3.2f : 2f) * k;
        var margin = outlineW + 1;

        // Fixed-width digit cells so the number does not wobble while it rolls; vertical extents from the glyph outlines.
        var digitW = Enumerable.Range(0, 10).Max(d => pg.MeasureString(d.ToString(CultureInfo.InvariantCulture), num, PointF.Empty, fmt).Width);
        var commaW = pg.MeasureString(",", num, PointF.Empty, fmt).Width;
        var digB = PathBounds("0123456789", num, em, fmt);
        var rowB = PathBounds("(0123456789+,)", num, em, fmt);
        var cellH = digB.Height + 2 * margin;
        var rowTop = Math.Min(rowB.Top - margin, digB.Top - margin);
        var rowH = Math.Max(rowB.Bottom + margin, digB.Bottom + margin) - rowTop;

        var target = (long)Math.Round(Math.Max(_to, _from));
        var digits = Math.Max(1, target.ToString(CultureInfo.InvariantCulture).Length);
        var numberW = digits * digitW + (digits - 1) / 3 * commaW;
        var deltaText = _delta > 0 ? $"(+{_delta.ToString("N0", CultureInfo.InvariantCulture)})" : null;
        var deltaGap = digitW * 0.3f;
        var deltaW = deltaText != null ? pg.MeasureString(deltaText, num, PointF.Empty, fmt).Width : 0;
        var rowW = numberW + (deltaText != null ? deltaGap + deltaW : 0);

        var label = LabelText;
        var labelSize = _s.Label ? pg.MeasureString(label, small, PointF.Empty, fmt) : SizeF.Empty;
        var pad = 12 * k;
        var gap = 10 * k;
        var iconS = _s.Icon && Chest != null ? 46 * k : 0;
        var textW = Math.Max(rowW, labelSize.Width);
        var textH = rowH + (_s.Label ? labelSize.Height + 2 * k : 0);
        var w = Math.Max(8, (int)Math.Ceiling(pad * 2 + (iconS > 0 ? iconS + gap : 0) + textW + margin));
        var h = Math.Max(8, (int)Math.Ceiling(pad * 2 + Math.Max(textH, iconS)));

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            var plate = new RectangleF(0.5f, 0.5f, w - 1, h - 1);
            using var plateP = RoundRect(plate, 10 * k);
            var a = (int)(255 * _s.Opacity / 100.0);
            if (a > 0 && _s.Background == "dark")
            {
                using var b = new SolidBrush(Color.FromArgb(a, 8, 14, 32));
                g.FillPath(b, plateP);
            }
            else if (a > 0 && _s.Background == "glass")
            {
                using var b = new LinearGradientBrush(plate, Color.FromArgb((int)(a * 0.45), 255, 255, 255), Color.FromArgb((int)(a * 0.15), 200, 220, 255), 90f);
                g.FillPath(b, plateP);
                using var pen = new Pen(Color.FromArgb((int)(a * 0.8), 255, 255, 255), Math.Max(1, k));
                g.DrawPath(pen, plateP);
            }
            if (_moving)
            {
                using var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
                g.FillPath(b, plateP);
                using var pen = new Pen(Color.FromArgb(240, 246, 210, 74), Math.Max(2, 2 * k)) { DashStyle = DashStyle.Dash };
                g.DrawPath(pen, plateP);
            }

            var x = pad;
            if (iconS > 0)
            {
                g.DrawImage(Chest!, new RectangleF(x, (h - iconS) / 2, iconS, iconS));
                x += iconS + gap;
            }
            var y = (h - textH) / 2;
            if (_s.Label)
            {
                using var lb = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                g.DrawString(label, small, lb, x, y, fmt);
                y += labelSize.Height + 2 * k;
            }

            var originY = y - rowTop; // the text origin that puts the top of the row at y
            using var outline = new Pen(Color.FromArgb(bare ? 170 : 110, 0, 0, 0), outlineW) { LineJoin = LineJoin.Round };
            using var fill = new SolidBrush(ColorTranslator.FromHtml(_s.Color));
            var clipTop = originY + digB.Top - margin;
            DrawNumber(g, num, em, fmt, fill, outline, x, originY, clipTop, cellH, digitW, commaW, digits);
            if (deltaText != null)
            {
                using var green = new SolidBrush(Color.FromArgb(0x5f, 0xe0, 0x8a));
                DrawGlyph(g, deltaText, num, em, fmt, green, outline, x + numberW + deltaGap, originY, deltaW);
            }
        }
        Push(bmp);
    }

    private static RectangleF PathBounds(string s, Font font, float em, StringFormat fmt)
    {
        using var p = new GraphicsPath();
        p.AddString(s, font.FontFamily, (int)font.Style, em, PointF.Empty, fmt);
        return p.GetBounds();
    }

    /// <summary>Draws the number as an odometer: each digit cell scrolls when the digit below it rolls over.</summary>
    private void DrawNumber(Graphics g, Font font, float em, StringFormat fmt, Brush fill, Pen outline,
        float x, float originY, float clipTop, float cellH, float digitW, float commaW, int digits)
    {
        var v = Math.Max(0, _shown);
        var cx = x;
        for (var pos = digits - 1; pos >= 0; pos--)
        {
            var p = Math.Pow(10, pos);
            var whole = Math.Floor(v / p);
            var d = (int)(whole % 10);
            var f = pos == 0 ? v - Math.Floor(v) : Math.Clamp(v - whole * p - (p - 1), 0, 1);
            var state = g.Save();
            g.SetClip(new RectangleF(cx - 2, clipTop, digitW + 4, cellH));
            var leading = pos > 0 && whole < 1;
            if (!leading)
            {
                DrawGlyph(g, d.ToString(CultureInfo.InvariantCulture), font, em, fmt, fill, outline, cx, originY - (float)f * cellH, digitW);
            }
            if (f > 0 && (!leading || d == 0))
            {
                DrawGlyph(g, ((d + 1) % 10).ToString(CultureInfo.InvariantCulture), font, em, fmt, fill, outline, cx, originY + (float)(1 - f) * cellH, digitW);
            }
            g.Restore(state);
            cx += digitW;
            if (pos > 0 && pos % 3 == 0)
            {
                if (!leading)
                {
                    DrawGlyph(g, ",", font, em, fmt, fill, outline, cx, originY, commaW);
                }
                cx += commaW;
            }
        }
    }

    private static void DrawGlyph(Graphics g, string s, Font font, float em, StringFormat fmt, Brush fill, Pen outline, float x, float y, float cellW)
    {
        var w = g.MeasureString(s, font, PointF.Empty, fmt).Width;
        using var path = new GraphicsPath();
        path.AddString(s, font.FontFamily, (int)font.Style, em, new PointF(x + (cellW - w) / 2, y), fmt);
        g.DrawPath(outline, path);
        g.FillPath(fill, path);
    }

    private void Push(Bitmap bmp)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE { Cx = bmp.Width, Cy = bmp.Height };
            Point loc;
            if (_moving && _placed)
            {
                loc = new Point(Left, Top); // the user is dragging it: stay where the system put it
            }
            else
            {
                loc = _s.X is { } sx && _s.Y is { } sy ? new Point(sx, sy) : DefaultLocation(new Size(bmp.Width, bmp.Height));
                var r = new Rectangle(loc, new Size(bmp.Width, bmp.Height));
                if (!Screen.AllScreens.Any(sc => sc.Bounds.IntersectsWith(r)))
                {
                    loc = DefaultLocation(r.Size); // the monitor it was on is gone
                }
            }
            _placed = true;
            var dst = new POINT { X = loc.X, Y = loc.Y };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Font MakeFont(bool big, float px)
    {
        var name = big ? "Burbank Big Condensed" : "Burbank Small";
        var fam = Fonts.Families.FirstOrDefault(f => f.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (fam != null)
        {
            foreach (var st in new[] { FontStyle.Regular, FontStyle.Bold })
            {
                if (fam.IsStyleAvailable(st))
                {
                    return new Font(fam, Math.Max(4, px), st, GraphicsUnit.Pixel);
                }
            }
        }
        return new Font("Arial", Math.Max(4, px), FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static PrivateFontCollection LoadFonts()
    {
        var pfc = new PrivateFontCollection();
        var asm = typeof(OverlayWindow).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var st = asm.GetManifestResourceStream(res)!;
                var bytes = new byte[st.Length];
                st.ReadExactly(bytes);
                var mem = Marshal.AllocCoTaskMem(bytes.Length); // must stay alive for the life of the process
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                pfc.AddMemoryFont(mem, bytes.Length);
            }
            catch (Exception ex)
            {
                Log.Error("overlay font " + res, ex);
            }
        }
        return pfc;
    }

    private static Image? LoadImage(string name)
    {
        using var st = typeof(OverlayWindow).Assembly.GetManifestResourceStream(name);
        return st == null ? null : new Bitmap(Image.FromStream(st));
    }
}
