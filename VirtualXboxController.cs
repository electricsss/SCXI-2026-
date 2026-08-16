using Viiper.Client;
using Viiper.Client.Types;
using Viiper.Client.Devices.Xbox360;


// =====================================================================
// VIRTUAL XBOX CONTROLLER
//
// Owns the VIIPER Xbox 360 device used by SCXI.
//
// Responsibilities:
//
// - Connect to VIIPER
// - Find/create a VIIPER bus
// - Create the Xbox 360 device
// - Expose the live ViiperDevice to the input listener
// - Reset the controller on shutdown
// - Remove the virtual controller cleanly
// =====================================================================

internal sealed class VirtualXboxController :
    IAsyncDisposable
{
    private ViiperClient? _client;

    private ViiperDevice? _device;

    private uint _busId;

    private string? _deviceId;

    private bool _started;


    // =================================================================
    // LIVE DEVICE
    // =================================================================

    public ViiperDevice Device
    {
        get
        {
            if (_device is null)
            {
                throw new InvalidOperationException(
                    "Virtual Xbox controller has not been started."
                );
            }

            return _device;
        }
    }


    // =================================================================
    // START
    // =================================================================

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }


        Console.WriteLine(
            "[SCXI] Connecting to VIIPER..."
        );


        _client =
            new ViiperClient(
                "localhost",
                3242
            );


        // =============================================================
        // FIND OR CREATE BUS
        // =============================================================

        var buses =
            await _client.BusListAsync();


        if (buses.Buses.Length == 0)
        {
            var bus =
                await _client.BusCreateAsync(
                    null
                );

            _busId =
                bus.BusID;


            Console.WriteLine(
                $"[SCXI] Created VIIPER bus {_busId}."
            );
        }
        else
        {
            _busId =
                buses.Buses[0];


            Console.WriteLine(
                $"[SCXI] Using VIIPER bus {_busId}."
            );
        }


        // =============================================================
        // CREATE XBOX 360 DEVICE
        // =============================================================

        var request =
            new DeviceCreateRequest
            {
                Type = "xbox360"
            };


        var response =
            await _client.BusDeviceAddAsync(
                _busId,
                request
            );


        _deviceId =
            response.DevID;


        Console.WriteLine(
            $"[SCXI] Created virtual Xbox controller: " +
            $"{response.BusID}-{response.DevID}"
        );


        // =============================================================
        // CONNECT TO DEVICE
        // =============================================================

        _device =
            await _client.ConnectDeviceAsync(
                _busId,
                _deviceId
            );


        _started =
            true;


        Console.WriteLine(
            "[SCXI] Virtual Xbox controller connected."
        );
    }


    // =================================================================
    // RESET
    // =================================================================

    public async Task ResetAsync()
    {
        if (_device is null)
        {
            return;
        }


        try
        {
            await _device.SendAsync(
                CreateNeutralState()
            );


            Console.WriteLine(
                "[SCXI] Virtual Xbox state reset."
            );
        }
        catch
        {
            // Shutdown should continue even if
            // the virtual device is already gone.
        }
    }


    // =================================================================
    // NEUTRAL STATE
    // =================================================================

    private static Xbox360Input
        CreateNeutralState()
    {
        return new Xbox360Input
        {
            Buttons = 0,

            Lt = 0,
            Rt = 0,

            Lx = 0,
            Ly = 0,

            Rx = 0,
            Ry = 0
        };
    }


    // =================================================================
    // CLEANUP
    // =================================================================

    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            return;
        }


        await ResetAsync();


        // =============================================================
        // CLOSE DEVICE CONNECTION
        // =============================================================

        if (_device is not null)
        {
            try
            {
                await _device.DisposeAsync();
            }
            catch
            {
            }

            _device =
                null;
        }


        // =============================================================
        // REMOVE DEVICE FROM VIIPER
        // =============================================================

        if (_client is not null &&
            _deviceId is not null)
        {
            try
            {
                await _client.BusDeviceRemoveAsync(
                    _busId,
                    _deviceId
                );


                Console.WriteLine(
                    "[SCXI] Virtual controller removed."
                );
            }
            catch
            {
            }
        }


        _deviceId =
            null;

        _client =
            null;

        _started =
            false;
    }
}
