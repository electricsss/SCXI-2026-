using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Forms;

using Viiper.Client;
using Viiper.Client.Types;
using Viiper.Client.Devices.Xbox360;

using XboxButton = Viiper.Client.Devices.Xbox360.Button;


// =====================================================================
// PROGRAM
// =====================================================================

internal static class Program
{
    [STAThread]
    static async Task Main()
    {
        Console.WriteLine("SteamInputBridge");
        Console.WriteLine("================");
        Console.WriteLine();

        Console.WriteLine("Connecting to VIIPER...");

        var client =
            new ViiperClient("localhost", 3242);


        // =============================================================
        // FIND OR CREATE VIIPER BUS
        // =============================================================

        var buses =
            await client.BusListAsync();

        uint busId;

        if (buses.Buses.Length == 0)
        {
            var bus =
                await client.BusCreateAsync(null);

            busId = bus.BusID;

            Console.WriteLine(
                $"Created VIIPER bus {busId}");
        }
        else
        {
            busId =
                buses.Buses[0];

            Console.WriteLine(
                $"Using VIIPER bus {busId}");
        }


        // =============================================================
        // CREATE VIRTUAL XBOX 360 CONTROLLER
        // =============================================================

        var request =
            new DeviceCreateRequest
            {
                Type = "xbox360"
            };

        var response =
            await client.BusDeviceAddAsync(
                busId,
                request);

        Console.WriteLine(
            $"Created virtual Xbox controller: " +
            $"{response.BusID}-{response.DevID}");


        // =============================================================
        // CONNECT TO VIRTUAL CONTROLLER
        // =============================================================

        await using var xbox =
            await client.ConnectDeviceAsync(
                busId,
                response.DevID);

        Console.WriteLine(
            "Connected to virtual Xbox controller.");

        Console.WriteLine();


        // =============================================================
        // START STEAM CONTROLLER RAW INPUT LISTENER
        // =============================================================

        using var listener =
            new SteamRawInputListener(xbox);

        Console.WriteLine(
            "Steam Controller listener active.");

        Console.WriteLine();
        Console.WriteLine(
            "Open joy.cpl to test the controller.");

        Console.WriteLine();
        Console.WriteLine(
            "Press Ctrl+C to stop SteamInputBridge.");

        Console.WriteLine();


        // =============================================================
        // CTRL+C HANDLER
        // =============================================================

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;

            Application.Exit();
        };


        // =============================================================
        // WINDOWS MESSAGE LOOP
        // =============================================================

        try
        {
            Application.Run(
                new ApplicationContext());
        }

        finally
        {
            Console.WriteLine();
            Console.WriteLine(
                "Shutting down...");


            // Release all Xbox inputs
            try
            {
                await xbox.SendAsync(
                    new Xbox360Input
                    {
                        Buttons = 0,

                        Lt = 0,
                        Rt = 0,

                        Lx = 0,
                        Ly = 0,

                        Rx = 0,
                        Ry = 0
                    });
            }
            catch
            {
            }


            // Remove virtual controller
            try
            {
                await client.BusDeviceRemoveAsync(
                    busId,
                    response.DevID);
            }
            catch
            {
            }

            Console.WriteLine(
                "Virtual controller removed.");
        }
    }
}


// =====================================================================
// STEAM CONTROLLER RAW INPUT LISTENER
// =====================================================================

internal sealed class SteamRawInputListener :
    NativeWindow,
    IDisposable
{
    private const int WM_INPUT =
        0x00FF;

    private const uint RID_INPUT =
        0x10000003;

    private const uint RIDI_DEVICENAME =
        0x20000007;

    private const uint RIM_TYPEHID =
        2;

    private const uint RIDEV_INPUTSINK =
        0x00000100;


    private readonly ViiperDevice _xbox;

    private SteamGamepadState? _lastState;


    // Keep only the newest controller state.
    //
    // If Steam Controller packets arrive faster
    // than VIIPER can process them, older states
    // are discarded instead of creating latency.

    private readonly Channel<Xbox360Input>
        _stateChannel =
            Channel.CreateBounded<Xbox360Input>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = true,

                    FullMode =
                        BoundedChannelFullMode
                            .DropOldest
                });


    private readonly Task _senderTask;


