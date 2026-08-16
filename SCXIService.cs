// =====================================================================
// SCXI SERVICE
//
// Owns the complete SCXI runtime:
//
// Physical Steam Controller
//          ↓
//      Raw Input
//          ↓
//        SCXI
//          ↓
// Virtual Xbox 360 Controller
//          ↓
//        VIIPER
//          ↓
//        XInput
//
// Feedback travels the opposite direction:
//
// Game / XInput
//      ↓
// VIIPER
//      ↓
// VirtualXboxController
//      ↓
// SteamControllerHaptics
//      ↓
// Physical Steam Controller
// =====================================================================

internal sealed class ScxiService :
    IAsyncDisposable
{
    private readonly SemaphoreSlim
        _operationLock =
            new(1, 1);


    private ViiperProcessManager?
        _viiperManager;


    private VirtualXboxController?
        _xbox;


    private SteamRawInputListener?
        _listener;


    private SteamControllerHaptics?
        _haptics;


    // 0 = haptics not ready
    // 1 = haptics connected and ready for game rumble
    private int _hapticsReady =
        0;


    public bool IsRunning
    {
        get;
        private set;
    }


    // =================================================================
    // PHYSICAL CONTROLLER STATUS
    // =================================================================

    public bool IsControllerDetected =>
        IsRunning &&
        _listener?.IsControllerConnected == true;


    // =================================================================
    // START
    // =================================================================

    public async Task StartAsync()
    {
        await _operationLock.WaitAsync();


        try
        {
            if (IsRunning)
            {
                return;
            }


            Console.WriteLine(
                "[SCXI] Starting bridge..."
            );


            var viiperManager =
                new ViiperProcessManager();


            VirtualXboxController? xbox =
                null;


            SteamRawInputListener? listener =
                null;


            SteamControllerHaptics? haptics =
                null;


            try
            {
                // =====================================================
                // VIIPER
                // =====================================================

                await viiperManager
                    .EnsureRunningAsync();


                // =====================================================
                // VIRTUAL XBOX CONTROLLER
                // =====================================================

                xbox =
                    new VirtualXboxController();


                await xbox
                    .StartAsync();


                // =====================================================
                // PHYSICAL HAPTICS
                // =====================================================

                haptics =
                    new SteamControllerHaptics();


                // =====================================================
                // RAW INPUT LISTENER
                // =====================================================

                listener =
                    new SteamRawInputListener(
                        xbox.Device
                    );


                // =====================================================
                // STORE LIVE COMPONENTS
                // =====================================================

                _viiperManager =
                    viiperManager;


                _xbox =
                    xbox;


                _listener =
                    listener;


                _haptics =
                    haptics;


                System.Threading.Volatile.Write(
                    ref _hapticsReady,
                    0
                );


                IsRunning =
                    true;


                // =====================================================
                // PHYSICAL CONTROLLER EVENTS
                // =====================================================

                listener.ControllerConnected +=
                    ControllerConnected;


                listener.ControllerDisconnected +=
                    ControllerDisconnected;


                // =====================================================
                // GAME RUMBLE FEEDBACK
                // =====================================================

                xbox.RumbleReceived +=
                    RumbleReceived;


                Console.WriteLine(
                    "[SCXI] Bridge enabled."
                );


                Console.WriteLine(
                    "[SCXI] Waiting for Steam Controller..."
                );
            }
            catch
            {
                // =====================================================
                // PARTIAL STARTUP CLEANUP
                // =====================================================

                IsRunning =
                    false;


                System.Threading.Volatile.Write(
                    ref _hapticsReady,
                    0
                );


                if (listener is not null)
                {
                    try
                    {
                        listener.ControllerConnected -=
                            ControllerConnected;


                        listener.ControllerDisconnected -=
                            ControllerDisconnected;


                        listener.Dispose();
                    }
                    catch
                    {
                    }
                }


                if (xbox is not null)
                {
                    try
                    {
                        xbox.RumbleReceived -=
                            RumbleReceived;
                    }
                    catch
                    {
                    }
                }


                if (haptics is not null)
                {
                    try
                    {
                        haptics.Dispose();
                    }
                    catch
                    {
                    }
                }


                if (xbox is not null)
                {
                    try
                    {
                        await xbox
                            .DisposeAsync();
                    }
                    catch
                    {
                    }
                }


                try
                {
                    await viiperManager
                        .StopIfOwnedAsync();
                }
                catch
                {
                }


                _listener =
                    null;


                _haptics =
                    null;


                _xbox =
                    null;


                _viiperManager =
                    null;


                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }


    // =================================================================
    // PHYSICAL CONTROLLER CONNECTED
    //
    // Open the physical haptics interface and perform the short
    // confirmation buzz.
    //
    // Game rumble is ignored until this sequence finishes successfully.
    // =================================================================

    private async void ControllerConnected(
        string devicePath
    )
    {
        SteamControllerHaptics? haptics =
            _haptics;


        if (haptics is null ||
            !IsRunning)
        {
            return;
        }


        // Prevent game rumble from interfering
        // with the connection confirmation buzz.
        System.Threading.Volatile.Write(
            ref _hapticsReady,
            0
        );


        Console.WriteLine(
            "[SCXI] Initializing physical controller haptics..."
        );


        try
        {
            bool success =
                await haptics
                    .TestBuzzAsync(
                        devicePath
                    );


            if (!success)
            {
                Console.WriteLine(
                    "[SCXI] Physical haptics unavailable."
                );


                try
                {
                    haptics.Close();
                }
                catch
                {
                }


                return;
            }


            // The test buzz completed and the HID handle
            // remains open. Game rumble may now use it.
            System.Threading.Volatile.Write(
                ref _hapticsReady,
                1
            );


            Console.WriteLine(
                "[SCXI] Physical haptics ready."
            );


            Console.WriteLine(
                "[SCXI] Game rumble enabled."
            );
        }
        catch (Exception ex)
        {
            System.Threading.Volatile.Write(
                ref _hapticsReady,
                0
            );


            Console.WriteLine(
                "[SCXI] Physical haptics initialization error: " +
                ex.Message
            );


            try
            {
                haptics.Close();
            }
            catch
            {
            }
        }
    }


    // =================================================================
    // PHYSICAL CONTROLLER DISCONNECTED
    // =================================================================

    private void ControllerDisconnected()
    {
        // Stop accepting game rumble immediately.
        System.Threading.Volatile.Write(
            ref _hapticsReady,
            0
        );


        SteamControllerHaptics? haptics =
            _haptics;


        if (haptics is null)
        {
            return;
        }


        Console.WriteLine(
            "[SCXI] Closing physical haptics connection."
        );


        try
        {
            haptics.Close();
        }
        catch
        {
        }
    }


    // =================================================================
    // GAME RUMBLE RECEIVED
    //
    // Called by VirtualXboxController whenever VIIPER receives
    // Xbox 360 force-feedback from the game.
    //
    // left/right are Xbox-style 0..255 motor strengths.
    // =================================================================

    private void RumbleReceived(
        byte left,
        byte right
    )
    {
        if (!IsRunning)
        {
            return;
        }


        if (
            System.Threading.Volatile.Read(
                ref _hapticsReady
            ) != 1
        )
        {
            return;
        }


        SteamControllerHaptics? haptics =
            _haptics;


        if (haptics is null ||
            !haptics.IsOpen)
        {
            return;
        }


        try
        {
            haptics.SetXboxRumble(
                left,
                right
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Physical rumble error: " +
                ex.Message
            );
        }
    }


    // =================================================================
    // STOP
    // =================================================================

    public async Task StopAsync()
    {
        await _operationLock.WaitAsync();


        try
        {
            if (!IsRunning &&
                _listener is null &&
                _xbox is null &&
                _viiperManager is null &&
                _haptics is null)
            {
                return;
            }


            Console.WriteLine(
                "[SCXI] Stopping bridge..."
            );


            IsRunning =
                false;


            System.Threading.Volatile.Write(
                ref _hapticsReady,
                0
            );


            // =========================================================
            // DISCONNECT EVENTS FIRST
            //
            // This prevents any new controller or rumble callbacks
            // while shutdown is in progress.
            // =========================================================

            SteamRawInputListener? listener =
                _listener;


            if (listener is not null)
            {
                try
                {
                    listener.ControllerConnected -=
                        ControllerConnected;


                    listener.ControllerDisconnected -=
                        ControllerDisconnected;
                }
                catch
                {
                }
            }


            VirtualXboxController? xbox =
                _xbox;


            if (xbox is not null)
            {
                try
                {
                    xbox.RumbleReceived -=
                        RumbleReceived;
                }
                catch
                {
                }
            }


            // =========================================================
            // PHYSICAL HAPTICS
            //
            // Stop any physical vibration before destroying
            // the virtual controller.
            // =========================================================

            SteamControllerHaptics? haptics =
                _haptics;


            _haptics =
                null;


            if (haptics is not null)
            {
                try
                {
                    haptics.StopRumble();
                }
                catch
                {
                }


                try
                {
                    haptics.Dispose();
                }
                catch
                {
                }
            }


            // =========================================================
            // RAW INPUT
            // =========================================================

            _listener =
                null;


            if (listener is not null)
            {
                try
                {
                    listener.Dispose();
                }
                catch
                {
                }
            }


            // =========================================================
            // VIRTUAL XBOX CONTROLLER
            // =========================================================

            _xbox =
                null;


            if (xbox is not null)
            {
                try
                {
                    await xbox
                        .DisposeAsync();
                }
                catch
                {
                }
            }


            // =========================================================
            // VIIPER
            // =========================================================

            ViiperProcessManager? viiperManager =
                _viiperManager;


            _viiperManager =
                null;


            if (viiperManager is not null)
            {
                try
                {
                    await viiperManager
                        .StopIfOwnedAsync();
                }
                catch
                {
                }
            }


            Console.WriteLine(
                "[SCXI] Bridge disabled."
            );
        }
        finally
        {
            _operationLock.Release();
        }
    }


    // =================================================================
    // REFRESH
    // =================================================================

    public async Task RefreshAsync()
    {
        Console.WriteLine(
            "[SCXI] Refreshing devices..."
        );


        await StopAsync();


        await Task.Delay(
            250
        );


        await StartAsync();


        Console.WriteLine(
            "[SCXI] Device refresh complete."
        );
    }


    // =================================================================
    // CLEANUP
    // =================================================================

    public async ValueTask DisposeAsync()
    {
        await StopAsync();


        _operationLock.Dispose();
    }
}
