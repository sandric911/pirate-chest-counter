using PirateChest.Core.Counting;

namespace PirateChest.App;

/// <summary>Everything the running app owns.</summary>
public sealed class AppServices
{
    public required Database Db { get; init; }
    public required ChestAtlas Atlas { get; init; }
    public required ReplayProcessor Processor { get; init; }
    public required FortniteMonitor Fortnite { get; init; }
    public required Updater Updater { get; init; }
    public UiBridge Ui { get; } = new();
}

/// <summary>Lets the web API reach the windows (they live on the UI thread).</summary>
public sealed class UiBridge
{
    public event Action<OverlaySettings>? OverlayChanged;
    public event Action<bool>? OverlayMoveRequested;

    public bool OverlayMoving { get; set; }

    public void ChangeOverlay(OverlaySettings s) => OverlayChanged?.Invoke(s);
    public void SetOverlayMove(bool on) => OverlayMoveRequested?.Invoke(on);
}

/// <summary>
/// Tray icon and menu (Open, Overlay, Recount everything, Start with Windows, Exit). Owns the app window, which is
/// created when opened and disposed when closed (counting and the overlay keep running), and the overlay window.
/// No balloon notifications.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly AppServices _s;
    private readonly Stats _stats;
    private readonly string _windowUrl;
    private readonly string _origin;
    private readonly ToolStripMenuItem _autostart;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private MainWindow? _window;
    private OverlayWindow? _overlay;
    private OverlaySettings _overlaySettings;

    public TrayApp(AppServices s, string windowUrl, string origin, bool openWindow, EventWaitHandle exitEvent, EventWaitHandle showEvent)
    {
        _s = s;
        _windowUrl = windowUrl;
        _origin = origin;
        _stats = new Stats(s.Db);
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _overlaySettings = OverlaySettings.Load(s.Db);

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open", null, (_, _) => ShowWindow()) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };
        _overlayItem = new ToolStripMenuItem("Overlay", null, (_, _) => ToggleOverlay());
        var recount = new ToolStripMenuItem("Recount everything", null, (_, _) => _s.Processor.RecountAll());
        _autostart = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart());
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitThread());
        menu.Items.AddRange(new ToolStripItem[] { open, _overlayItem, recount, _autostart, new ToolStripSeparator(), exit });
        menu.Opening += (_, _) =>
        {
            _autostart.Checked = Autostart.IsEnabled();
            _overlayItem.Checked = _overlaySettings.Enabled;
        };

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(SystemInformation.SmallIconSize),
            Text = AppInfo.DisplayName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowWindow();
            }
        };

        s.Processor.MatchProcessed += (_, _) => _ui.Post(_ => UpdateTooltip(), null);
        s.Processor.BatchFinished += _ => _ui.Post(_ => UpdateTooltip(), null);
        s.Updater.RequestExit = () => _ui.Post(_ => ExitThread(), null);
        s.Updater.CanApplyNow = () => !s.Processor.Busy;
        s.Fortnite.RunningChanged += _ => _ui.Post(_ => SyncOverlay(), null);
        s.Ui.OverlayChanged += o => _ui.Post(_ =>
        {
            _overlaySettings = o;
            SyncOverlay();
        }, null);
        s.Ui.OverlayMoveRequested += on => _ui.Post(_ => SetOverlayMoving(on), null);

        // Exit when another process (installer/uninstaller) asks us to; show the window when a second copy is started.
        ThreadPool.RegisterWaitForSingleObject(exitEvent, (_, _) => _ui.Post(_ => ExitThread(), null), null, Timeout.Infinite, true);
        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => _ui.Post(_ => ShowWindow(), null), null, Timeout.Infinite, false);

        _poll.Tick += (_, _) => RefreshOverlayValue();
        _poll.Start();
        UpdateTooltip();
        SyncOverlay();
        if (openWindow)
        {
            ShowWindow();
        }
    }

    public static Icon LoadIcon(Size size)
    {
        using var st = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
        return st != null ? new Icon(st, size) : SystemIcons.Application;
    }

    public void ShowWindow()
    {
        if (_window == null || _window.IsDisposed)
        {
            _window = new MainWindow(_s.Db, _windowUrl, _origin, LoadIcon(new Size(256, 256)));
            _window.FormClosed += (_, _) =>
            {
                _window?.Dispose();
                _window = null;
                GC.Collect(); // the page's memory goes back to Windows while the app sits in the tray
            };
        }
        _window.ShowAndActivate(restore: true);
    }

    private void ToggleOverlay()
    {
        _overlaySettings.Enabled = !_overlaySettings.Enabled;
        _overlaySettings.Save(_s.Db);
        SyncOverlay();
    }

    private bool OverlayVisible => _s.Ui.OverlayMoving || (_overlaySettings.Enabled && (_overlaySettings.When == "always" || _s.Fortnite.Running));

    private int OverlayValue() => _stats.OverlayValue(_overlaySettings.Show, _s.Processor.Status, _s.Fortnite.Running);

    private void SyncOverlay()
    {
        if (!OverlayVisible)
        {
            if (_overlay != null)
            {
                _overlay.Close();
                _overlay.Dispose();
                _overlay = null;
            }
            return;
        }
        if (_overlay == null)
        {
            _overlay = new OverlayWindow(_overlaySettings.Clone(), OverlayValue());
            _overlay.Moved += (x, y) =>
            {
                _overlaySettings.X = x;
                _overlaySettings.Y = y;
                _overlaySettings.Save(_s.Db);
            };
            _overlay.Show();
        }
        _overlay.Apply(_overlaySettings.Clone());
        _overlay.SetMoving(_s.Ui.OverlayMoving);
        _overlay.SetValue(OverlayValue(), animate: false);
    }

    private void SetOverlayMoving(bool on)
    {
        _s.Ui.OverlayMoving = on;
        SyncOverlay();
    }

    private void RefreshOverlayValue()
    {
        try
        {
            _overlay?.SetValue(OverlayValue(), animate: true);
        }
        catch (Exception ex)
        {
            Log.Error("overlay value", ex);
        }
    }

    private void ToggleAutostart()
    {
        if (Autostart.IsEnabled())
        {
            Autostart.Disable();
        }
        else
        {
            Autostart.Enable(AppPaths.RunningInstalled ? AppPaths.InstalledExe : AppPaths.CurrentExe);
        }
        _autostart.Checked = Autostart.IsEnabled();
    }

    private int TotalMine() => _s.Db.Matches().Where(m => m.Status == "counted").Sum(m => m.Mine);

    private void UpdateTooltip()
    {
        try
        {
            var t = $"{AppInfo.DisplayName}: {TotalMine():N0} chests";
            _icon.Text = t.Length > 63 ? t[..63] : t;
        }
        catch
        {
            // db busy
        }
    }

    protected override void ExitThreadCore()
    {
        _poll.Stop();
        _poll.Dispose();
        _overlay?.Close();
        _overlay?.Dispose();
        _window?.Close();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
