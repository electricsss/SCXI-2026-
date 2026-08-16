using System.Drawing;
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
        // LOAD ICONS
        // =============================================================

        _enabledIcon =
            LoadIcon(
                "scxi-enabled.ico"
            );


        _disabledIcon =
            LoadIcon(
                "scxi-disabled.ico"
            );


        // =============================================================
        // STATUS ITEM
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
        // ENABLE / DISABLE ITEM
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
        // REFRESH ITEM
        // =============================================================

        _refreshItem =
            new ToolStripMenuItem(
                "Refresh Devices"
            );


        _refreshItem.Click +=
            RefreshItem_Click;


        // =============================================================
        // QUIT ITEM
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
            _quitItem
        );


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
        //
        // Wait briefly for the WinForms message loop to start,
        // then apply the saved Enabled preference.
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
        //
        // Keeps the red/green tray icon synchronized with the
        // physical Steam Controller.
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
    // ICON LOADER
    // =================================================================

    private static Icon LoadIcon(
        string fileName
    )
    {
        string path =
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                fileName
            );


        if (!File.Exists(
                path
            ))
        {
            return (Icon)
                SystemIcons.Application.Clone();
        }


        using var stream =
            File.OpenRead(
                path
            );


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


        // =============================================================
        // REMEMBERED ENABLED STATE
        // =============================================================

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


            // Do NOT start VIIPER or create a virtual controller.
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
    // ENABLE / DISABLE CLICK
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


    // =================================================================
    // SET ENABLED STATE
    // =================================================================

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
            // =========================================================
            // ENABLE
            // =========================================================

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

            // =========================================================
            // DISABLE
            // =========================================================

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


            // =========================================================
            // SAVE USER CHOICE
            //
            // Startup does not rewrite the settings file.
            // Only a manual Enabled toggle changes the preference.
            // =========================================================

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


            _notifyIcon.BalloonTipTitle =
                "SCXI";


            _notifyIcon.BalloonTipText =
                ex.Message;


            _notifyIcon.BalloonTipIcon =
                ToolTipIcon.Error;


            _notifyIcon.ShowBalloonTip(
                5000
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
    // VISUAL STATE
    // =================================================================

    private void UpdateVisualState()
    {
        // =============================================================
        // SCXI DISABLED
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
        // SCXI ENABLED, CONTROLLER NOT DETECTED
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
    // REFRESH DEVICES
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


            _notifyIcon.BalloonTipTitle =
                "SCXI";


            _notifyIcon.BalloonTipText =
                ex.Message;


            _notifyIcon.BalloonTipIcon =
                ToolTipIcon.Error;


            _notifyIcon.ShowBalloonTip(
                5000
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
    // TRAY ICON STATE
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


        // IMPORTANT:
        //
        // We intentionally do NOT change _settings.Enabled here.
        //
        // Quitting the app is different from disabling SCXI.
        // The user's last Enabled/Disabled choice should survive.


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
