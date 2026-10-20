using System;
using System.Threading;
using System.Windows;

namespace Byte.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--probe") { SmokeTests.RunProbe(args); return; }
        using var mutex = new Mutex(true, @"Local\Byte.DesktopCompanion", out var created);
        if (!created)
        {
            MessageBox.Show("Byte is already running. Use its tray icon or your summon hotkey.", "Byte",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Storage.Log(e.Exception);
            MessageBox.Show("Byte hit an unexpected error and will close safely.\nDetails: " +
                System.IO.Path.Combine(Storage.Folder, "errors.log"), "Byte", MessageBoxButton.OK, MessageBoxImage.Error);
            app.Shutdown(1);
            e.Handled = true;
        };
        try
        {
            using var host = new CompanionHost(args);
            app.Run();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Storage.Log(ex);
            MessageBox.Show("Byte couldn't start: " + ex.Message + "\nDetails: " + Storage.Folder,
                "Byte", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
