using System.Diagnostics;
using System.Net.Sockets;
using System.Text;


// =====================================================================
// VIIPER PROCESS MANAGER
//
// Existing VIIPER:
//     SCXI uses it.
//     SCXI does NOT stop it.
//
// No existing VIIPER:
//     SCXI starts it.
//     SCXI remembers the exact process.
//     SCXI stops that exact process when SCXI exits.
// =====================================================================

internal sealed class ViiperProcessManager
{
    private const int ViiperApiPort =
        3242;


    private Process? _ownedProcess;


    public bool StartedByScxi =>
        _ownedProcess is not null;


    // =================================================================
    // ENSURE VIIPER IS AVAILABLE
    // =================================================================

    public async Task EnsureRunningAsync()
    {
        Console.WriteLine(
            "[SCXI] Checking for VIIPER..."
        );


        if (await IsViiperRunningAsync())
        {
            Console.WriteLine(
                "[SCXI] Existing VIIPER server detected."
            );

            Console.WriteLine(
                "[SCXI] SCXI will not stop that instance on exit."
            );

            return;
        }


        Console.WriteLine(
            "[SCXI] No VIIPER server detected."
        );


        string viiperPath =
            FindViiperExecutable()
            ?? throw new FileNotFoundException(
                "Could not find viiper.exe. " +
                "Expected it beside SCXI, under tools\\viiper, " +
                "or in %LOCALAPPDATA%\\VIIPER."
            );


        Console.WriteLine(
            "[SCXI] Starting VIIPER:"
        );

        Console.WriteLine(
            $"       {viiperPath}"
        );


        StartViiperProcess(
            viiperPath
        );


        // =============================================================
        // WAIT FOR API SERVER
        // =============================================================

        var timeout =
            Stopwatch.StartNew();


        while (timeout.Elapsed <
            TimeSpan.FromSeconds(10))
        {
            if (_ownedProcess is not null &&
                _ownedProcess.HasExited)
            {
                throw new Exception(
                    "VIIPER exited before its API server became ready. " +
                    $"Exit code: {_ownedProcess.ExitCode}"
                );
            }


            if (await IsViiperRunningAsync())
            {
                Console.WriteLine(
                    $"[SCXI] VIIPER ready. " +
                    $"PID {_ownedProcess?.Id}."
                );

                return;
            }


            await Task.Delay(
                200
            );
        }


        throw new TimeoutException(
            "VIIPER did not become ready within 10 seconds."
        );
    }


    // =================================================================
    // START VIIPER
    // =================================================================

    private void StartViiperProcess(
        string viiperPath
    )
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    viiperPath,

                Arguments =
                    "server",

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                WindowStyle =
                    ProcessWindowStyle.Hidden,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                WorkingDirectory =
                    Path.GetDirectoryName(
                        viiperPath
                    )
                    ?? AppContext.BaseDirectory
            };


        // =============================================================
        // MAKE USBIP AVAILABLE TO VIIPER
        // =============================================================

        string usbipDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                ),
                "USBip"
            );


        if (Directory.Exists(
                usbipDirectory
            ))
        {
            string currentPath =
                startInfo.Environment.ContainsKey(
                    "PATH"
                )
                    ? startInfo.Environment["PATH"] ?? ""
                    : Environment.GetEnvironmentVariable(
                        "PATH"
                    ) ?? "";


            if (!currentPath.Contains(
                    usbipDirectory,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                startInfo.Environment["PATH"] =
                    currentPath +
                    Path.PathSeparator +
                    usbipDirectory;
            }
        }


        var process =
            new Process
            {
                StartInfo =
                    startInfo,

                EnableRaisingEvents =
                    true
            };


        process.OutputDataReceived +=
            (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(
                        e.Data
                    ))
                {
                    Console.WriteLine(
                        $"[VIIPER] {e.Data}"
                    );
                }
            };


        process.ErrorDataReceived +=
            (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(
                        e.Data
                    ))
                {
                    Console.WriteLine(
                        $"[VIIPER] {e.Data}"
                    );
                }
            };


        if (!process.Start())
        {
            process.Dispose();

            throw new Exception(
                "Windows could not start VIIPER."
            );
        }


        process.BeginOutputReadLine();
        process.BeginErrorReadLine();


        _ownedProcess =
            process;
    }


    // =================================================================
    // FIND VIIPER.EXE
    // =================================================================

    private static string?
        FindViiperExecutable()
    {
        // -------------------------------------------------------------
        // Packaged SCXI:
        // SCXI.exe + viiper.exe
        // -------------------------------------------------------------

        string besideScxi =
            Path.Combine(
                AppContext.BaseDirectory,
                "viiper.exe"
            );


        if (File.Exists(
                besideScxi
            ))
        {
            return besideScxi;
        }


        // -------------------------------------------------------------
        // Development / portable tools directory
        // -------------------------------------------------------------

        string toolsDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                "viiper",
                "viiper.exe"
            );


        if (File.Exists(
                toolsDirectory
            ))
        {
            return toolsDirectory;
        }


        // -------------------------------------------------------------
        // VIIPER Windows install location
        // -------------------------------------------------------------

        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );


        string installedViiper =
            Path.Combine(
                localAppData,
                "VIIPER",
                "viiper.exe"
            );


        if (File.Exists(
                installedViiper
            ))
        {
            return installedViiper;
        }


        return null;
    }


    // =================================================================
    // PING VIIPER
    // =================================================================

    private static async Task<bool>
        IsViiperRunningAsync()
    {
        try
        {
            using var timeout =
                new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(
                        500
                    )
                );


            using var client =
                new TcpClient();


            await client.ConnectAsync(
                "127.0.0.1",
                ViiperApiPort,
                timeout.Token
            );


            using NetworkStream stream =
                client.GetStream();


            byte[] request =
                Encoding.UTF8.GetBytes(
                    "ping\0"
                );


            await stream.WriteAsync(
                request,
                timeout.Token
            );


            await stream.FlushAsync(
                timeout.Token
            );


            byte[] response =
                new byte[1024];


            int count =
                await stream.ReadAsync(
                    response,
                    timeout.Token
                );


            if (count <= 0)
            {
                return false;
            }


            string text =
                Encoding.UTF8.GetString(
                    response,
                    0,
                    count
                );


            return text.Contains(
                "VIIPER",
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch
        {
            return false;
        }
    }


    // =================================================================
    // STOP VIIPER ONLY IF SCXI STARTED IT
    // =================================================================

    public async Task StopIfOwnedAsync()
    {
        Process? process =
            _ownedProcess;


        _ownedProcess =
            null;


        if (process is null)
        {
            return;
        }


        try
        {
            if (process.HasExited)
            {
                Console.WriteLine(
                    "[SCXI] VIIPER already stopped."
                );

                return;
            }


            int pid =
                process.Id;


            Console.WriteLine(
                $"[SCXI] Stopping VIIPER PID {pid}..."
            );


            // Kill only the exact VIIPER process
            // that SCXI itself created.
            process.Kill(
                entireProcessTree: true
            );


            using var timeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(3)
                );


            try
            {
                await process.WaitForExitAsync(
                    timeout.Token
                );
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine(
                    "[SCXI] VIIPER shutdown timed out."
                );
            }


            if (process.HasExited)
            {
                Console.WriteLine(
                    "[SCXI] VIIPER stopped."
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCXI] Could not stop VIIPER: " +
                $"{ex.Message}"
            );
        }
        finally
        {
            process.Dispose();
        }
    }
}
