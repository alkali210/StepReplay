namespace StepReplay.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public int RecordDelaySeconds { get; set; } = 3;
    public int ReplayDelaySeconds { get; set; } = 3;
    public bool ShowMouseMovesInList { get; set; }
}
