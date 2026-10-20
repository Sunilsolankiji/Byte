using System;

namespace Byte.Core;

public enum ActivityLevel { Quiet, Balanced, Playful }
public sealed class Settings
{
    public ActivityLevel Activity { get; set; } = ActivityLevel.Balanced;
    public bool Muted { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool RespectSystemReducedMotion { get; set; } = true;
    public double Size { get; set; } = 1;
    public bool LaunchAtLogin { get; set; }
    public string SummonHotkey { get; set; } = "Ctrl+Alt+B";
    public string HideHotkey { get; set; } = "Ctrl+Alt+H";
    public int FocusMinutes { get; set; } = 25;
    public bool PauseForFullscreen { get; set; } = true;
    public bool PointerPlay { get; set; } = true;

    public void Validate()
    {
        if (!Enum.IsDefined(Activity)) Activity = ActivityLevel.Balanced;
        Size = double.IsFinite(Size) ? Math.Clamp(Size, .65, 1.6) : 1;
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        if (string.IsNullOrWhiteSpace(SummonHotkey)) SummonHotkey = "Ctrl+Alt+B";
        if (string.IsNullOrWhiteSpace(HideHotkey)) HideHotkey = "Ctrl+Alt+H";
    }
}
