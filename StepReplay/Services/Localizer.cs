using StepReplay.Models;

namespace StepReplay.Services;

public sealed class Localizer
{
    private readonly AppSettings _settings;

    public Localizer(AppSettings settings)
    {
        _settings = settings;
    }

    public string Language => _settings.Language;

    public string T(string key) =>
        _settings.Language == "en-US" && En.TryGetValue(key, out var en)
            ? en
            : Zh.GetValueOrDefault(key, key);

    public string FormatEventDetail(InputEvent inputEvent)
    {
        if (inputEvent.Kind == InputEventKind.Keyboard)
        {
            return string.Format(T("Event.Keyboard"), inputEvent.VirtualKey, inputEvent.KeyboardAction);
        }

        return inputEvent.MouseAction switch
        {
            MouseAction.Move => string.Format(T("Event.Mouse.Move"), inputEvent.X, inputEvent.Y),
            MouseAction.Wheel => string.Format(T("Event.Mouse.Wheel"), inputEvent.MouseData, inputEvent.X, inputEvent.Y),
            MouseAction.XButtonDown => string.Format(T("Event.Mouse.XDown"), inputEvent.MouseData, inputEvent.X, inputEvent.Y),
            MouseAction.XButtonUp => string.Format(T("Event.Mouse.XUp"), inputEvent.MouseData, inputEvent.X, inputEvent.Y),
            _ => string.Format(T("Event.Mouse.Default"), inputEvent.MouseAction, inputEvent.X, inputEvent.Y)
        };
    }

