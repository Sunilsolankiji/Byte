using System;
using System.IO;
using System.Text.Json;
using Byte.Core;
using Microsoft.Win32;

namespace Byte.App;

internal sealed class Storage
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Byte");
    private readonly string folder;
    private string SettingsPath => Path.Combine(folder, "settings.json");
    private string MemoryPath => Path.Combine(folder, "personality.json");
    public string? Warning { get; private set; }
    public string? MemoryWarning { get; private set; }
    public bool CanSaveMemory { get; private set; } = true;
    public Storage(string? folder = null) => this.folder = folder ?? Folder;
    public Settings Load()
    {
        Directory.CreateDirectory(folder);
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
        WriteAtomic(SettingsPath, settings);
    }

    public PersonalityMemory LoadMemory()
    {
        if (!File.Exists(MemoryPath)) return new();
        try
        {
            var memory = JsonSerializer.Deserialize<PersonalityMemory>(File.ReadAllText(MemoryPath))
                ?? throw new JsonException("Personality memory was empty.");
            memory.Validate();
            return memory;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log(ex);
            CanSaveMemory = false;
            MemoryWarning = "Byte couldn't read its personality memory. Fresh session memory is in use; the saved file is untouched. Use Forget memories in Preferences to reset it.";
            return new();
        }
    }

    public void SaveMemory(PersonalityMemory memory)
    {
        if (!CanSaveMemory) throw new InvalidOperationException("Unreadable personality memory must be explicitly reset before saving.");
        WriteAtomic(MemoryPath, memory);
    }

    public void ForgetMemory()
    {
        File.Delete(MemoryPath);
        CanSaveMemory = true;
        MemoryWarning = null;
    }

    private void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
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
