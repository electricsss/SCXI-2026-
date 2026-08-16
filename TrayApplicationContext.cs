using System.Drawing;
using System.Windows.Forms;


// =====================================================================
// SCXI TRAY APPLICATION
// =====================================================================

internal sealed class TrayApplicationContext :
    ApplicationContext
{
    private readonly ScxiService _service =
        new();


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


    private readonly System.Windows.Forms.Timer
        _startupTimer;


    // Poll physical-controller connection state.
    private readonly System.Windows.Forms.Timer
        _statusTimer;


    private readonly Icon
        _enabledIcon;


    private readonly Icon
        _disabledIcon;


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
        // ICONS
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
        // STATUS
        // =============================================================

        _statusItem =
            new ToolStripMenuItem(
                "Status: Starting..."
            )
            {
                Enabled = false
            };


        // =============================================================
        // ENABLED
        // =============================================================

        _enabledItem =
            new ToolStripMenuItem(
                "Enabled"
            )
            {
                Checked = false,

                CheckOnClick = false
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
        // QUIT
        // =============================================================

        _quitItem =
            new ToolStripMenuItem(
                "Quit"
            );


        _quitItem.Click +=
            QuitItem_Click;


        // =============================================================
        // MENU
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
                Icon = _disabledIcon,

                Text = "SCXI - Starting",

                ContextMenuStrip = _menu,

                Visible = true
            };


        // =============================================================
        // STARTUP TIMER
        // =============================================================

        _startupTimer =
            new System.Windows.Forms.Timer
            {
                Interval = 100
            };


        _startupTimer.Tick +=
            StartupTimer_Tick;


        _startupTimer.Start();


        // =============================================================
        // CONTROLLER STATUS TIMER
        //
        // Checks four times per second whether the physical
        // Steam Controller is currently detected.
        // =============================================================

        _statusTimer =
            new System.Windows.Forms.Timer
            {
                Interval = 250
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


        if (!File.Exists(path))
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
    // INITIAL START
    // =================================================================

    private async void StartupTimer_Tick(
        object? sender,
        EventArgs e
    )
    {
        _startupTimer.Stop();


        await SetEnabledAsync(
            true
        );
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
            enable
        );
    }


    private async Task SetEnabledAsync(
        bool enabled
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
        // -------------------------------------------------------------
        // BRIDGE DISABLED
        // -------------------------------------------------------------

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


        // -------------------------------------------------------------
        // BRIDGE ENABLED BUT NO PHYSICAL CONTROLLER
        // -------------------------------------------------------------

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


        // -------------------------------------------------------------
        // PHYSICAL CONTROLLER DETECTED
        // -------------------------------------------------------------

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
    // ICON STATE
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
