using System.Windows.Forms;

using Viiper.Client;
using Viiper.Client.Types;
using Viiper.Client.Devices.Xbox360;


// =====================================================================
// SCXI
// Steam Controller -> XInput
// =====================================================================

internal static class Program
{
    [STAThread]
    static async Task Main()
    {
        Console.WriteLine("SCXI");
        Console.WriteLine("Steam Controller -> XInput");
        Console.WriteLine("==========================");
        Console.WriteLine();

        var viiperManager =
            new ViiperProcessManager();

        try
        {
            // =========================================================
            // MAKE SURE VIIPER IS AVAILABLE
            // =========================================================

            await viiperManager.EnsureRunningAsync();

            Console.WriteLine();
            Console.WriteLine(
                "Connecting SCXI to VIIPER..."
            );


            var client =
                new ViiperClient(
                    "localhost",
                    3242
                );


            // =========================================================
            // FIND OR CREATE VIIPER BUS
            // =========================================================

            var buses =
                await client.BusListAsync();

            uint busId;

            if (buses.Buses.Length == 0)
            {
                var bus =
                    await client.BusCreateAsync(null);

                busId =
                    bus.BusID;

                Console.WriteLine(
                    $"Created VIIPER bus {busId}."
                );
            }
            else
            {
                busId =
                    buses.Buses[0];

                Console.WriteLine(
                    $"Using VIIPER bus {busId}."
                );
            }


            // =========================================================
            // CREATE VIRTUAL XBOX 360 CONTROLLER
            // =========================================================

            var request =
                new DeviceCreateRequest
                {
                    Type = "xbox360"
                };


            var response =
                await client.BusDeviceAddAsync(
                    busId,
                    request
                );


            Console.WriteLine(
                $"Created virtual Xbox controller: " +
                $"{response.BusID}-{response.DevID}"
            );


            // =========================================================
            // CONNECT TO VIRTUAL CONTROLLER
            // =========================================================

            await using (
                var xbox =
                    await client.ConnectDeviceAsync(
                        busId,
                        response.DevID
                    )
            )
            {
                Console.WriteLine(
                    "Virtual Xbox controller connected."
                );

                Console.WriteLine();


                // =====================================================
                // START STEAM CONTROLLER LISTENER
                // =====================================================

                using var listener =
                    new SteamRawInputListener(
                        xbox
                    );


                Console.WriteLine(
                    "Waiting for Steam Controller..."
                );

                Console.WriteLine();

                Console.WriteLine(
                    "Press Ctrl+C to stop SCXI."
                );

                Console.WriteLine();


                // =====================================================
                // CTRL+C
                // =====================================================

                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;

                    Application.Exit();
                };


                // =====================================================
                // WINDOWS MESSAGE LOOP
                // =====================================================

                try
                {
                    Application.Run(
                        new ApplicationContext()
                    );
                }
                finally
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "[SCXI] Shutting down..."
                    );


                    // =================================================
                    // RESET VIRTUAL CONTROLLER
                    // =================================================

                    try
                    {
                        await xbox.SendAsync(
                            CreateNeutralXboxState()
                        );

                        Console.WriteLine(
                            "[SCXI] Virtual Xbox state reset."
                        );
                    }
                    catch
                    {
                    }


                    // =================================================
                    // REMOVE VIRTUAL CONTROLLER
                    // =================================================

                    try
                    {
                        await client.BusDeviceRemoveAsync(
                            busId,
                            response.DevID
                        );

                        Console.WriteLine(
                            "[SCXI] Virtual controller removed."
                        );
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "[SCXI] Fatal error:"
            );

            Console.WriteLine(
                ex.Message
            );
        }
        finally
        {
            await viiperManager.StopIfOwnedAsync();

            Console.WriteLine();
            Console.WriteLine(
                "[SCXI] SCXI stopped."
            );
        }
    }


    private static Xbox360Input
        CreateNeutralXboxState()
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
}
