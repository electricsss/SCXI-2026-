using System.Text.Json;


// =====================================================================
// SCXI SETTINGS
//
// Stores small persistent user preferences.
//
// Location:
//
// %LOCALAPPDATA%\SCXI\settings.json
//
// Current settings:
//
// Enabled
//     true  = automatically start the SCXI bridge on launch
//     false = launch SCXI disabled
// =====================================================================

internal sealed class ScxiSettings
{
    // =================================================================
    // SETTINGS VALUES
    // =================================================================

    public bool Enabled
    {
        get;
        set;
    } = true;


    // =================================================================
    // SETTINGS PATH
    // =================================================================

    private static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            ),
            "SCXI"
        );


    private static string SettingsPath =>
        Path.Combine(
            SettingsDirectory,
            "settings.json"
        );


    // =================================================================
    // JSON OPTIONS
    // =================================================================

    private static readonly JsonSerializerOptions
        JsonOptions =
            new()
            {
                WriteIndented =
                    true
            };


    // =================================================================
    // LOAD
    // =================================================================

    public static ScxiSettings Load()
    {
        try
        {
            if (!File.Exists(
                    SettingsPath
                ))
            {
                Console.WriteLine(
                    "[SCXI] No settings file found. " +
                    "Using defaults."
                );


                return new ScxiSettings();
            }


            string json =
                File.ReadAllText(
                    SettingsPath
                );


            ScxiSettings? settings =
                JsonSerializer.Deserialize<ScxiSettings>(
                    json,
                    JsonOptions
                );


            if (settings is null)
            {
                Console.WriteLine(
                    "[SCXI] Settings file was empty or invalid. " +
                    "Using defaults."
                );


                return new ScxiSettings();
            }


            Console.WriteLine(
                $"[SCXI] Settings loaded. " +
                $"Enabled={settings.Enabled}"
            );


            return settings;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Could not load settings: " +
                ex.Message
            );


            Console.WriteLine(
                "[SCXI] Using default settings."
            );


            return new ScxiSettings();
        }
    }


    // =================================================================
    // SAVE
    // =================================================================

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(
                SettingsDirectory
            );


            string json =
                JsonSerializer.Serialize(
                    this,
                    JsonOptions
                );


            // Write to a temporary file first so a crash during
            // saving does not leave settings.json half-written.
            string tempPath =
                SettingsPath +
                ".tmp";


            File.WriteAllText(
                tempPath,
                json
            );


            File.Move(
                tempPath,
                SettingsPath,
                true
            );


            Console.WriteLine(
                $"[SCXI] Settings saved. " +
                $"Enabled={Enabled}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[SCXI] Could not save settings: " +
                ex.Message
            );
        }
    }
}