    public string FormatCount(string prefix, int total, int visible, int hiddenMoves)
    {
        return string.Format(T("Status.Count"), prefix, total, visible, hiddenMoves);
    }

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["App.Subtitle"] = "记录鼠标键盘操作，然后用按钮按原始节奏自动回放。",
        ["Button.Start"] = "开始录制",
        ["Button.Stop"] = "停止录制",
        ["Button.Replay"] = "回放",
        ["Button.CancelReplay"] = "取消回放",
        ["Button.Clear"] = "清空",
        ["Button.Save"] = "保存 JSON",
        ["Button.Load"] = "载入 JSON",
        ["Button.Settings"] = "设置",
        ["Button.Back"] = "返回",
        ["Status.Ready"] = "就绪",
        ["Status.Recording"] = "录制中",
        ["Status.RecordComplete"] = "录制完成",
        ["Status.Loaded"] = "已载入",
        ["Status.NoEvents"] = "未记录到事件",
        ["Status.Cleared"] = "已清空",
        ["Status.RecordStart"] = "录制中……点击“停止录制”结束",
        ["Status.RecordDelay"] = "{0} 秒后开始录制，请切换到目标窗口……",
        ["Status.ReplayDelay"] = "{0} 秒后开始回放，请切换到目标窗口……",
        ["Status.Replaying"] = "回放中……",
        ["Status.ReplayComplete"] = "回放完成",
        ["Status.ReplayCanceled"] = "回放已取消",
        ["Status.RecordFailed"] = "开始录制失败：{0}",
        ["Status.SaveComplete"] = "已保存：{0}",
        ["Status.SaveSync"] = "保存完成，但文件同步状态为 {0}",
        ["Status.SaveFailed"] = "保存失败：{0}",
        ["Status.LoadEmpty"] = "已载入：{0}，但没有事件",
        ["Status.LoadComplete"] = "{0}：{1}",
        ["Status.LoadFailed"] = "载入失败：{0}",
        ["Status.Count"] = "{0}：完整 {1:N0} 个事件，列表显示 {2:N0} 个，隐藏鼠标移动 {3:N0} 个",
        ["Column.Time"] = "时间",
        ["Column.Type"] = "类型",
        ["Column.Detail"] = "详情",
        ["Kind.Mouse"] = "鼠标",
        ["Kind.Keyboard"] = "键盘",
        ["Summary.NoHidden"] = "列表不单独显示鼠标移动事件；回放和 JSON 会保留完整移动轨迹。",
        ["Summary.Hidden"] = "已隐藏 {0:N0} 个鼠标移动事件；列表仅显示点击、滚轮、侧键和键盘事件。回放和 JSON 仍保留完整轨迹。",
        ["Info.Title"] = "提示",
        ["Info.Message"] = "开始录制和回放可在设置中配置准备时间；录制期间会忽略本窗口内操作；回放会真实控制桌面。",
        ["Settings.Title"] = "设置",
        ["Settings.Language"] = "显示语言",
        ["Settings.Language.zh-CN"] = "简体中文",
        ["Settings.Language.en-US"] = "English",
        ["Settings.RecordDelay"] = "开始录制延迟（秒）",
        ["Settings.ReplayDelay"] = "开始回放延迟（秒）",
        ["Settings.ShowMouseMoves"] = "在列表中显示鼠标轨迹记录",
        ["Settings.Saved"] = "设置已保存",
        ["Picker.Json"] = "JSON 文件",
        ["Event.Mouse.Move"] = "移动到 ({0}, {1})",
        ["Event.Mouse.Wheel"] = "滚轮 {0} @ ({1}, {2})",
        ["Event.Mouse.XDown"] = "侧键按下 {0} @ ({1}, {2})",
        ["Event.Mouse.XUp"] = "侧键抬起 {0} @ ({1}, {2})",
        ["Event.Mouse.Default"] = "{0} @ ({1}, {2})",
        ["Event.Keyboard"] = "VK 0x{0:X2} {1}"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["App.Subtitle"] = "Record mouse and keyboard actions, then replay them with the original timing.",
        ["Button.Start"] = "Start recording",
        ["Button.Stop"] = "Stop recording",
        ["Button.Replay"] = "Replay",
        ["Button.CancelReplay"] = "Cancel replay",
        ["Button.Clear"] = "Clear",
        ["Button.Save"] = "Save JSON",
        ["Button.Load"] = "Load JSON",
        ["Button.Settings"] = "Settings",
        ["Button.Back"] = "Back",
        ["Status.Ready"] = "Ready",
        ["Status.Recording"] = "Recording",
        ["Status.RecordComplete"] = "Recording complete",
        ["Status.Loaded"] = "Loaded",
        ["Status.NoEvents"] = "No events recorded",
        ["Status.Cleared"] = "Cleared",
        ["Status.RecordStart"] = "Recording... click \"Stop recording\" to finish",
        ["Status.RecordDelay"] = "Recording starts in {0} seconds. Switch to the target window...",
        ["Status.ReplayDelay"] = "Replay starts in {0} seconds. Switch to the target window...",
        ["Status.Replaying"] = "Replaying...",
        ["Status.ReplayComplete"] = "Replay complete",
        ["Status.ReplayCanceled"] = "Replay canceled",
        ["Status.RecordFailed"] = "Failed to start recording: {0}",
        ["Status.SaveComplete"] = "Saved: {0}",
        ["Status.SaveSync"] = "Saved, but file sync status is {0}",
        ["Status.SaveFailed"] = "Save failed: {0}",
        ["Status.LoadEmpty"] = "Loaded: {0}, but it contains no events",
        ["Status.LoadComplete"] = "{0}: {1}",
        ["Status.LoadFailed"] = "Load failed: {0}",
        ["Status.Count"] = "{0}: {1:N0} total events, {2:N0} shown, {3:N0} mouse moves hidden",
        ["Column.Time"] = "Time",
        ["Column.Type"] = "Type",
        ["Column.Detail"] = "Details",
        ["Kind.Mouse"] = "Mouse",
        ["Kind.Keyboard"] = "Keyboard",
        ["Summary.NoHidden"] = "Mouse move events are hidden in the list; replay and JSON still keep the full path.",
        ["Summary.Hidden"] = "{0:N0} mouse move events are hidden; the list shows clicks, wheel, side buttons, and keyboard events. Replay and JSON still keep the full path.",
        ["Info.Title"] = "Tip",
        ["Info.Message"] = "The recording and replay preparation delays are configurable in Settings; actions inside this window are ignored while recording; replay controls the real desktop.",
        ["Settings.Title"] = "Settings",
        ["Settings.Language"] = "Display language",
        ["Settings.Language.zh-CN"] = "简体中文",
        ["Settings.Language.en-US"] = "English",
        ["Settings.RecordDelay"] = "Recording start delay (seconds)",
        ["Settings.ReplayDelay"] = "Replay start delay (seconds)",
        ["Settings.ShowMouseMoves"] = "Show mouse path records in the list",
        ["Settings.Saved"] = "Settings saved",
        ["Picker.Json"] = "JSON files",
        ["Event.Mouse.Move"] = "Move to ({0}, {1})",
        ["Event.Mouse.Wheel"] = "Wheel {0} @ ({1}, {2})",
        ["Event.Mouse.XDown"] = "X button down {0} @ ({1}, {2})",
        ["Event.Mouse.XUp"] = "X button up {0} @ ({1}, {2})",
        ["Event.Mouse.Default"] = "{0} @ ({1}, {2})",
        ["Event.Keyboard"] = "VK 0x{0:X2} {1}"
    };
}