// =====================================================================
// INTERNAL GAMEPAD STATE
// =====================================================================

    private readonly record struct SteamGamepadState(
        uint Buttons,

        byte Lt,
        byte Rt,

        short Lx,
        short Ly,

        short Rx,
        short Ry);


// =====================================================================
// WINDOWS RAW INPUT STRUCTURES
// =====================================================================

    [StructLayout(
        LayoutKind.Sequential)]

    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;

        public ushort usUsage;

        public uint dwFlags;

        public IntPtr hwndTarget;
    }


    [StructLayout(
        LayoutKind.Sequential)]

    private struct RAWINPUTHEADER
    {
        public uint dwType;

        public uint dwSize;

        public IntPtr hDevice;

        public IntPtr wParam;
    }


// =====================================================================
// WINDOWS API
// =====================================================================

    [DllImport(
        "user32.dll",
        SetLastError = true)]

    private static extern bool
        RegisterRawInputDevices(
            RAWINPUTDEVICE[]
                pRawInputDevices,

            uint uiNumDevices,

            uint cbSize);


    [DllImport(
        "user32.dll",
        SetLastError = true)]

    private static extern uint
        GetRawInputData(
            IntPtr hRawInput,

            uint uiCommand,

            IntPtr pData,

            ref uint pcbSize,

            uint cbSizeHeader);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]

    private static extern uint
        GetRawInputDeviceInfo(
            IntPtr hDevice,

            uint uiCommand,

            IntPtr pData,

            ref uint pcbSize);


// =====================================================================
// CONSTRUCTOR
// =====================================================================

    public SteamRawInputListener(
        ViiperDevice xbox)
    {
        _xbox = xbox;


        CreateHandle(
            new CreateParams
            {
                Caption =
                    "SteamInputBridgeRawInput"
            });


        // Listen for Valve vendor-defined
        // controller HID reports.

        var devices =
            new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = 0xFF00,

                    usUsage = 0x0001,

                    dwFlags =
                        RIDEV_INPUTSINK,

                    hwndTarget =
                        Handle
                }
            };


        if (!RegisterRawInputDevices(
                devices,

                (uint)devices.Length,

                (uint)Marshal.SizeOf<
                    RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }


        _senderTask =
            Task.Run(SenderLoopAsync);
    }


// =====================================================================
// WINDOWS MESSAGE HANDLER
// =====================================================================

    protected override void WndProc(
        ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            ProcessRawInput(
                m.LParam);
        }


        base.WndProc(
            ref m);
    }


