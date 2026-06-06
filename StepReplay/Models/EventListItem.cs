namespace StepReplay.Models;

public sealed class EventListItem
{
    public InputEvent Source { get; set; } = new();
    public string OffsetText { get; set; } = string.Empty;
    public string KindText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
}
