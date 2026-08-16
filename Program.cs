using System.Windows.Forms;


// =====================================================================
// SCXI
// Steam Controller -> XInput
// =====================================================================

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("SCXI");
        Console.WriteLine("Steam Controller -> XInput");
        Console.WriteLine("==========================");
        Console.WriteLine();

        Application.EnableVisualStyles();

        Application.SetCompatibleTextRenderingDefault(
            false
        );

        using var app =
            new TrayApplicationContext();

        Application.Run(
            app
        );
    }
}
