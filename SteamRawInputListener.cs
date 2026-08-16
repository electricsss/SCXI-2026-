using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Forms;

using Viiper.Client;
using Viiper.Client.Devices.Xbox360;

using XboxButton = Viiper.Client.Devices.Xbox360.Button;


// =====================================================================
// STEAM CONTROLLER RAW INPUT LISTENER
//
// Reads the physical Steam Controller through Windows Raw Input,
// converts its state to Xbox 360 input, and sends that state to VIIPER.
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


    // If no valid controller packet arrives for one second,
    // consider the Steam Controller disconnected.
    private const long DisconnectTimeoutMs =
        1000;


    private readonly ViiperDevice _xbox;


    // =================================================================
    // CONNECTION STATE
    // =================================================================

    private long _lastPacketTick =
        0;


    // 0 = disconnected
    // 1 = connected
    private int _controllerConnected =
        0;


    private readonly System.Threading.Timer
        _disconnectTimer;


    // =================================================================
    // LAST CONTROLLER STATE
    // =================================================================

    private readonly object
        _stateLock =
            new();


    private SteamGamepadState
        _lastState;


    private bool _hasLastState =
        false;


    // =================================================================
    // OUTPUT QUEUE
    //
    // Keep only the newest controller state.
    // This prevents input latency from building up if output ever
    // briefly falls behind input.
    // =================================================================

    private readonly Channel<Xbox360Input>
        _stateChannel =
            Channel.CreateBounded<Xbox360Input>(
                new BoundedChannelOptions(1)
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    FullMode =
                        BoundedChannelFullMode
                            .DropOldest
                }
            );


    private readonly Task
        _senderTask;


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
    private static extern bool
        RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices,
            uint cbSize
        );


    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    private static extern uint
        GetRawInputData(
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
    private static extern uint
        GetRawInputDeviceInfo(
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
        _xbox =
            xbox;


        // Hidden Windows window that receives WM_INPUT messages.
        CreateHandle(
            new CreateParams
            {
                Caption =
                    "SCXI_RawInput"
            }
        );


        // Listen for Valve's vendor-defined FF00 / 0001 HID collection.
        var devices =
            new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage =
                        0xFF00,

                    usUsage =
                        0x0001,

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
                    RAWINPUTDEVICE>()
            ))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error()
            );
        }


        // Start VIIPER sender.
        _senderTask =
            Task.Run(
                SenderLoopAsync
            );


        // Check controller connection state four times per second.
        _disconnectTimer =
            new System.Threading.Timer(
                CheckForDisconnect,
                null,
                250,
                250
            );
    }


    // =================================================================
    // WINDOWS MESSAGE LOOP
    // =================================================================

    protected override void WndProc(
        ref Message m
    )
    {
        if (m.Msg ==
            WM_INPUT)
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
            (uint)Marshal.SizeOf<
                RAWINPUTHEADER>();


        uint totalSize =
            0;


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


            if (result ==
                uint.MaxValue)
            {
                return;
            }


            var header =
                Marshal.PtrToStructure<
                    RAWINPUTHEADER>(
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


            // Only accept the 2026 Steam Controller wireless puck.
            if (!path.Contains(
                    "VID_28DE&PID_1304",
                    StringComparison.OrdinalIgnoreCase
                ))
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


                if (offset +
                    reportSize >
                    totalSize)
                {
                    break;
                }


                byte[] report =
                    new byte[
                        reportSize
                    ];


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
    // STEAM CONTROLLER -> XBOX STATE
    // =================================================================

    private void HandleSteamReport(
        byte[] report
    )
    {
        if (report.Length <
            18)
        {
            return;
        }


        // 0x42 = USB / wireless state report
        // 0x45 = alternate / BLE state report
        if (report[0] !=
                0x42 &&
            report[0] !=
                0x45)
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


        // A
        if ((b0 & 0x01) != 0)
        {
            buttons |=
                (uint)XboxButton.A;
        }


        // B
        if ((b0 & 0x02) != 0)
        {
            buttons |=
                (uint)XboxButton.B;
        }


        // X
        if ((b0 & 0x04) != 0)
        {
            buttons |=
                (uint)XboxButton.X;
        }


        // Y
        if ((b0 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.Y;
        }


        // Right stick click
        if ((b0 & 0x20) != 0)
        {
            buttons |=
                (uint)XboxButton.RThumb;
        }


        // Menu -> Start
        if ((b0 & 0x40) != 0)
        {
            buttons |=
                (uint)XboxButton.Start;
        }


        // Right bumper
        if ((b1 & 0x02) != 0)
        {
            buttons |=
                (uint)XboxButton.RShoulder;
        }


        // D-pad Down
        if ((b1 & 0x04) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadDown;
        }


        // D-pad Right
        if ((b1 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadRight;
        }


        // D-pad Left
        if ((b1 & 0x10) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadLeft;
        }


        // D-pad Up
        if ((b1 & 0x20) != 0)
        {
            buttons |=
                (uint)XboxButton.DPadUp;
        }


        // View -> Back
        if ((b1 & 0x40) != 0)
        {
            buttons |=
                (uint)XboxButton.Back;
        }


        // Left stick click
        if ((b1 & 0x80) != 0)
        {
            buttons |=
                (uint)XboxButton.LThumb;
        }


        // Steam -> Guide
        if ((b2 & 0x01) != 0)
        {
            buttons |=
                (uint)XboxButton.Guide;
        }


        // Left bumper
        if ((b2 & 0x08) != 0)
        {
            buttons |=
                (uint)XboxButton.LShoulder;
        }


        // =============================================================
        // ANALOG TRIGGERS
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
        // ANALOG STICKS
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
        // BUILD STATE
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
                _lastState !=
                    state;


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


        // First packet after disconnected state.
        if (
            System.Threading.Interlocked.Exchange(
                ref _controllerConnected,
                1
            ) == 0
        )
        {
            lock (_stateLock)
            {
                // Force first state after reconnect to be sent.
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
        if (
            System.Threading.Volatile.Read(
                ref _controllerConnected
            ) == 0
        )
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


        // Ensure disconnect processing only happens once.
        if (
            System.Threading.Interlocked.Exchange(
                ref _controllerConnected,
                0
            ) != 1
        )
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
            (value * 255) /
                32767,
            0,
            255
        );
    }


    // =================================================================
    // XBOX STATE CREATION
    // =================================================================

    private static Xbox360Input
        CreateXboxState(
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


    // =================================================================
    // VIIPER OUTPUT LOOP
    // =================================================================

    private async Task
        SenderLoopAsync()
    {
        await foreach (
            Xbox360Input state in
            _stateChannel
                .Reader
                .ReadAllAsync()
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
    // RAW INPUT DEVICE PATH
    // =================================================================

    private static string
        GetDeviceName(
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


            if (result ==
                uint.MaxValue)
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
