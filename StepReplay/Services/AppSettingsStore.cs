using System.Text.Json;
using StepReplay.Models;

namespace StepReplay.Services;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StepReplay");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void Normalize(AppSettings settings)
    {
        if (!string.Equals(settings.Language, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            settings.Language = "zh-CN";
        }
        else
        {
            settings.Language = "en-US";
        }

        settings.RecordDelaySeconds = Math.Clamp(settings.RecordDelaySeconds, 0, 60);
        settings.ReplayDelaySeconds = Math.Clamp(settings.ReplayDelaySeconds, 0, 60);
        settings.ThemeMode = NormalizeChoice(settings.ThemeMode, ["Default", "Light", "Dark"], "Default");
        settings.BackdropKind = NormalizeChoice(settings.BackdropKind, ["Mica", "MicaAlt"], "Mica");

        settings.StartRecordingHotkey = NormalizeHotkey(settings.StartRecordingHotkey);
        settings.StopRecordingHotkey = NormalizeHotkey(settings.StopRecordingHotkey);
        settings.StartReplayHotkey = NormalizeHotkey(settings.StartReplayHotkey);
        settings.StopReplayHotkey = NormalizeHotkey(settings.StopReplayHotkey);
    }

    private static string NormalizeHotkey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return HotkeyGesture.TryParse(value, out var gesture)
            ? gesture.ToString()
            : string.Empty;
    }

    private static string NormalizeChoice(string? value, string[] allowed, string fallback)
    {
        return allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }
}
