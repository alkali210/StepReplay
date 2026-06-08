namespace StepReplay.Models;

public sealed class AppSettings
{
    public const string DefaultStopReplayHotkey = "Ctrl+Alt+F4";

    public int SettingsVersion { get; set; }
    public string Language { get; set; } = "zh-CN";
    public string ThemeMode { get; set; } = "Default";
    public string BackdropKind { get; set; } = "Mica";
    public int RecordDelaySeconds { get; set; } = 3;
    public int ReplayDelaySeconds { get; set; } = 3;
    public int ReplayRepeatCount { get; set; } = 1;
    public bool ShowMouseMovesInList { get; set; }
    public string StartRecordingHotkey { get; set; } = string.Empty;
    public string StopRecordingHotkey { get; set; } = string.Empty;
    public string StartReplayHotkey { get; set; } = string.Empty;
    public string StopReplayHotkey { get; set; } = DefaultStopReplayHotkey;
}
