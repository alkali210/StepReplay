using System.Diagnostics;
using System.Runtime.InteropServices;
using StepReplay.Models;
using StepReplay.Native;

namespace StepReplay.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly Win32.HookProc _keyboardHookProc;
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _hotkeys = [];
    private readonly HashSet<HotkeyAction> _activeActions = [];
    private nint _keyboardHook;

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public GlobalHotkeyService()
    {
        _keyboardHookProc = KeyboardHookCallback;
    }

    public void Start(AppSettings settings)
    {
        UpdateSettings(settings);
        if (_keyboardHook != nint.Zero)
        {
            return;
        }

        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardHookProc, nint.Zero, 0);
        if (_keyboardHook == nint.Zero)
        {
            throw new InvalidOperationException("安装全局快捷键钩子失败。");
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        _hotkeys.Clear();
        AddHotkey(HotkeyAction.StartRecording, settings.StartRecordingHotkey);
        AddHotkey(HotkeyAction.StopRecording, settings.StopRecordingHotkey);
        AddHotkey(HotkeyAction.StartReplay, settings.StartReplayHotkey);
        AddHotkey(HotkeyAction.StopReplay, settings.StopReplayHotkey);
        _activeActions.Clear();
    }

    private void AddHotkey(HotkeyAction action, string text)
    {
        if (HotkeyGesture.TryParse(text, out var gesture))
        {
            _hotkeys[action] = gesture;
        }
    }

    private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                if ((data.flags & Win32.LLKHF_INJECTED) == 0)
                {
                    var message = unchecked((int)wParam);
                    if (message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN)
                    {
                        HandleKeyDown((int)data.vkCode);
                    }
                    else if (message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP)
                    {
                        HandleKeyUp((int)data.vkCode);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Global hotkey hook callback failed: {ex}");
        }

        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void HandleKeyDown(int key)
    {
        foreach (var (action, gesture) in _hotkeys)
        {
            if (gesture.Key != key || _activeActions.Contains(action) || !ModifiersMatch(gesture.Modifiers))
            {
                continue;
            }

            _activeActions.Add(action);
            HotkeyPressed?.Invoke(this, action);
        }
    }

    private void HandleKeyUp(int key)
    {
        foreach (var (action, gesture) in _hotkeys)
        {
            if (gesture.Key == key)
            {
                _activeActions.Remove(action);
            }
        }
    }

    private static bool ModifiersMatch(HotkeyModifiers modifiers)
    {
        return IsDown(0x11) == modifiers.HasFlag(HotkeyModifiers.Ctrl)
            && IsDown(0x12) == modifiers.HasFlag(HotkeyModifiers.Alt)
            && IsDown(0x10) == modifiers.HasFlag(HotkeyModifiers.Shift)
            && (IsDown(0x5B) || IsDown(0x5C)) == modifiers.HasFlag(HotkeyModifiers.Win);
    }

    private static bool IsDown(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    public void Dispose()
    {
        if (_keyboardHook != nint.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }
    }
}
