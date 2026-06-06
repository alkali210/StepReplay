namespace StepReplay.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public int RecordDelaySeconds { get; set; } = 3;
    public int ReplayDelaySeconds { get; set; } = 3;
    public bool ShowMouseMovesInList { get; set; }
    public string StartRecordingHotkey { get; set; } = "Ctrl+Alt+R";
    public string StopRecordingHotkey { get; set; } = "Ctrl+Alt+S";
    public string StartReplayHotkey { get; set; } = "Ctrl+Alt+P";
    public string StopReplayHotkey { get; set; } = "Ctrl+Alt+X";
}
