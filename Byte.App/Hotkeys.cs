using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

namespace Byte.App;

internal sealed class Hotkeys : IDisposable
{
    private readonly IntPtr hwnd;
    private readonly Dictionary<int, (uint Modifiers, uint Key)> registered = new();
    public Hotkeys(IntPtr hwnd) => this.hwnd = hwnd;

    public static (uint Modifiers, uint Key) Parse(string text)
    {
        uint modifiers = 0x4000;
        uint key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToUpperInvariant();
            switch (part)
            {
                case "CTRL": case "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": modifiers |= 8; break;
                default:
                    if (key != 0 || !Enum.TryParse<Keys>(raw, true, out var parsed) ||
                        (int)parsed < 0x20 || (int)parsed > 0xFE)
                        throw new ArgumentException("Use a key such as B, H, or F8, with Ctrl, Alt, Shift, or Win.");
                    key = (uint)parsed;
                    break;
            }
        }
        if (key == 0 || (modifiers & 15) == 0)
            throw new ArgumentException("A hotkey needs at least one modifier and a key, for example Ctrl+Alt+B.");
        return (modifiers, key);
    }

    public void Set(string summon, string hide)
    {
        var next = new Dictionary<int, (uint Modifiers, uint Key)> { [1] = Parse(summon), [2] = Parse(hide) };
        if (next[1] == next[2]) throw new ArgumentException("Summon and hide need different hotkeys.");
        var old = new Dictionary<int, (uint Modifiers, uint Key)>(registered);
        Clear();
        try
        {
            foreach (var (id, value) in next)
            {
                if (!Native.RegisterHotKey(hwnd, id, value.Modifiers, value.Key))
                    throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        "A hotkey is unavailable. Choose another combination.");
                registered[id] = value;
            }
        }
        catch (Win32Exception)
        {
            Clear();
            foreach (var (id, value) in old)
            {
                if (!Native.RegisterHotKey(hwnd, id, value.Modifiers, value.Key))
                    throw new Win32Exception("Byte could not restore a previous hotkey. Reopen settings to choose another.");
                registered[id] = value;
            }
            throw;
        }
    }

    private void Clear()
    {
        foreach (var id in registered.Keys) Native.UnregisterHotKey(hwnd, id);
        registered.Clear();
    }
    public void Dispose() => Clear();
}
