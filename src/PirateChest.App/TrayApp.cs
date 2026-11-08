using System.Diagnostics;
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
}

/// <summary>Tray icon and menu: open statistics, recount, start with Windows, exit (in the interface language).</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly AppServices _s;
    private readonly string _url;
    private readonly ToolStripMenuItem _autostart;
    private readonly SynchronizationContext _ui;

    public TrayApp(AppServices s, string url, bool openBrowser, EventWaitHandle exitEvent)
    {
        _s = s;
        _url = url;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("", null, (_, _) => OpenBrowser()) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };
        var recount = new ToolStripMenuItem("", null, (_, _) =>
        {
            _s.Processor.RecountAll();
            Notify(Lang.T("Пересчёт запущен", "Recount started"), Lang.T("Все реплеи будут посчитаны заново.", "All replays will be counted again."));
        });
        _autostart = new ToolStripMenuItem("", null, (_, _) => ToggleAutostart()) { Checked = Autostart.IsEnabled() };
        var exit = new ToolStripMenuItem("", null, (_, _) => ExitThread());
        menu.Items.AddRange(new ToolStripItem[] { open, recount, _autostart, new ToolStripSeparator(), exit });
        menu.Opening += (_, _) =>
        {
            open.Text = Lang.T("Открыть статистику", "Open statistics");
            recount.Text = Lang.T("Пересчитать всё", "Recount everything");
            _autostart.Text = Lang.T("Автозапуск", "Start with Windows");
            exit.Text = Lang.T("Выход", "Exit");
            _autostart.Checked = Autostart.IsEnabled();
        };
        Lang.Changed += () => _ui.Post(_ => UpdateTooltip(), null);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Pirate Chest Counter",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenBrowser();
        _icon.BalloonTipClicked += (_, _) => OpenBrowser();

        s.Processor.MatchProcessed += OnMatch;
        s.Processor.BatchFinished += OnBatch;
        s.Updater.RequestExit = () => _ui.Post(_ => ExitThread(), null);
        s.Updater.CanApplyNow = () => !s.Processor.Busy;

        // Exit when another process (installer/uninstaller) asks us to.
        ThreadPool.RegisterWaitForSingleObject(exitEvent, (_, _) => _ui.Post(_ => ExitThread(), null), null, Timeout.Infinite, true);

        UpdateTooltip();
        if (openBrowser)
        {
            OpenBrowser();
        }
    }

    private static Icon LoadIcon()
    {
        using var st = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
        return st != null ? new Icon(st, SystemInformation.SmallIconSize) : SystemIcons.Application;
    }

    public void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("open browser", ex);
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

    private void OnMatch(MatchSummary summary, bool bulk)
    {
        _ui.Post(_ => UpdateTooltip(), null);
        if (bulk || !summary.Counted)
        {
            return;
        }
        var total = TotalMine();
        var k = summary.Count.Mine;
        _ui.Post(_ => Notify(Lang.T("Матч посчитан", "Match counted"),
            Lang.T($"+{k} {Lang.Chests(k)} (мои) · всего {total}", $"+{k} {Lang.Chests(k)} (mine) · {total} in total")), null);
    }

    private void OnBatch(int counted)
    {
        var total = TotalMine();
        _ui.Post(_ =>
        {
            UpdateTooltip();
            if (counted > 0)
            {
                Notify(Lang.T("Реплеи посчитаны", "Replays counted"),
                    Lang.T($"Матчей: {counted} · всего {total} {Lang.Chests(total)} (мои)", $"Matches: {counted} · {total} {Lang.Chests(total)} in total (mine)"));
            }
        }, null);
    }

    private void UpdateTooltip()
    {
        try
        {
            var t = Lang.T($"Сундуки: {TotalMine()} (мои)", $"Chests: {TotalMine()} (mine)");
            _icon.Text = t.Length > 63 ? t[..63] : t;
        }
        catch
        {
            // db busy
        }
    }

    /// <summary>Shown after a self-update so the user knows the new version is running.</summary>
    public void ShowUpdated() => Notify(Lang.T("Программа обновлена", "Program updated"),
        Lang.T($"Pirate Chest Counter {AppInfo.VersionText} установлен автоматически.", $"Pirate Chest Counter {AppInfo.VersionText} was installed automatically."));

    private void Notify(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(5000);
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
