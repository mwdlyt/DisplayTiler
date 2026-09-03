using DisplayTiler.Host.Services;

namespace DisplayTiler.Host;

internal static class Program
{
    private const string SingleInstanceName = @"Local\DisplayTiler.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance) return;

        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        settings.StartWithWindows = StartupManager.IsEnabled();

        using var controller = new SwitcherController();
        using var menu = new ContextMenuStrip();
        using var settingsItem = new ToolStripMenuItem("Settings…");
        using var enableSwitcher = new ToolStripMenuItem("Replace Windows Alt+Tab") { CheckOnClick = true };
        using var startWithWindows = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        using var openSwitcher = new ToolStripMenuItem("Open grouped switcher");
        using var behaviorMenu = new ToolStripMenuItem("Alt+Tab behavior");
        using var holdBehavior = new ToolStripMenuItem("Hold Alt and release to switch");
        using var stickyBehavior = new ToolStripMenuItem("Keep switcher open until selection");
        using var layoutMenu = new ToolStripMenuItem("Layout");
        using var packedGrid = new ToolStripMenuItem("Packed grid");
        using var categoryRows = new ToolStripMenuItem("Category rows");
        using var exit = new ToolStripMenuItem("Exit DisplayTiler");
        using var trayIcon = TrayIconFactory.Create();
        using var tray = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "DisplayTiler",
            Visible = true,
        };

        void RefreshUi()
        {
            controller.IsAltTabReplacementEnabled = settings.AltTabReplacementEnabled;
            controller.ActivateOnAltRelease = settings.ActivateOnAltRelease;
            controller.SetLayoutMode(settings.LayoutMode);
            enableSwitcher.Checked = settings.AltTabReplacementEnabled;
            startWithWindows.Checked = settings.StartWithWindows;
            packedGrid.Checked = settings.LayoutMode == SwitcherLayoutMode.PackedGrid;
            categoryRows.Checked = settings.LayoutMode == SwitcherLayoutMode.CategoryRows;
            holdBehavior.Checked = settings.ActivateOnAltRelease;
            stickyBehavior.Checked = !settings.ActivateOnAltRelease;
            tray.Text = settings.AltTabReplacementEnabled
                ? "DisplayTiler - Alt+Tab replacement on"
                : "DisplayTiler - Alt+Tab replacement off";
        }

        bool SaveSettings(bool showError = true)
        {
            try
            {
                StartupManager.SetEnabled(settings.StartWithWindows);
                settings.Save();
                RefreshUi();
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (showError)
                    MessageBox.Show(exception.Message, "DisplayTiler could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                settings.StartWithWindows = StartupManager.IsEnabled();
                RefreshUi();
                return false;
            }
        }

        void ShowSettings()
        {
            using var dialog = new SettingsForm(settings);
            if (dialog.ShowDialog() != DialogResult.OK) return;
            settings.AltTabReplacementEnabled = dialog.AltTabReplacementEnabled;
            settings.ActivateOnAltRelease = dialog.ActivateOnAltRelease;
            settings.StartWithWindows = dialog.StartWithWindows;
            settings.LayoutMode = dialog.LayoutMode;
            SaveSettings();
        }

        settingsItem.Click += (_, _) => ShowSettings();
        tray.DoubleClick += (_, _) => ShowSettings();
        enableSwitcher.Click += (_, _) =>
        {
            settings.AltTabReplacementEnabled = enableSwitcher.Checked;
            SaveSettings();
        };
        startWithWindows.Click += (_, _) =>
        {
            settings.StartWithWindows = startWithWindows.Checked;
            SaveSettings();
        };
        openSwitcher.Click += (_, _) => controller.ShowPreview();
        holdBehavior.Click += (_, _) =>
        {
            settings.ActivateOnAltRelease = true;
            SaveSettings();
        };
        stickyBehavior.Click += (_, _) =>
        {
            settings.ActivateOnAltRelease = false;
            SaveSettings();
        };
        packedGrid.Click += (_, _) =>
        {
            settings.LayoutMode = SwitcherLayoutMode.PackedGrid;
            SaveSettings();
        };
        categoryRows.Click += (_, _) =>
        {
            settings.LayoutMode = SwitcherLayoutMode.CategoryRows;
            SaveSettings();
        };
        exit.Click += (_, _) => Application.ExitThread();

        behaviorMenu.DropDownItems.AddRange([holdBehavior, stickyBehavior]);
        layoutMenu.DropDownItems.AddRange([packedGrid, categoryRows]);
        menu.Items.AddRange([
            settingsItem,
            new ToolStripSeparator(),
            enableSwitcher,
            startWithWindows,
            openSwitcher,
            behaviorMenu,
            layoutMenu,
            new ToolStripSeparator(),
            exit,
        ]);
        tray.ContextMenuStrip = menu;
        RefreshUi();

        // Opened by the installer on completion. Without it a fresh install finishes with no visible
        // sign that anything happened: the app has no main window, so it goes straight to the tray
        // and the only way to tell it worked is to try Alt+Tab and hope.
        // ShowDialog runs its own message loop, so the tray icon stays live while it is up, and
        // Application.Run below takes over once it closes.
        if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            ShowSettings();

        if (args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
            controller.ShowPreview();

        Application.Run();
        tray.Visible = false;
    }
}
