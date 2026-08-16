using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;


// =====================================================================
// STEAM CONTROLLER HAPTICS
//
// Handles physical rumble output for the 2026 Steam Controller.
//
// Xbox rumble:
//     0..255
//
// Steam Controller rumble:
//     0..65535
//
// Active rumble is refreshed every 40 ms so the controller's
// hardware safety timeout does not stop sustained vibration.
// =====================================================================

internal sealed class SteamControllerHaptics :
    IDisposable
{
    // =================================================================
    // WINDOWS FILE ACCESS
    // =================================================================

    private const uint GENERIC_READ =
        0x80000000;


    private const uint GENERIC_WRITE =
        0x40000000;


    private const uint FILE_SHARE_READ =
        0x00000001;


    private const uint FILE_SHARE_WRITE =
        0x00000002;


    private const uint OPEN_EXISTING =
        3;


    // =================================================================
    // HID
    // =================================================================

    private const int HIDP_STATUS_SUCCESS =
        0x00110000;


    // =================================================================
    // STEAM CONTROLLER RUMBLE
    // =================================================================

    private const byte RUMBLE_REPORT_ID =
        0x80;


    // Logical Steam Controller rumble command.
    private const int RUMBLE_COMMAND_LENGTH =
        10;


    // Keep sustained vibration alive.
    private const int RUMBLE_REFRESH_MS =
        40;


    // =================================================================
    // DEVICE
    // =================================================================

    private SafeFileHandle?
        _deviceHandle;


    private int _outputReportLength =
        RUMBLE_COMMAND_LENGTH;


    private readonly object
        _writeLock =
            new();


    // =================================================================
    // CURRENT RUMBLE STATE
    //
    // Stored as int so Interlocked/Volatile operations can be used.
    // Values are Steam-style 0..65535.
    // =================================================================

    private int _leftStrength =
        0;


    private int _rightStrength =
        0;


    private readonly System.Threading.Timer
        _rumbleTimer;


    private bool _disposed =
        false;


    public bool IsOpen =>
        _deviceHandle is not null &&
        !_deviceHandle.IsInvalid &&
        !_deviceHandle.IsClosed;


    // =================================================================
    // CONSTRUCTOR
    // =================================================================

    public SteamControllerHaptics()
    {
        _rumbleTimer =
            new System.Threading.Timer(
                RefreshRumble,
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );
    }


    // =================================================================
    // HIDP_CAPS
    // =================================================================

    [StructLayout(
        LayoutKind.Sequential
    )]
    private struct HIDP_CAPS
    {
        public ushort Usage;

        public ushort UsagePage;


        public ushort InputReportByteLength;

        public ushort OutputReportByteLength;

        public ushort FeatureReportByteLength;


        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 17
        )]
        public ushort[] Reserved;


        public ushort NumberLinkCollectionNodes;


        public ushort NumberInputButtonCaps;

        public ushort NumberInputValueCaps;

        public ushort NumberInputDataIndices;


        public ushort NumberOutputButtonCaps;

        public ushort NumberOutputValueCaps;

        public ushort NumberOutputDataIndices;


        public ushort NumberFeatureButtonCaps;

        public ushort NumberFeatureValueCaps;

        public ushort NumberFeatureDataIndices;
    }


    // =================================================================
    // WINDOWS API
    // =================================================================

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );


    [DllImport(
        "kernel32.dll",
        SetLastError = true
    )]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped
    );


    [DllImport(
        "hid.dll",
        SetLastError = true
    )]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle hObject,
        out IntPtr preparsedData
    );


    [DllImport(
        "hid.dll",
        SetLastError = true
    )]
    private static extern bool HidD_FreePreparsedData(
        IntPtr preparsedData
    );


    [DllImport(
        "hid.dll"
    )]
    private static extern int HidP_GetCaps(
        IntPtr preparsedData,
        out HIDP_CAPS capabilities
    );


    // =================================================================
    // OPEN
    // =================================================================

    public bool Open(
        string devicePath
    )
    {
        if (_disposed)
        {
            return false;
        }


        Close();


        Console.WriteLine(
            "[SCXI] Opening Steam Controller haptics..."
        );


        SafeFileHandle handle =
            CreateFileW(
                devicePath,

                GENERIC_READ |
                GENERIC_WRITE,

                FILE_SHARE_READ |
                FILE_SHARE_WRITE,

                IntPtr.Zero,

                OPEN_EXISTING,

                0,

                IntPtr.Zero
            );


        if (handle.IsInvalid)
        {
            int error =
                Marshal.GetLastWin32Error();


            handle.Dispose();


            Console.WriteLine(
                "[SCXI] Could not open Steam Controller " +
                $"for haptics. Win32 error {error}: " +
                $"{new Win32Exception(error).Message}"
            );


            return false;
        }


        _deviceHandle =
            handle;


        try
        {
            _outputReportLength =
                GetOutputReportLength(
                    handle
                );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Could not determine HID output " +
                "report length: " +
                ex.Message
            );


            Close();


            return false;
        }


        if (_outputReportLength <
            RUMBLE_COMMAND_LENGTH)
        {
            Console.WriteLine(
                "[SCXI] HID output report is too small."
            );


            Close();


            return false;
        }


        Console.WriteLine(
            "[SCXI] Steam Controller haptics ready."
        );


        Console.WriteLine(
            $"[SCXI] HID output report length: " +
            $"{_outputReportLength} bytes."
        );


        return true;
    }


    // =================================================================
    // GET WINDOWS HID OUTPUT REPORT LENGTH
    // =================================================================

    private static int GetOutputReportLength(
        SafeFileHandle handle
    )
    {
        IntPtr preparsedData =
            IntPtr.Zero;


        try
        {
            if (!HidD_GetPreparsedData(
                    handle,
                    out preparsedData
                ))
            {
                int error =
                    Marshal.GetLastWin32Error();


                throw new Win32Exception(
                    error,
                    "Could not get HID preparsed data."
                );
            }


            int status =
                HidP_GetCaps(
                    preparsedData,
                    out HIDP_CAPS caps
                );


            if (status !=
                HIDP_STATUS_SUCCESS)
            {
                throw new InvalidOperationException(
                    "HidP_GetCaps failed with status " +
                    $"0x{status:X8}."
                );
            }


            return
                caps.OutputReportByteLength;
        }
        finally
        {
            if (preparsedData !=
                IntPtr.Zero)
            {
                HidD_FreePreparsedData(
                    preparsedData
                );
            }
        }
    }


    // =================================================================
    // XBOX RUMBLE INPUT
    //
    // Called with values received from VIIPER.
    //
    // left  = 0..255
    // right = 0..255
    // =================================================================

    public void SetXboxRumble(
        byte left,
        byte right
    )
    {
        if (_disposed ||
            !IsOpen)
        {
            return;
        }


        // 255 * 257 = 65535
        //
        // This maps the complete 8-bit Xbox range
        // directly into the full 16-bit Steam range.

        ushort steamLeft =
            (ushort)(
                left *
                257
            );


        ushort steamRight =
            (ushort)(
                right *
                257
            );


        SetRumble(
            steamLeft,
            steamRight
        );
    }


    // =================================================================
    // SET PHYSICAL RUMBLE
    // =================================================================

    public void SetRumble(
        ushort left,
        ushort right
    )
    {
        if (_disposed)
        {
            return;
        }


        System.Threading.Volatile.Write(
            ref _leftStrength,
            left
        );


        System.Threading.Volatile.Write(
            ref _rightStrength,
            right
        );


        // =============================================================
        // STOP
        // =============================================================

        if (left == 0 &&
            right == 0)
        {
            // No need for continued refreshes.
            _rumbleTimer.Change(
                Timeout.Infinite,
                Timeout.Infinite
            );


            // Send the stop immediately.
            WriteCurrentRumble();


            return;
        }


        // =============================================================
        // ACTIVE RUMBLE
        //
        // Send immediately for minimum latency.
        // =============================================================

        WriteCurrentRumble();


        // Then maintain it every 40 ms.
        _rumbleTimer.Change(
            RUMBLE_REFRESH_MS,
            RUMBLE_REFRESH_MS
        );
    }


    // =================================================================
    // TIMER KEEP-ALIVE
    // =================================================================

    private void RefreshRumble(
        object? state
    )
    {
        if (_disposed ||
            !IsOpen)
        {
            return;
        }


        int left =
            System.Threading.Volatile.Read(
                ref _leftStrength
            );


        int right =
            System.Threading.Volatile.Read(
                ref _rightStrength
            );


        // If rumble has stopped, there is
        // nothing to refresh.
        if (left == 0 &&
            right == 0)
        {
            return;
        }


        WriteCurrentRumble();
    }


    // =================================================================
    // WRITE CURRENT STATE
    // =================================================================

    private bool WriteCurrentRumble()
    {
        ushort left =
            (ushort)Math.Clamp(
                System.Threading.Volatile.Read(
                    ref _leftStrength
                ),
                0,
                65535
            );


        ushort right =
            (ushort)Math.Clamp(
                System.Threading.Volatile.Read(
                    ref _rightStrength
                ),
                0,
                65535
            );


        lock (_writeLock)
        {
            return WriteRumbleCore(
                left,
                right
            );
        }
    }


    // =================================================================
    // WRITE RUMBLE REPORT
    //
    // Caller must hold _writeLock.
    // =================================================================

    private bool WriteRumbleCore(
        ushort left,
        ushort right
    )
    {
        if (_deviceHandle is null ||
            _deviceHandle.IsInvalid ||
            _deviceHandle.IsClosed)
        {
            return false;
        }


        byte[] report =
            new byte[
                _outputReportLength
            ];


        BuildRumbleCommand(
            report,
            left,
            right
        );


        bool success =
            WriteFile(
                _deviceHandle,
                report,
                (uint)report.Length,
                out uint bytesWritten,
                IntPtr.Zero
            );


        if (!success)
        {
            int error =
                Marshal.GetLastWin32Error();


            Console.WriteLine(
                "[SCXI] Steam Controller rumble write " +
                $"failed. Win32 error {error}: " +
                $"{new Win32Exception(error).Message}"
            );


            return false;
        }


        if (bytesWritten !=
            report.Length)
        {
            Console.WriteLine(
                "[SCXI] Steam Controller rumble write " +
                $"was incomplete: {bytesWritten}/" +
                $"{report.Length} bytes."
            );


            return false;
        }


        return true;
    }


    // =================================================================
    // BUILD 2026 STEAM CONTROLLER RUMBLE COMMAND
    //
    // Bytes 0..9:
    //
    // 0      Report ID
    // 1      Type
    // 2-3    Intensity
    // 4-5    Left speed
    // 6      Left gain
    // 7-8    Right speed
    // 9      Right gain
    //
    // Remaining HID report bytes are zero padding.
    // =================================================================

    private static void BuildRumbleCommand(
        byte[] report,
        ushort left,
        ushort right
    )
    {
        // Report ID
        report[0] =
            RUMBLE_REPORT_ID;


        // Type
        report[1] =
            0;


        // Intensity
        report[2] =
            0;

        report[3] =
            0;


        // Left motor speed
        report[4] =
            (byte)(
                left &
                0xFF
            );


        report[5] =
            (byte)(
                left >>
                8
            );


        // Left gain
        report[6] =
            0;


        // Right motor speed
        report[7] =
            (byte)(
                right &
                0xFF
            );


        report[8] =
            (byte)(
                right >>
                8
            );


        // Right gain
        report[9] =
            0;
    }


    // =================================================================
    // STOP
    // =================================================================

    public void StopRumble()
    {
        if (_disposed)
        {
            return;
        }


        System.Threading.Volatile.Write(
            ref _leftStrength,
            0
        );


        System.Threading.Volatile.Write(
            ref _rightStrength,
            0
        );


        _rumbleTimer.Change(
            Timeout.Infinite,
            Timeout.Infinite
        );


        if (!IsOpen)
        {
            return;
        }


        lock (_writeLock)
        {
            WriteRumbleCore(
                0,
                0
            );
        }
    }


    // =================================================================
    // CONTROLLED TEST BUZZ
    //
    // Temporarily retained so the current SCXIService.cs
    // continues to compile during our file-by-file upgrade.
    // =================================================================

    public async Task<bool> TestBuzzAsync(
        string devicePath
    )
    {
        if (!Open(
                devicePath
            ))
        {
            return false;
        }


        Console.WriteLine(
            "[SCXI] Sending Steam Controller test rumble..."
        );


        const ushort testStrength =
            14000;


        try
        {
            SetRumble(
                testStrength,
                testStrength
            );


            await Task.Delay(
                250
            );


            StopRumble();


            Console.WriteLine(
                "[SCXI] Test rumble complete."
            );


            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Test rumble failed: " +
                ex.Message
            );


            StopRumble();


            return false;
        }
    }


    // =================================================================
    // CLOSE
    // =================================================================

    public void Close()
    {
        // Stop the recurring timer first.
        _rumbleTimer.Change(
            Timeout.Infinite,
            Timeout.Infinite
        );


        System.Threading.Volatile.Write(
            ref _leftStrength,
            0
        );


        System.Threading.Volatile.Write(
            ref _rightStrength,
            0
        );


        lock (_writeLock)
        {
            if (_deviceHandle is null)
            {
                return;
            }


            // Send one final zero-rumble report.
            if (!_deviceHandle.IsInvalid &&
                !_deviceHandle.IsClosed)
            {
                try
                {
                    WriteRumbleCore(
                        0,
                        0
                    );
                }
                catch
                {
                }
            }


            try
            {
                _deviceHandle.Dispose();
            }
            catch
            {
            }


            _deviceHandle =
                null;


            _outputReportLength =
                RUMBLE_COMMAND_LENGTH;
        }


        Console.WriteLine(
            "[SCXI] Steam Controller haptics closed."
        );
    }


    // =================================================================
    // CLEANUP
    // =================================================================

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }


        Close();


        _disposed =
            true;


        _rumbleTimer.Dispose();
    }
}
