using System.Drawing;
using System.Reflection;
using System.Windows.Forms;


// =====================================================================
// SCXI TRAY APPLICATION
// =====================================================================

internal sealed class TrayApplicationContext :
    ApplicationContext
{
    private readonly ScxiService
        _service =
            new();


    // =================================================================
    // SETTINGS
    // =================================================================

    private readonly ScxiSettings
        _settings;


    // =================================================================
    // TRAY UI
    // =================================================================

    private readonly NotifyIcon
        _notifyIcon;


    private readonly ContextMenuStrip
        _menu;


    private readonly ToolStripMenuItem
        _statusItem;


    private readonly ToolStripMenuItem
        _enabledItem;


    private readonly ToolStripMenuItem
        _refreshItem;


    private readonly ToolStripMenuItem
        _startWithWindowsItem;


    private readonly ToolStripMenuItem
        _quitItem;


    // =================================================================
    // TIMERS
    // =================================================================

    private readonly System.Windows.Forms.Timer
        _startupTimer;


    private readonly System.Windows.Forms.Timer
        _statusTimer;


    // =================================================================
    // ICONS
    // =================================================================

    private readonly Icon
        _enabledIcon;


    private readonly Icon
        _disabledIcon;


    // =================================================================
    // STATE
    // =================================================================

    private bool _busy =
        false;


    private bool _quitting =
        false;


    // =================================================================
    // CONSTRUCTOR
    // =================================================================

    public TrayApplicationContext()
    {
        // =============================================================
        // LOAD SETTINGS
        // =============================================================

        _settings =
            ScxiSettings.Load();


        // =============================================================
        // LOAD EMBEDDED ICONS
        // =============================================================

        _enabledIcon =
            LoadEmbeddedIcon(
                "SCXI.Assets.scxi-enabled.ico"
            );


        _disabledIcon =
            LoadEmbeddedIcon(
                "SCXI.Assets.scxi-disabled.ico"
            );


        // =============================================================
        // STATUS
        // =============================================================

        _statusItem =
            new ToolStripMenuItem(
                "Status: Starting..."
            )
            {
                Enabled =
                    false
            };


        // =============================================================
        // ENABLE / DISABLE
        // =============================================================

        _enabledItem =
            new ToolStripMenuItem(
                "Enabled"
            )
            {
                Checked =
                    false,

                CheckOnClick =
                    false
            };


        _enabledItem.Click +=
            EnabledItem_Click;


        // =============================================================
        // REFRESH
        // =============================================================

        _refreshItem =
            new ToolStripMenuItem(
                "Refresh Devices"
            );


        _refreshItem.Click +=
            RefreshItem_Click;


        // =============================================================
        // START WITH WINDOWS
        // =============================================================

        _startWithWindowsItem =
            new ToolStripMenuItem(
                "Start with Windows"
            )
            {
                Checked =
                    StartupManager.IsEnabled(),

                CheckOnClick =
                    false
            };


        _startWithWindowsItem.Click +=
            StartWithWindowsItem_Click;


        // =============================================================
        // QUIT
        // =============================================================

        _quitItem =
            new ToolStripMenuItem(
                "Quit"
            );


        _quitItem.Click +=
            QuitItem_Click;


        // =============================================================
        // CONTEXT MENU
        // =============================================================

        _menu =
            new ContextMenuStrip();


        _menu.Items.Add(
            _statusItem
        );


        _menu.Items.Add(
            new ToolStripSeparator()
        );


        _menu.Items.Add(
            _enabledItem
        );


        _menu.Items.Add(
            _refreshItem
        );


        _menu.Items.Add(
            new ToolStripSeparator()
        );


        _menu.Items.Add(
            _startWithWindowsItem
        );


        _menu.Items.Add(
            new ToolStripSeparator()
        );


        _menu.Items.Add(
            _quitItem
        );


        // Keep the startup checkbox synchronized
        // with the actual Windows registry value.
        _menu.Opening +=
            Menu_Opening;


        // =============================================================
        // TRAY ICON
        // =============================================================

        _notifyIcon =
            new NotifyIcon
            {
                Icon =
                    _disabledIcon,

                Text =
                    "SCXI - Starting",

                ContextMenuStrip =
                    _menu,

                Visible =
                    true
            };


        // =============================================================
        // STARTUP TIMER
        // =============================================================

        _startupTimer =
            new System.Windows.Forms.Timer
            {
                Interval =
                    100
            };


        _startupTimer.Tick +=
            StartupTimer_Tick;


        _startupTimer.Start();


        // =============================================================
        // CONTROLLER STATUS TIMER
        // =============================================================

        _statusTimer =
            new System.Windows.Forms.Timer
            {
                Interval =
                    250
            };


        _statusTimer.Tick +=
            StatusTimer_Tick;


        _statusTimer.Start();
    }


    // =================================================================
    // EMBEDDED ICON LOADER
    //
    // Icons are stored inside SCXI.exe.
    // No external Assets folder is required in the published build.
    // =================================================================

    private static Icon LoadEmbeddedIcon(
        string resourceName
    )
    {
        Assembly assembly =
            Assembly.GetExecutingAssembly();


        using Stream? stream =
            assembly.GetManifestResourceStream(
                resourceName
            );


        if (stream is null)
        {
            Console.WriteLine(
                "[SCXI] Embedded icon not found: " +
                resourceName
            );


            return (Icon)
                SystemIcons.Application.Clone();
        }


        using var icon =
            new Icon(
                stream
            );


        return (Icon)
            icon.Clone();
    }


    // =================================================================
    // STARTUP
    // =================================================================

    private async void StartupTimer_Tick(
        object? sender,
        EventArgs e
    )
    {
        _startupTimer.Stop();


        if (_settings.Enabled)
        {
            Console.WriteLine(
                "[SCXI] Saved state: Enabled."
            );


            await SetEnabledAsync(
                true,
                savePreference: false
            );
        }
        else
        {
            Console.WriteLine(
                "[SCXI] Saved state: Disabled."
            );


            UpdateVisualState();
        }
    }


    // =================================================================
    // STATUS POLLING
    // =================================================================

    private void StatusTimer_Tick(
        object? sender,
        EventArgs e
    )
    {
        if (_busy ||
            _quitting)
        {
            return;
        }


        UpdateVisualState();
    }


    // =================================================================
    // MENU OPENING
    // =================================================================

    private void Menu_Opening(
        object? sender,
        System.ComponentModel.CancelEventArgs e
    )
    {
        _startWithWindowsItem.Checked =
            StartupManager.IsEnabled();
    }


    // =================================================================
    // ENABLE / DISABLE
    // =================================================================

    private async void EnabledItem_Click(
        object? sender,
        EventArgs e
    )
    {
        if (_busy ||
            _quitting)
        {
            return;
        }


        bool enable =
            !_service.IsRunning;


        await SetEnabledAsync(
            enable,
            savePreference: true
        );
    }


    private async Task SetEnabledAsync(
        bool enabled,
        bool savePreference
    )
    {
        if (_busy)
        {
            return;
        }


        SetBusy(
            true
        );


        try
        {
            if (enabled)
            {
                _statusItem.Text =
                    "Status: Starting...";


                _notifyIcon.Text =
                    "SCXI - Starting";


                SetTrayIcon(
                    false
                );


                await _service
                    .StartAsync();
            }
            else
            {
                _statusItem.Text =
                    "Status: Stopping...";


                _notifyIcon.Text =
                    "SCXI - Stopping";


                SetTrayIcon(
                    false
                );


                await _service
                    .StopAsync();
            }


            if (savePreference)
            {
                _settings.Enabled =
                    enabled;


                _settings.Save();
            }


            UpdateVisualState();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCXI] Error: {ex}"
            );


            _statusItem.Text =
                "Status: Error";


            _notifyIcon.Text =
                "SCXI - Error";


            _enabledItem.Checked =
                false;


            SetTrayIcon(
                false
            );


            ShowError(
                "SCXI Error",
                ex.Message
            );
        }
        finally
        {
            SetBusy(
                false
            );
        }
    }


    // =================================================================
    // START WITH WINDOWS
    // =================================================================

    private void StartWithWindowsItem_Click(
        object? sender,
        EventArgs e
    )
    {
        if (_busy ||
            _quitting)
        {
            return;
        }


        bool currentlyEnabled =
            StartupManager.IsEnabled();


        bool success;


        if (currentlyEnabled)
        {
            success =
                StartupManager.Disable();
        }
        else
        {
            success =
                StartupManager.Enable();
        }


        if (!success)
        {
            _startWithWindowsItem.Checked =
                StartupManager.IsEnabled();


            ShowError(
                "SCXI",
                currentlyEnabled
                    ? "Could not disable Start with Windows."
                    : "Could not enable Start with Windows."
            );


            return;
        }


        _startWithWindowsItem.Checked =
            StartupManager.IsEnabled();


        if (_startWithWindowsItem.Checked)
        {
            _notifyIcon.BalloonTipTitle =
                "SCXI";


            _notifyIcon.BalloonTipText =
                "SCXI will now start when you sign in to Windows.";


            _notifyIcon.BalloonTipIcon =
                ToolTipIcon.Info;


            _notifyIcon.ShowBalloonTip(
                2500
            );
        }
    }


    // =================================================================
    // VISUAL STATE
    // =================================================================

    private void UpdateVisualState()
    {
        // =============================================================
        // DISABLED
        // =============================================================

        if (!_service.IsRunning)
        {
            _statusItem.Text =
                "Status: Disabled";


            _notifyIcon.Text =
                "SCXI - Disabled";


            _enabledItem.Checked =
                false;


            _refreshItem.Enabled =
                false;


            SetTrayIcon(
                false
            );


            return;
        }


        // =============================================================
        // WAITING FOR CONTROLLER
        // =============================================================

        if (!_service.IsControllerDetected)
        {
            _statusItem.Text =
                "Status: Waiting for Steam Controller...";


            _notifyIcon.Text =
                "SCXI - Waiting for Controller";


            _enabledItem.Checked =
                true;


            _refreshItem.Enabled =
                true;


            SetTrayIcon(
                false
            );


            return;
        }


        // =============================================================
        // CONTROLLER CONNECTED
        // =============================================================

        _statusItem.Text =
            "Status: Controller Connected";


        _notifyIcon.Text =
            "SCXI - Controller Connected";


        _enabledItem.Checked =
            true;


        _refreshItem.Enabled =
            true;


        SetTrayIcon(
            true
        );
    }


    // =================================================================
    // REFRESH
    // =================================================================

    private async void RefreshItem_Click(
        object? sender,
        EventArgs e
    )
    {
        if (_busy ||
            _quitting ||
            !_service.IsRunning)
        {
            return;
        }


        SetBusy(
            true
        );


        try
        {
            _statusItem.Text =
                "Status: Refreshing...";


            _notifyIcon.Text =
                "SCXI - Refreshing";


            SetTrayIcon(
                false
            );


            await _service
                .RefreshAsync();


            UpdateVisualState();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCXI] Refresh error: {ex}"
            );


            _statusItem.Text =
                "Status: Error";


            _notifyIcon.Text =
                "SCXI - Error";


            SetTrayIcon(
                false
            );


            ShowError(
                "SCXI Refresh Error",
                ex.Message
            );
        }
        finally
        {
            SetBusy(
                false
            );
        }
    }


    // =================================================================
    // ERROR BALLOON
    // =================================================================

    private void ShowError(
        string title,
        string message
    )
    {
        _notifyIcon.BalloonTipTitle =
            title;


        _notifyIcon.BalloonTipText =
            message;


        _notifyIcon.BalloonTipIcon =
            ToolTipIcon.Error;


        _notifyIcon.ShowBalloonTip(
            5000
        );
    }


    // =================================================================
    // TRAY ICON
    // =================================================================

    private void SetTrayIcon(
        bool controllerConnected
    )
    {
        _notifyIcon.Icon =
            controllerConnected
                ? _enabledIcon
                : _disabledIcon;
    }


    // =================================================================
    // BUSY STATE
    // =================================================================

    private void SetBusy(
        bool busy
    )
    {
        _busy =
            busy;


        _enabledItem.Enabled =
            !busy;


        _refreshItem.Enabled =
            !busy &&
            _service.IsRunning;


        _startWithWindowsItem.Enabled =
            !busy;


        _quitItem.Enabled =
            !busy;
    }


    // =================================================================
    // QUIT
    // =================================================================

    private async void QuitItem_Click(
        object? sender,
        EventArgs e
    )
    {
        await QuitAsync();
    }


    private async Task QuitAsync()
    {
        if (_quitting)
        {
            return;
        }


        _quitting =
            true;


        SetBusy(
            true
        );


        _statusItem.Text =
            "Status: Shutting down...";


        _notifyIcon.Text =
            "SCXI - Shutting down";


        SetTrayIcon(
            false
        );


        _statusTimer.Stop();


        try
        {
            await _service
                .DisposeAsync();
        }
        catch
        {
        }


        _notifyIcon.Visible =
            false;


        ExitThread();
    }


    // =================================================================
    // CLEANUP
    // =================================================================

    protected override void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _startupTimer.Dispose();

            _statusTimer.Dispose();


            _notifyIcon.Visible =
                false;


            _notifyIcon.Dispose();


            _menu.Dispose();


            _enabledIcon.Dispose();

            _disabledIcon.Dispose();
        }


        base.Dispose(
            disposing
        );
    }
}
