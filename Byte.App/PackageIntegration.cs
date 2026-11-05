using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Byte.App;

internal static class PackageIntegration
{
    private const int NoPackage = 15700;
    private const int InsufficientBuffer = 122;
    public const string StartupTaskId = "ByteStartup";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);

    public static bool IsPackaged
    {
        get
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            if (result == NoPackage) return false;
            if (result is 0 or InsufficientBuffer) return true;
            throw new Win32Exception(result, "Windows could not determine Byte's package identity.");
        }
    }

    public static string LocalFolder => ApplicationData.Current.LocalFolder.Path;

    public static async Task<bool> StartupEnabled()
    {
        var task = await StartupTask.GetAsync(StartupTaskId);
        return task.State == StartupTaskState.Enabled;
    }

    public static async Task SetStartup(bool enabled)
    {
        var task = await StartupTask.GetAsync(StartupTaskId);
        if (!enabled) { task.Disable(); return; }
        var state = await task.RequestEnableAsync();
        if (state != StartupTaskState.Enabled)
            throw new InvalidOperationException(
                "Windows did not enable launch at login. Check Settings > Apps > Startup for Byte; it may be disabled by you or system policy.");
    }
}