// =====================================================================
// PROCESS RAW INPUT
// =====================================================================

    private void ProcessRawInput(
        IntPtr rawInputHandle)
    {
        uint headerSize =
            (uint)Marshal.SizeOf<
                RAWINPUTHEADER>();


        uint totalSize = 0;


        uint result =
            GetRawInputData(
                rawInputHandle,

                RID_INPUT,

                IntPtr.Zero,

                ref totalSize,

                headerSize);


        if (result == uint.MaxValue ||
            totalSize == 0)
        {
            return;
        }


        IntPtr buffer =
            Marshal.AllocHGlobal(
                (int)totalSize);


        try
        {
            result =
                GetRawInputData(
                    rawInputHandle,

                    RID_INPUT,

                    buffer,

                    ref totalSize,

                    headerSize);


            if (result == uint.MaxValue)
            {
                return;
            }


            var header =
                Marshal.PtrToStructure<
                    RAWINPUTHEADER>(
                        buffer);


            if (header.dwType !=
                RIM_TYPEHID)
            {
                return;
            }


            string path =
                GetDeviceName(
                    header.hDevice);


            // Ignore everything except
            // the Steam Controller wireless puck.

            if (!path.Contains(
                    "VID_28DE&PID_1304",

                    StringComparison
                        .OrdinalIgnoreCase))
            {
                return;
            }


            int hidOffset =
                Marshal.SizeOf<
                    RAWINPUTHEADER>();


            if (totalSize <
                hidOffset + 8)
            {
                return;
            }


            uint reportSize =
                (uint)Marshal.ReadInt32(
                    buffer,

                    hidOffset);


            uint reportCount =
                (uint)Marshal.ReadInt32(
                    buffer,

                    hidOffset + 4);


            int dataOffset =
                hidOffset + 8;


            if (reportSize == 0 ||
                reportCount == 0)
            {
                return;
            }


            for (
                uint i = 0;

                i < reportCount;

                i++)
            {
                int offset =
                    dataOffset +
                    (int)(i * reportSize);


                if (offset + reportSize >
                    totalSize)
                {
                    break;
                }


                byte[] report =
                    new byte[
                        reportSize];


                Marshal.Copy(
                    IntPtr.Add(
                        buffer,
                        offset),

                    report,

                    0,

                    (int)reportSize);


                HandleSteamReport(
                    report);
            }
        }

        finally
        {
            Marshal.FreeHGlobal(
                buffer);
        }
    }


