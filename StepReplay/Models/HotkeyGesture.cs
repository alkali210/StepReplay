namespace StepReplay.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

public enum HotkeyAction
{
    StartRecording,
    StopRecording,
    StartReplay,
    StopReplay
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyToString(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(HotkeyModifiers.None, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        int? key = null;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.ToUpperInvariant();
            switch (part)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                case "META":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null || !TryParseKey(part, out var parsedKey))
                    {
                        return false;
                    }

                    key = parsedKey;
                    break;
            }
        }

        if (key is null || modifiers == HotkeyModifiers.None || IsModifierKey(key.Value))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    private static bool TryParseKey(string part, out int key)
    {
        key = 0;

        if (part.Length == 1)
        {
            var ch = part[0];
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = ch;
                return true;
            }
        }

        if (part.StartsWith('F') && int.TryParse(part[1..], out var fNumber) && fNumber is >= 1 and <= 24)
        {
            key = 0x70 + fNumber - 1;
            return true;
        }

        if (NamedKeys.TryGetValue(part, out key))
        {
            return true;
        }

        return false;
    }

    private static string KeyToString(int key)
    {
        if (key is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)key).ToString();
        }

        if (key is >= 0x70 and <= 0x87)
        {
            return $"F{key - 0x70 + 1}";
        }

        return NamedKeys.FirstOrDefault(kvp => kvp.Value == key).Key switch
        {
            { Length: > 0 } name => name,
            _ => $"VK{key:X2}"
        };
    }

    private static bool IsModifierKey(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C;

    private static readonly Dictionary<string, int> NamedKeys = new()
    {
        ["ESC"] = 0x1B,
        ["ESCAPE"] = 0x1B,
        ["TAB"] = 0x09,
        ["SPACE"] = 0x20,
        ["ENTER"] = 0x0D,
        ["RETURN"] = 0x0D,
        ["BACKSPACE"] = 0x08,
        ["DELETE"] = 0x2E,
        ["INSERT"] = 0x2D,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["UP"] = 0x26,
        ["DOWN"] = 0x28,
        ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27,
        ["PLUS"] = 0xBB,
        ["MINUS"] = 0xBD
    };
}
