using Microsoft.Win32;


// =====================================================================
// SCXI STARTUP MANAGER
//
// Manages SCXI's per-user Windows startup entry.
//
// Registry location:
//
// HKEY_CURRENT_USER
// \Software
// \Microsoft
// \Windows
// \CurrentVersion
// \Run
//
// This does NOT require administrator rights.
// =====================================================================

internal static class StartupManager
{
    private const string StartupRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";


    private const string StartupValueName =
        "SCXI";


    // =================================================================
    // CURRENT EXECUTABLE
    // =================================================================

    private static string GetExecutablePath()
    {
        string? path =
            Environment.ProcessPath;


        if (string.IsNullOrWhiteSpace(
                path
            ))
        {
            throw new InvalidOperationException(
                "SCXI could not determine its executable path."
            );
        }


        return Path.GetFullPath(
            path
        );
    }


    // =================================================================
    // BUILD WINDOWS STARTUP COMMAND
    //
    // Always quote the executable path so paths containing spaces work.
    // =================================================================

    private static string GetStartupCommand()
    {
        string executablePath =
            GetExecutablePath();


        return
            $"\"{executablePath}\"";
    }


    // =================================================================
    // IS START WITH WINDOWS ENABLED?
    //
    // Returns true only when the Windows Run entry exists AND points
    // to the copy of SCXI that is currently running.
    //
    // This prevents an old installation path from appearing enabled.
    // =================================================================

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    StartupRegistryPath,
                    writable: false
                );


            if (key is null)
            {
                return false;
            }


            string? registeredCommand =
                key.GetValue(
                    StartupValueName
                ) as string;


            if (string.IsNullOrWhiteSpace(
                    registeredCommand
                ))
            {
                return false;
            }


            string currentCommand =
                GetStartupCommand();


            return string.Equals(
                registeredCommand.Trim(),
                currentCommand.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Could not read Windows startup setting: " +
                ex.Message
            );


            return false;
        }
    }


    // =================================================================
    // ENABLE START WITH WINDOWS
    // =================================================================

    public static bool Enable()
    {
        try
        {
            string command =
                GetStartupCommand();


            using RegistryKey key =
                Registry.CurrentUser.CreateSubKey(
                    StartupRegistryPath,
                    writable: true
                );


            key.SetValue(
                StartupValueName,
                command,
                RegistryValueKind.String
            );


            Console.WriteLine(
                "[SCXI] Start with Windows enabled."
            );


            Console.WriteLine(
                "[SCXI] Startup command:"
            );


            Console.WriteLine(
                $"       {command}"
            );


            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Could not enable Start with Windows: " +
                ex.Message
            );


            return false;
        }
    }


    // =================================================================
    // DISABLE START WITH WINDOWS
    // =================================================================

    public static bool Disable()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    StartupRegistryPath,
                    writable: true
                );


            if (key is null)
            {
                return true;
            }


            key.DeleteValue(
                StartupValueName,
                throwOnMissingValue: false
            );


            Console.WriteLine(
                "[SCXI] Start with Windows disabled."
            );


            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Could not disable Start with Windows: " +
                ex.Message
            );


            return false;
        }
    }


    // =================================================================
    // GET REGISTERED COMMAND
    //
    // Mainly useful for diagnostics/testing.
    // =================================================================

    public static string? GetRegisteredCommand()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    StartupRegistryPath,
                    writable: false
                );


            return key?.GetValue(
                StartupValueName
            ) as string;
        }
        catch
        {
            return null;
        }
    }
}