// =====================================================================
// CONVERT STEAM CONTROLLER REPORT -> XBOX 360 STATE
// =====================================================================

    private void HandleSteamReport(
        byte[] report)
    {
        if (report.Length < 18)
        {
            return;
        }


        // 0x42 = USB / wireless puck
        // 0x45 = alternate / BLE state

        if (report[0] != 0x42 &&
            report[0] != 0x45)
        {
            return;
        }


// =====================================================================
// BUTTONS
// =====================================================================

        uint buttons = 0;


        byte b0 =
            report[2];

        byte b1 =
            report[3];

        byte b2 =
            report[4];


// ---------------------------------------------------------------------
// A / B / X / Y
// ---------------------------------------------------------------------

        if ((b0 & 0x01) != 0)
        {
            buttons |=
                (uint)XboxButton.A;
        }


        if ((b0 & 0x02) != 0)
        {
            buttons |=
                (uint)XboxButton.B;
        }


        if ((b0 & 0x04) != 0)
        {
            buttons |=
                (uint)XboxButton.X;
        }


        if ((b0 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.Y;
        }


// ---------------------------------------------------------------------
// RIGHT STICK CLICK
// ---------------------------------------------------------------------

        if ((b0 & 0x20) != 0)
        {
            buttons |=
                (uint)XboxButton.RThumb;
        }


// ---------------------------------------------------------------------
// MENU -> START
// ---------------------------------------------------------------------

        if ((b0 & 0x40) != 0)
        {
            buttons |=
                (uint)XboxButton.Start;
        }


// ---------------------------------------------------------------------
// RIGHT BUMPER
// ---------------------------------------------------------------------

        if ((b1 & 0x02) != 0)
        {
            buttons |=
                (uint)XboxButton.RShoulder;
        }


// ---------------------------------------------------------------------
// D-PAD
// ---------------------------------------------------------------------

        if ((b1 & 0x04) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadDown;
        }


        if ((b1 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadRight;
        }


        if ((b1 & 0x10) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadLeft;
        }


        if ((b1 & 0x20) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadUp;
        }


// ---------------------------------------------------------------------
// VIEW -> BACK
// ---------------------------------------------------------------------

        if ((b1 & 0x40) != 0)
        {
            buttons |=
                (uint)XboxButton.Back;
        }


// ---------------------------------------------------------------------
// LEFT STICK CLICK
// ---------------------------------------------------------------------

        if ((b1 & 0x80) != 0)
        {
            buttons |=
                (uint)XboxButton.LThumb;
        }


// ---------------------------------------------------------------------
// STEAM -> XBOX GUIDE
// ---------------------------------------------------------------------

        if ((b2 & 0x01) != 0)
        {
            buttons |=
                (uint)XboxButton.Guide;
        }


// ---------------------------------------------------------------------
// LEFT BUMPER
// ---------------------------------------------------------------------

        if ((b2 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.LShoulder;
        }


// =====================================================================
// ANALOG TRIGGERS
// =====================================================================

        short rawLt =
            BitConverter.ToInt16(
                report,
                6);


        short rawRt =
            BitConverter.ToInt16(
                report,
                8);


        byte lt =
            ScaleTrigger(
                rawLt);


        byte rt =
            ScaleTrigger(
                rawRt);


// =====================================================================
// ANALOG STICKS
// =====================================================================

        short lx =
            BitConverter.ToInt16(
                report,
                10);


        short ly =
            BitConverter.ToInt16(
                report,
                12);


        short rx =
            BitConverter.ToInt16(
                report,
                14);


        short ry =
            BitConverter.ToInt16(
                report,
                16);


// =====================================================================
// BUILD CONTROLLER STATE
// =====================================================================

        var state =
            new SteamGamepadState(
                buttons,

                lt,
                rt,

                lx,
                ly,

                rx,
                ry);


// ---------------------------------------------------------------------
// Ignore identical reports.
// ---------------------------------------------------------------------

        if (_lastState.HasValue &&
            _lastState.Value == state)
        {
            return;
        }


        _lastState =
            state;


// =====================================================================
// SEND TO VIIPER QUEUE
// =====================================================================

        _stateChannel.Writer.TryWrite(
            new Xbox360Input
            {
                Buttons =
                    buttons,

                Lt =
                    lt,

                Rt =
                    rt,

                Lx =
                    lx,

                Ly =
                    ly,

                Rx =
                    rx,

                Ry =
                    ry
            });
    }


// =====================================================================
// TRIGGER CONVERSION
// =====================================================================

    private static byte ScaleTrigger(
        short raw)
    {
        // Steam Controller:
        //
        // 0     = released
        // 32767 = fully pressed
        //
        // Xbox 360:
        //
        // 0   = released
        // 255 = fully pressed


        int value =
            Math.Clamp(
                (int)raw,

                0,

                32767);


        return (byte)Math.Clamp(
            (value * 255) / 32767,

            0,

            255);
    }


// =====================================================================
// VIIPER SENDER LOOP
// =====================================================================

    private async Task SenderLoopAsync()
    {
        await foreach (
            Xbox360Input state in
            _stateChannel
                .Reader
                .ReadAllAsync())
        {
            try
            {
                await _xbox.SendAsync(
                    state);
            }

            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Xbox send error: " +
                    $"{ex.Message}");
            }
        }
    }


// =====================================================================
// GET WINDOWS DEVICE PATH
// =====================================================================

    private static string GetDeviceName(
        IntPtr device)
    {
        uint size = 0;


        GetRawInputDeviceInfo(
            device,

            RIDI_DEVICENAME,

            IntPtr.Zero,

            ref size);


        if (size == 0)
        {
            return "";
        }


        IntPtr buffer =
            Marshal.AllocHGlobal(
                (int)((size + 1) * 2));


        try
        {
            uint result =
                GetRawInputDeviceInfo(
                    device,

                    RIDI_DEVICENAME,

                    buffer,

                    ref size);


            if (result == uint.MaxValue)
            {
                return "";
            }


            return
                Marshal.PtrToStringUni(
                    buffer)

                ?? "";
        }

        finally
        {
            Marshal.FreeHGlobal(
                buffer);
        }
    }


// =====================================================================
// CLEANUP
// =====================================================================

    public void Dispose()
    {
        _stateChannel
            .Writer
            .TryComplete();


        try
        {
            _senderTask
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
        }


        DestroyHandle();
    }
}
