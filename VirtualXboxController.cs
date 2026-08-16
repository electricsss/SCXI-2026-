using System.IO;

using Viiper.Client;
using Viiper.Client.Devices.Xbox360;
using Viiper.Client.Types;


// =====================================================================
// VIRTUAL XBOX CONTROLLER
//
// Creates and manages the VIIPER virtual Xbox 360 controller.
//
// Input:
//     SCXI -> VIIPER -> XInput
//
// Feedback:
//     Game -> XInput -> VIIPER -> SCXI
// =====================================================================

internal sealed class VirtualXboxController :
    IAsyncDisposable
{
    private ViiperClient?
        _client;


    private ViiperDevice?
        _device;


    private uint
        _busId;


    private string?
        _deviceId;


    // =================================================================
    // RUMBLE FEEDBACK READER
    //
    // The VIIPER output stream is long-lived. We need an explicit
    // cancellation signal so shutdown does not wait forever for
    // another rumble packet.
    // =================================================================

    private readonly CancellationTokenSource
        _outputCancellation =
            new();


    private int _disposing =
        0;


    // =================================================================
    // RUMBLE FEEDBACK EVENT
    //
    // Values:
    //
    // left  = 0..255
    // right = 0..255
    // =================================================================

    public event Action<byte, byte>?
        RumbleReceived;


    // =================================================================
    // DEVICE
    // =================================================================

    public ViiperDevice Device =>
        _device
        ?? throw new InvalidOperationException(
            "Virtual Xbox controller has not been started."
        );


    // =================================================================
    // START
    // =================================================================

    public async Task StartAsync()
    {
        Console.WriteLine(
            "Connecting SCXI to VIIPER..."
        );


        // =============================================================
        // CLIENT
        // =============================================================

        _client =
            new ViiperClient(
                "localhost",
                3242
            );


        // =============================================================
        // FIND OR CREATE BUS
        // =============================================================

        var buses =
            await _client
                .BusListAsync();


        if (buses.Buses.Length >
            0)
        {
            _busId =
                buses.Buses[0];


            Console.WriteLine(
                $"Using VIIPER bus {_busId}."
            );
        }
        else
        {
            var bus =
                await _client
                    .BusCreateAsync(
                        null,
                        CancellationToken.None
                    );


            _busId =
                bus.BusID;


            Console.WriteLine(
                $"Created VIIPER bus {_busId}."
            );
        }


        // =============================================================
        // CREATE XBOX 360 DEVICE
        // =============================================================

        var request =
            new DeviceCreateRequest
            {
                Type =
                    "xbox360"
            };


        var created =
            await _client
                .BusDeviceAddAsync(
                    _busId,
                    request
                );


        _deviceId =
            created.DevID;


        Console.WriteLine(
            $"Created virtual Xbox controller: " +
            $"{_busId}-{_deviceId}"
        );


        // =============================================================
        // CONNECT DEVICE
        // =============================================================

        _device =
            await _client
                .ConnectDeviceAsync(
                    _busId,
                    _deviceId
                );


        // =============================================================
        // LISTEN FOR XINPUT FEEDBACK
        // =============================================================

        _device.OnOutput +=
            HandleOutputAsync;


        Console.WriteLine(
            "Virtual Xbox controller connected."
        );


        Console.WriteLine(
            "[SCXI] Rumble feedback ready."
        );


        Console.WriteLine();
    }


    // =================================================================
    // VIIPER OUTPUT
    //
    // Xbox 360 feedback packets:
    //
    // byte 0 = left motor
    // byte 1 = right motor
    // =================================================================

    private async Task HandleOutputAsync(
        Stream stream
    )
    {
        byte[] packet =
            new byte[2];


        CancellationToken cancellationToken =
            _outputCancellation.Token;


        try
        {
            while (
                !cancellationToken.IsCancellationRequested
            )
            {
                int received =
                    0;


                while (received <
                    packet.Length)
                {
                    int count =
                        await stream.ReadAsync(
                            packet.AsMemory(
                                received,
                                packet.Length -
                                received
                            ),
                            cancellationToken
                        );


                    // VIIPER closed the stream.
                    if (count ==
                        0)
                    {
                        return;
                    }


                    received +=
                        count;
                }


                // Shutdown may have started between
                // receiving the packet and raising the event.
                if (
                    cancellationToken.IsCancellationRequested ||
                    System.Threading.Volatile.Read(
                        ref _disposing
                    ) == 1
                )
                {
                    return;
                }


                byte leftMotor =
                    packet[0];


                byte rightMotor =
                    packet[1];


                try
                {
                    RumbleReceived?.Invoke(
                        leftMotor,
                        rightMotor
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "[SCXI] Rumble event error: " +
                        ex.Message
                    );
                }
            }
        }
        catch (
            OperationCanceledException
        )
        {
            // Expected during SCXI shutdown.
        }
        catch (
            ObjectDisposedException
        )
        {
            // Expected during SCXI shutdown.
        }
        catch (
            IOException
        )
        {
            // Expected when VIIPER closes
            // the virtual-device stream.
        }
        catch (Exception ex)
        {
            if (
                System.Threading.Volatile.Read(
                    ref _disposing
                ) == 0
            )
            {
                Console.WriteLine(
                    "[SCXI] Rumble listener error: " +
                    ex.Message
                );
            }
        }
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


        await _device.SendAsync(
            new Xbox360Input
            {
                Buttons =
                    0,

                Lt =
                    0,

                Rt =
                    0,

                Lx =
                    0,

                Ly =
                    0,

                Rx =
                    0,

                Ry =
                    0,

                Reserved =
                    new byte[6]
            }
        );


        Console.WriteLine(
            "[SCXI] Virtual Xbox state reset."
        );
    }


    // =================================================================
    // CLEANUP
    // =================================================================

    public async ValueTask DisposeAsync()
    {
        if (
            System.Threading.Interlocked.Exchange(
                ref _disposing,
                1
            ) == 1
        )
        {
            return;
        }


        // =============================================================
        // NEUTRALIZE VIRTUAL CONTROLLER
        // =============================================================

        if (_device is not null)
        {
            try
            {
                await ResetAsync();
            }
            catch
            {
            }
        }


        // =============================================================
        // STOP ACCEPTING RUMBLE EVENTS
        // =============================================================

        if (_device is not null)
        {
            try
            {
                _device.OnOutput -=
                    HandleOutputAsync;
            }
            catch
            {
            }
        }


        RumbleReceived =
            null;


        // =============================================================
        // CANCEL LONG-LIVED OUTPUT STREAM READER
        //
        // This is the important shutdown fix.
        // =============================================================

        try
        {
            _outputCancellation.Cancel();
        }
        catch
        {
        }


        // =============================================================
        // REMOVE VIIPER DEVICE FIRST
        //
        // Removing the virtual device causes VIIPER to close the
        // underlying device stream. We intentionally do this BEFORE
        // disposing ViiperDevice so its output callback cannot leave
        // shutdown waiting on a blocked Stream.ReadAsync().
        // =============================================================

        if (_client is not null &&
            _deviceId is not null)
        {
            try
            {
                Console.WriteLine(
                    "[SCXI] Removing virtual controller..."
                );


                await _client
                    .BusDeviceRemoveAsync(
                        _busId,
                        _deviceId
                    );


                Console.WriteLine(
                    "[SCXI] Virtual controller removed."
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[SCXI] Virtual controller removal error: " +
                    ex.Message
                );
            }
        }


        _deviceId =
            null;


        // =============================================================
        // DISPOSE DEVICE OBJECT
        //
        // At this point:
        //
        // - rumble callbacks are unsubscribed
        // - the output reader is cancelled
        // - VIIPER has been told to remove the device
        // - the stream should be closing/closed
        //
        // So Dispose() should no longer block.
        // =============================================================

        if (_device is not null)
        {
            try
            {
                _device.Dispose();
            }
            catch
            {
            }


            _device =
                null;
        }


        // =============================================================
        // CLIENT
        // =============================================================

        if (_client is not null)
        {
            try
            {
                _client.Dispose();
            }
            catch
            {
            }


            _client =
                null;
        }


        // =============================================================
        // OUTPUT CANCELLATION
        // =============================================================

        try
        {
            _outputCancellation.Dispose();
        }
        catch
        {
        }
    }
}
