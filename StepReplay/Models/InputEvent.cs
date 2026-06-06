using System.Text.Json.Serialization;

namespace StepReplay.Models;

public enum InputEventKind
{
    Mouse,
    Keyboard
}

public enum MouseAction
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel,
    XButtonDown,
    XButtonUp
}

public enum KeyboardAction
{
    Down,
    Up
}

public sealed class InputEvent
{
    public long OffsetMs { get; set; }
    public InputEventKind Kind { get; set; }

    public MouseAction? MouseAction { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int MouseData { get; set; }

    public KeyboardAction? KeyboardAction { get; set; }
    public ushort VirtualKey { get; set; }

    [JsonIgnore]
    public string OffsetText => $"{OffsetMs:N0} ms";

    [JsonIgnore]
    public string KindText => Kind == InputEventKind.Mouse ? "鼠标" : "键盘";

    [JsonIgnore]
    public string DetailText
    {
        get
        {
            if (Kind == InputEventKind.Mouse)
            {
                return MouseAction switch
                {
                    Models.MouseAction.Move => $"移动到 ({X}, {Y})",
                    Models.MouseAction.Wheel => $"滚轮 {MouseData} @ ({X}, {Y})",
                    Models.MouseAction.XButtonDown => $"侧键按下 {MouseData} @ ({X}, {Y})",
                    Models.MouseAction.XButtonUp => $"侧键抬起 {MouseData} @ ({X}, {Y})",
                    _ => $"{MouseAction} @ ({X}, {Y})"
                };
            }

            return $"VK 0x{VirtualKey:X2} {KeyboardAction}";
        }
    }
}
