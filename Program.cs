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

        Console.WriteLine("Connecting to VIIPER...");

        var client = new ViiperClient(
            "localhost",
            3242
        );


        // =============================================================
        // FIND OR CREATE VIIPER BUS
        // =============================================================

        var buses = await client.BusListAsync();

        uint busId;

        if (buses.Buses.Length == 0)
        {
            var bus = await client.BusCreateAsync(null);

            busId = bus.BusID;

            Console.WriteLine(
                $"Created VIIPER bus {busId}");
        }
        else
        {
            busId = buses.Buses[0];

            Console.WriteLine(
                $"Using VIIPER bus {busId}");
        }


        // =============================================================
        // CREATE VIRTUAL XBOX 360 CONTROLLER
        // =============================================================

        var request = new DeviceCreateRequest
        {
            Type = "xbox360"
        };

        var response = await client.BusDeviceAddAsync(
            busId,
            request
        );

        Console.WriteLine(
            $"Created virtual Xbox controller: " +
            $"{response.BusID}-{response.DevID}"
        );


        // =============================================================
        // CONNECT TO VIRTUAL CONTROLLER
        // =============================================================

        await using var xbox =
            await client.ConnectDeviceAsync(
                busId,
                response.DevID
            );

        Console.WriteLine(
            "Virtual Xbox controller connected."
        );

        Console.WriteLine();


        // =============================================================
        // START RAW INPUT LISTENER
        // =============================================================

        using var listener =
            new SteamRawInputListener(xbox);

        Console.WriteLine(
            "Waiting for Steam Controller..."
        );

        Console.WriteLine();

        Console.WriteLine(
            "Press Ctrl+C to stop SCXI."
        );

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
                new ApplicationContext()
            );
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine(
                "Shutting down SCXI..."
            );


            // Release every virtual input.
            try
            {
                await xbox.SendAsync(
                    CreateNeutralXboxState()
                );
            }
            catch
            {
            }


            // Remove virtual Xbox controller.
            try
            {
                await client.BusDeviceRemoveAsync(
                    busId,
                    response.DevID
                );
            }
            catch
            {
            }


            Console.WriteLine(
                "Virtual controller removed."
            );

            Console.WriteLine(
                "SCXI stopped."
            );
        }
    }


    private static Xbox360Input CreateNeutralXboxState()
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


    // No valid state packet for 1 second =
    // controller considered disconnected.
    private const long DisconnectTimeoutMs =
        1000;


    private readonly ViiperDevice _xbox;


    // =================================================================
    // CONNECTION STATE
    // =================================================================

    private long _lastPacketTick = 0;

    // 0 = disconnected
    // 1 = connected
    private int _controllerConnected = 0;

    private readonly System.Threading.Timer
        _disconnectTimer;


    // =================================================================
    // LAST INPUT STATE
    // =================================================================

    private readonly object _stateLock =
        new();

    private SteamGamepadState _lastState;

    private bool _hasLastState =
        false;


    // =================================================================
    // OUTPUT QUEUE
    //
    // Keep only the newest controller state.
    // This prevents input latency from building up.
    // =================================================================

    private readonly Channel<Xbox360Input>
        _stateChannel =
            Channel.CreateBounded<Xbox360Input>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,

                    FullMode =
                        BoundedChannelFullMode.DropOldest
                }
            );


    private readonly Task _senderTask;


    // =================================================================
    // INTERNAL CONTROLLER STATE
    // =================================================================

    private readonly record struct SteamGamepadState(
        uint Buttons,

        byte Lt,
        byte Rt,

        short Lx,
        short Ly,

        short Rx,
        short Ry
    );


    // =================================================================
    // WINDOWS RAW INPUT STRUCTURES
    // =================================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;

        public ushort usUsage;

        public uint dwFlags;

        public IntPtr hwndTarget;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;

        public uint dwSize;

        public IntPtr hDevice;

        public IntPtr wParam;
    }


    // =================================================================
    // WINDOWS API
    // =================================================================

    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    private static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize
    );


    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader
    );


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize
    );


    // =================================================================
    // CONSTRUCTOR
    // =================================================================

    public SteamRawInputListener(
        ViiperDevice xbox
    )
    {
        _xbox = xbox;


        // Hidden window that receives WM_INPUT.
        CreateHandle(
            new CreateParams
            {
                Caption = "SCXI_RawInput"
            }
        );


        // Listen for Valve's vendor-defined HID collection.
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
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>()
            ))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error()
            );
        }


        // Start virtual Xbox sender.
        _senderTask =
            Task.Run(SenderLoopAsync);


        // Check for controller disappearance
        // four times per second.
        _disconnectTimer =
            new System.Threading.Timer(
                CheckForDisconnect,
                null,
                250,
                250
            );
    }


    // =================================================================
    // WINDOWS MESSAGE HANDLER
    // =================================================================

    protected override void WndProc(
        ref Message m
    )
    {
        if (m.Msg == WM_INPUT)
        {
            ProcessRawInput(
                m.LParam
            );
        }

        base.WndProc(
            ref m
        );
    }


    // =================================================================
    // PROCESS WINDOWS RAW INPUT
    // =================================================================

    private void ProcessRawInput(
        IntPtr rawInputHandle
    )
    {
        uint headerSize =
            (uint)Marshal.SizeOf<RAWINPUTHEADER>();

        uint totalSize = 0;


        uint result =
            GetRawInputData(
                rawInputHandle,
                RID_INPUT,
                IntPtr.Zero,
                ref totalSize,
                headerSize
            );


        if (result == uint.MaxValue ||
            totalSize == 0)
        {
            return;
        }


        IntPtr buffer =
            Marshal.AllocHGlobal(
                (int)totalSize
            );


        try
        {
            result =
                GetRawInputData(
                    rawInputHandle,
                    RID_INPUT,
                    buffer,
                    ref totalSize,
                    headerSize
                );


            if (result == uint.MaxValue)
            {
                return;
            }


            var header =
                Marshal.PtrToStructure<RAWINPUTHEADER>(
                    buffer
                );


            if (header.dwType !=
                RIM_TYPEHID)
            {
                return;
            }


            string path =
                GetDeviceName(
                    header.hDevice
                );


            // Only process the 2026 Steam Controller puck.
            if (!path.Contains(
                    "VID_28DE&PID_1304",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return;
            }


            int hidOffset =
                Marshal.SizeOf<RAWINPUTHEADER>();


            if (totalSize <
                hidOffset + 8)
            {
                return;
            }


            uint reportSize =
                (uint)Marshal.ReadInt32(
                    buffer,
                    hidOffset
                );


            uint reportCount =
                (uint)Marshal.ReadInt32(
                    buffer,
                    hidOffset + 4
                );


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
                i++
            )
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
                    new byte[reportSize];


                Marshal.Copy(
                    IntPtr.Add(
                        buffer,
                        offset
                    ),
                    report,
                    0,
                    (int)reportSize
                );


                HandleSteamReport(
                    report
                );
            }
        }
        finally
        {
            Marshal.FreeHGlobal(
                buffer
            );
        }
    }


    // =================================================================
    // STEAM CONTROLLER REPORT -> XBOX STATE
    // =================================================================

    private void HandleSteamReport(
        byte[] report
    )
    {
        if (report.Length < 18)
        {
            return;
        }


        // Valid controller state reports.
        if (report[0] != 0x42 &&
            report[0] != 0x45)
        {
            return;
        }


        MarkControllerAlive();


        // =============================================================
        // BUTTONS
        // =============================================================

        uint buttons =
            0;


        byte b0 =
            report[2];

        byte b1 =
            report[3];

        byte b2 =
            report[4];


        // -------------------------------------------------------------
        // A / B / X / Y
        // -------------------------------------------------------------

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


        // -------------------------------------------------------------
        // RIGHT STICK CLICK
        // -------------------------------------------------------------

        if ((b0 & 0x20) != 0)
        {
            buttons |=
                (uint)XboxButton.RThumb;
        }


        // -------------------------------------------------------------
        // MENU -> START
        // -------------------------------------------------------------

        if ((b0 & 0x40) != 0)
        {
            buttons |=
                (uint)XboxButton.Start;
        }


        // -------------------------------------------------------------
        // RIGHT BUMPER
        // -------------------------------------------------------------

        if ((b1 & 0x02) != 0)
        {
            buttons |=
                (uint)XboxButton.RShoulder;
        }


        // -------------------------------------------------------------
        // D-PAD
        // -------------------------------------------------------------

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


        // -------------------------------------------------------------
        // VIEW -> BACK
        // -------------------------------------------------------------

        if ((b1 & 0x40) != 0)
        {
            buttons |=
                (uint)XboxButton.Back;
        }


        // -------------------------------------------------------------
        // LEFT STICK CLICK
        // -------------------------------------------------------------

        if ((b1 & 0x80) != 0)
        {
            buttons |=
                (uint)XboxButton.LThumb;
        }


        // -------------------------------------------------------------
        // STEAM -> GUIDE
        // -------------------------------------------------------------

        if ((b2 & 0x01) != 0)
        {
            buttons |=
                (uint)XboxButton.Guide;
        }


        // -------------------------------------------------------------
        // LEFT BUMPER
        // -------------------------------------------------------------

        if ((b2 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.LShoulder;
        }


        // =============================================================
        // TRIGGERS
        // =============================================================

        short rawLt =
            BitConverter.ToInt16(
                report,
                6
            );


        short rawRt =
            BitConverter.ToInt16(
                report,
                8
            );


        byte lt =
            ScaleTrigger(
                rawLt
            );


        byte rt =
            ScaleTrigger(
                rawRt
            );


        // =============================================================
        // STICKS
        // =============================================================

        short lx =
            BitConverter.ToInt16(
                report,
                10
            );


        short ly =
            BitConverter.ToInt16(
                report,
                12
            );


        short rx =
            BitConverter.ToInt16(
                report,
                14
            );


        short ry =
            BitConverter.ToInt16(
                report,
                16
            );


        // =============================================================
        // CREATE CURRENT STATE
        // =============================================================

        var state =
            new SteamGamepadState(
                buttons,
                lt,
                rt,
                lx,
                ly,
                rx,
                ry
            );


        bool shouldSend;


        lock (_stateLock)
        {
            shouldSend =
                !_hasLastState ||
                _lastState != state;


            if (shouldSend)
            {
                _lastState =
                    state;

                _hasLastState =
                    true;
            }
        }


        if (!shouldSend)
        {
            return;
        }


        _stateChannel.Writer.TryWrite(
            CreateXboxState(
                state
            )
        );
    }


    // =================================================================
    // CONNECTION TRACKING
    // =================================================================

    private void MarkControllerAlive()
    {
        System.Threading.Volatile.Write(
            ref _lastPacketTick,
            Environment.TickCount64
        );


        // First packet after being disconnected.
        if (System.Threading.Interlocked.Exchange(
                ref _controllerConnected,
                1
            ) == 0)
        {
            lock (_stateLock)
            {
                _hasLastState =
                    false;
            }


            Console.WriteLine(
                "[SCXI] Steam Controller connected."
            );
        }
    }


    private void CheckForDisconnect(
        object? state
    )
    {
        if (System.Threading.Volatile.Read(
                ref _controllerConnected
            ) == 0)
        {
            return;
        }


        long lastPacket =
            System.Threading.Volatile.Read(
                ref _lastPacketTick
            );


        if (lastPacket == 0)
        {
            return;
        }


        long elapsed =
            Environment.TickCount64 -
            lastPacket;


        if (elapsed <
            DisconnectTimeoutMs)
        {
            return;
        }


        // Make sure the disconnect handler
        // only runs once.
        if (System.Threading.Interlocked.Exchange(
                ref _controllerConnected,
                0
            ) != 1)
        {
            return;
        }


        lock (_stateLock)
        {
            _hasLastState =
                false;
        }


        // Immediately reset every virtual input.
        _stateChannel.Writer.TryWrite(
            CreateNeutralXboxState()
        );


        Console.WriteLine(
            "[SCXI] Steam Controller disconnected."
        );


        Console.WriteLine(
            "[SCXI] Virtual Xbox state reset."
        );
    }


    // =================================================================
    // TRIGGER CONVERSION
    // =================================================================

    private static byte ScaleTrigger(
        short raw
    )
    {
        int value =
            Math.Clamp(
                (int)raw,
                0,
                32767
            );


        return (byte)Math.Clamp(
            (value * 255) / 32767,
            0,
            255
        );
    }


    // =================================================================
    // XBOX STATE CREATION
    // =================================================================

    private static Xbox360Input CreateXboxState(
        SteamGamepadState state
    )
    {
        return new Xbox360Input
        {
            Buttons =
                state.Buttons,

            Lt =
                state.Lt,

            Rt =
                state.Rt,

            Lx =
                state.Lx,

            Ly =
                state.Ly,

            Rx =
                state.Rx,

            Ry =
                state.Ry
        };
    }


    private static Xbox360Input CreateNeutralXboxState()
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
    // VIIPER OUTPUT LOOP
    // =================================================================

    private async Task SenderLoopAsync()
    {
        await foreach (
            Xbox360Input state in
            _stateChannel.Reader.ReadAllAsync()
        )
        {
            try
            {
                await _xbox.SendAsync(
                    state
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SCXI] Xbox output error: " +
                    $"{ex.Message}"
                );
            }
        }
    }


    // =================================================================
    // GET RAW INPUT DEVICE PATH
    // =================================================================

    private static string GetDeviceName(
        IntPtr device
    )
    {
        uint size =
            0;


        GetRawInputDeviceInfo(
            device,
            RIDI_DEVICENAME,
            IntPtr.Zero,
            ref size
        );


        if (size == 0)
        {
            return "";
        }


        IntPtr buffer =
            Marshal.AllocHGlobal(
                (int)((size + 1) * 2)
            );


        try
        {
            uint result =
                GetRawInputDeviceInfo(
                    device,
                    RIDI_DEVICENAME,
                    buffer,
                    ref size
                );


            if (result == uint.MaxValue)
            {
                return "";
            }


            return
                Marshal.PtrToStringUni(
                    buffer
                )
                ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(
                buffer
            );
        }
    }


    // =================================================================
    // CLEANUP
    // =================================================================

    public void Dispose()
    {
        _disconnectTimer.Dispose();


        _stateChannel.Writer.TryComplete();


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
