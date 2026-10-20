using System;
using System.IO;
using System.Text.Json;
using Byte.Core;
using Microsoft.Win32;

namespace Byte.App;

internal sealed class Storage
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Byte");
    public static string SettingsPath => Path.Combine(Folder, "settings.json");
    public string? Warning { get; private set; }
    public Settings Load()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))
                ?? throw new JsonException("Settings were empty.");
            settings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Warning = "Byte could not read its settings. Defaults are in use; the original file is untouched until you save settings.";
            Log(ex);
            return new();
        }
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, true);
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find Byte's executable.");
            key.SetValue("ByteDesktopCompanion", "\"" + exe + "\"");
        }
        else key.DeleteValue("ByteDesktopCompanion", false);
    }

    public static void Log(Exception ex)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "errors.log");
        if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.Move(path, path + ".previous", true);
        File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + ex + Environment.NewLine);
    }
}
