using System.Drawing;
using System.Windows.Forms;


// =====================================================================
// SCXI TRAY APPLICATION
// =====================================================================

internal sealed class TrayApplicationContext :
    ApplicationContext,
    IDisposable
{
    private readonly ScxiService _service =
        new();


    private readonly NotifyIcon _notifyIcon;

    private readonly ContextMenuStrip _menu;

    private readonly ToolStripMenuItem _statusItem;

    private readonly ToolStripMenuItem _enabledItem;

    private readonly ToolStripMenuItem _refreshItem;

    private readonly ToolStripMenuItem _quitItem;


    private readonly System.Windows.Forms.Timer
        _startupTimer;


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
        //
        // Temporary icon.
        // Later we'll replace this with the actual SCXI icon.
        // =============================================================

        _notifyIcon =
            new NotifyIcon
            {
                Icon =
                    SystemIcons.Application,

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
        // This allows the Windows Forms message loop to begin before
        // we start the Raw Input listener.
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


                await _service
                    .StartAsync();
            }
            else
            {
                _statusItem.Text =
                    "Status: Stopping...";


                _notifyIcon.Text =
                    "SCXI - Stopping";


                await _service
                    .StopAsync();
            }


            UpdateMenuState();
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


            await _service
                .RefreshAsync();


            UpdateMenuState();
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
    // UPDATE MENU
    // =================================================================

    private void UpdateMenuState()
    {
        if (_service.IsRunning)
        {
            _statusItem.Text =
                "Status: Enabled";


            _notifyIcon.Text =
                "SCXI - Enabled";


            _enabledItem.Checked =
                true;


            _refreshItem.Enabled =
                true;
        }
        else
        {
            _statusItem.Text =
                "Status: Disabled";


            _notifyIcon.Text =
                "SCXI - Disabled";


            _enabledItem.Checked =
                false;


            _refreshItem.Enabled =
                false;
        }
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


        try
        {
            await _service
                .DisposeAsync();
        }
        catch
        {
        }


        _startupTimer.Dispose();


        _notifyIcon.Visible =
            false;


        _notifyIcon.Dispose();


        _menu.Dispose();


        ExitThread();
    }


    // =================================================================
    // DISPOSE
    // =================================================================

    public new void Dispose()
    {
        _startupTimer.Dispose();

        _notifyIcon.Visible =
            false;

        _notifyIcon.Dispose();

        _menu.Dispose();

        base.Dispose();
    }
}
