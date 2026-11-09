using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static PirateChest.App.NativeMethods;

namespace PirateChest.App;

/// <summary>
/// The app window: the local UI inside WebView2, with our own dark title bar (drawn by the page). It keeps the Windows
/// behaviour of a normal window: resizing by the edges, Snap, maximize by double-clicking the title bar.
/// Closing it only closes the window; counting and the overlay keep running in the tray.
/// </summary>
public sealed class MainWindow : Form
{
    private const string BoundsKey = "window";
    private static readonly Color Back = Color.FromArgb(0x0a, 0x12, 0x26);
    private readonly WebView2 _web;
    private readonly Database _db;
    private readonly string _startUrl;
    private readonly string _origin;

    public MainWindow(Database db, string startUrl, string origin, Icon? icon)
    {
        _db = db;
        _startUrl = startUrl;
        _origin = origin.TrimEnd('/');
        Text = AppInfo.DisplayName;
        if (icon != null)
        {
            Icon = icon;
        }
        BackColor = Back;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 640);
        LoadBounds();

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Back };
        Controls.Add(_web);
        UpdatePadding();
        Load += async (_, _) => await InitAsync();
        Resize += (_, _) =>
        {
            UpdatePadding();
            PostState();
        };
        FormClosing += (_, _) => SaveBounds();
    }

    /// <summary>A thin strip around the page stays ours, so the edges can be grabbed for resizing.</summary>
    private int Edge => WindowState == FormWindowState.Maximized ? 0 : Math.Max(4, (int)Math.Round(5 * DeviceDpi / 96.0));

    private void UpdatePadding() => Padding = new Padding(Edge);

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, "webview2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            Log.Error("webview2", ex);
            MessageBox.Show("The window needs Microsoft Edge WebView2, which is part of Windows 11.\n\n" + ex.Message,
                AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        var core = _web.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.AreDevToolsEnabled = Environment.GetEnvironmentVariable("PCC_DEVTOOLS") == "1";
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        core.NewWindowRequested += (_, e) => e.Handled = true; // no pop-ups, no browser
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(_origin + "/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        };
        core.WebMessageReceived += OnMessage;
        core.NavigationCompleted += async (_, _) =>
        {
            PostState();
            // Development only: run a test script inside the window (the page cannot be reached from outside it).
            if (Environment.GetEnvironmentVariable("PCC_DEV_SCRIPT") is { } script && File.Exists(script))
            {
                var result = await core.ExecuteScriptAsync(await File.ReadAllTextAsync(script));
                Log.Info("dev script: " + result);
            }
        };
        core.Navigate(_startUrl);
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? cmd;
        try
        {
            cmd = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }
        if (cmd != null && cmd.StartsWith("dbg:", StringComparison.Ordinal) && Environment.GetEnvironmentVariable("PCC_DEV_SCRIPT") != null)
        {
            Log.Info("page: " + cmd[4..]);
            return;
        }
        switch (cmd)
        {
            case "drag":
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero); // native move: Snap, Aero shake, etc.
                break;
            case "min":
                WindowState = FormWindowState.Minimized;
                break;
            case "max":
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                break;
            case "close":
                Close();
                break;
        }
    }

    private void PostState()
    {
        try
        {
            _web.CoreWebView2?.PostWebMessageAsString(WindowState == FormWindowState.Maximized ? "state:max" : "state:normal");
        }
        catch
        {
            // not ready
        }
    }

    public void ShowAndActivate(bool restore)
    {
        if (restore && WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Show();
        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var m = new MARGINS { Top = 1 }; // keeps the Windows shadow around a window without a system frame
        DwmExtendFrameIntoClientArea(Handle, ref m);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // The whole window is client area (the page draws its own title bar). When maximized, Windows places the
            // window past the screen edges by the frame thickness; pull the client area back inside.
            if (WindowState == FormWindowState.Maximized)
            {
                var r = Marshal.PtrToStructure<RECT>(m.LParam);
                var dpi = GetDpiForWindow(Handle);
                var pad = GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
                var fx = GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + pad;
                var fy = GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + pad;
                r.Left += fx;
                r.Right -= fx;
                r.Top += fy;
                r.Bottom -= fy;
                Marshal.StructureToPtr(r, m.LParam, false);
            }
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
        {
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            var e = Edge + 2;
            var left = p.X < e;
            var right = p.X >= ClientSize.Width - e;
            var top = p.Y < e;
            var bottom = p.Y >= ClientSize.Height - e;
            m.Result = (top, bottom, left, right) switch
            {
                (true, _, true, _) => HTTOPLEFT,
                (true, _, _, true) => HTTOPRIGHT,
                (_, true, true, _) => HTBOTTOMLEFT,
                (_, true, _, true) => HTBOTTOMRIGHT,
                (true, _, _, _) => HTTOP,
                (_, true, _, _) => HTBOTTOM,
                (_, _, true, _) => HTLEFT,
                (_, _, _, true) => HTRIGHT,
                _ => HTCLIENT,
            };
            return;
        }
        base.WndProc(ref m);
    }

    private sealed record Saved(int X, int Y, int W, int H, bool Max);

    private void LoadBounds()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var w = Math.Min(1360, area.Width - 80);
        var h = Math.Min(900, area.Height - 80);
        Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        try
        {
            if (_db.Get(BoundsKey) is { } j && JsonSerializer.Deserialize<Saved>(j) is { } s)
            {
                var r = new Rectangle(s.X, s.Y, Math.Max(s.W, MinimumSize.Width), Math.Max(s.H, MinimumSize.Height));
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)))
                {
                    Bounds = r;
                }
                if (s.Max)
                {
                    WindowState = FormWindowState.Maximized;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("window bounds", ex);
        }
    }

    private void SaveBounds()
    {
        try
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _db.Set(BoundsKey, JsonSerializer.Serialize(new Saved(b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized)));
        }
        catch (Exception ex)
        {
            Log.Error("save window bounds", ex);
        }
    }
}
