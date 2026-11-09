using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static PirateChest.App.NativeMethods;

namespace PirateChest.App;

/// <summary>
/// The app window: the local UI inside WebView2, with our own dark title bar (drawn by the page). The window has one
/// fixed size (the layout is made for it): it can be moved and minimized, not resized or maximized.
/// Closing it only closes the window; counting and the overlay keep running in the tray.
/// </summary>
public sealed class MainWindow : Form
{
    private const string BoundsKey = "window";
    private static readonly Color Back = Color.FromArgb(0x0a, 0x12, 0x26);
    /// <summary>Window size at 100% Windows scaling; scaled with the monitor's DPI.</summary>
    private static readonly Size DesignSize = new(1414, 917);
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
        FormBorderStyle = FormBorderStyle.Sizable; // keeps the Windows shadow and round corners; the size is locked below
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        LoadBounds();

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Back };
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
        FormClosing += (_, _) => SaveBounds();
    }

    /// <summary>Locks the window to <see cref="DesignSize"/> at the current DPI (smaller only if the screen is smaller).</summary>
    private void LockSize()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        var s = new Size(Math.Min(area.Width, DesignSize.Width * DeviceDpi / 96), Math.Min(area.Height, DesignSize.Height * DeviceDpi / 96));
        MaximumSize = Size.Empty;
        MinimumSize = s;
        MaximumSize = s;
        Size = s;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LockSize();
    }

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
            case "close":
                Close();
                break;
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
            m.Result = IntPtr.Zero; // the whole window is client area (the page draws its own title bar)
            return;
        }
        if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_MAXIMIZE)
        {
            return; // fixed size: no maximize (Win+Up, double click on the title bar, ...)
        }
        base.WndProc(ref m);
        if (m.Msg == WM_SIZE && (int)m.WParam == SIZE_MAXIMIZED)
        {
            // Maximized some other way (e.g. by another program): put it back. The size cannot change, but the
            // "maximized" state would pin the window to the screen corner.
            BeginInvoke(() => ShowWindow(Handle, SW_RESTORE));
        }
    }

    /// <summary>Saved position. W, H and Max are written for older versions and ignored: the size is fixed.</summary>
    private sealed record Saved(int X, int Y, int W, int H, bool Max);

    private void LoadBounds()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        Location = area.Location;
        LockSize();
        Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
        try
        {
            if (_db.Get(BoundsKey) is { } j && JsonSerializer.Deserialize<Saved>(j) is { } s
                && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(new Point(s.X, s.Y), Size))))
            {
                Location = new Point(s.X, s.Y);
                LockSize(); // the saved spot may be on a smaller screen
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
            _db.Set(BoundsKey, JsonSerializer.Serialize(new Saved(b.X, b.Y, b.Width, b.Height, false)));
        }
        catch (Exception ex)
        {
            Log.Error("save window bounds", ex);
        }
    }
}
