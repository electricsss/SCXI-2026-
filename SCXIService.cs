internal sealed class ScxiService :
    IAsyncDisposable
{
    private readonly SemaphoreSlim _operationLock =
        new(1, 1);

    private ViiperProcessManager? _viiperManager;

    private VirtualXboxController? _xbox;

    private SteamRawInputListener? _listener;


    public bool IsRunning { get; private set; }


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


            try
            {
                // -----------------------------------------------------
                // VIIPER
                // -----------------------------------------------------

                await viiperManager
                    .EnsureRunningAsync();


                // -----------------------------------------------------
                // VIRTUAL XBOX CONTROLLER
                // -----------------------------------------------------

                xbox =
                    new VirtualXboxController();


                await xbox.StartAsync();


                // -----------------------------------------------------
                // STEAM CONTROLLER INPUT
                // -----------------------------------------------------

                listener =
                    new SteamRawInputListener(
                        xbox.Device
                    );


                // -----------------------------------------------------
                // STORE LIVE COMPONENTS
                // -----------------------------------------------------

                _viiperManager =
                    viiperManager;


                _xbox =
                    xbox;


                _listener =
                    listener;


                IsRunning =
                    true;


                Console.WriteLine(
                    "[SCXI] Bridge enabled."
                );
            }
            catch
            {
                // -----------------------------------------------------
                // CLEAN UP PARTIAL STARTUP
                // -----------------------------------------------------

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


                if (xbox is not null)
                {
                    try
                    {
                        await xbox.DisposeAsync();
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


                throw;
            }
        }
        finally
        {
            _operationLock.Release();
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
                _viiperManager is null)
            {
                return;
            }


            Console.WriteLine(
                "[SCXI] Stopping bridge..."
            );


            IsRunning =
                false;


            // ---------------------------------------------------------
            // STOP RAW INPUT FIRST
            // ---------------------------------------------------------

            SteamRawInputListener? listener =
                _listener;


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


            // ---------------------------------------------------------
            // RESET + REMOVE VIRTUAL XBOX CONTROLLER
            // ---------------------------------------------------------

            VirtualXboxController? xbox =
                _xbox;


            _xbox =
                null;


            if (xbox is not null)
            {
                try
                {
                    await xbox.DisposeAsync();
                }
                catch
                {
                }
            }


            // ---------------------------------------------------------
            // STOP VIIPER ONLY IF SCXI STARTED IT
            // ---------------------------------------------------------

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
