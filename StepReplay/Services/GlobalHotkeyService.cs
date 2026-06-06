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
    private bool _isCapturingHotkey;
    private HotkeyModifiers _captureModifiers;

    public event EventHandler<HotkeyAction>? HotkeyPressed;
    public event EventHandler<HotkeyGesture>? HotkeyCaptured;
    public event EventHandler? HotkeyCaptureCanceled;
    public event EventHandler? HotkeyCaptureCleared;
    public event EventHandler? HotkeyCaptureNeedsModifier;

    public bool SuppressHotkeys { get; set; }

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

    public void BeginCapture()
    {
        _isCapturingHotkey = true;
        _captureModifiers = GetCurrentModifiers();
        SuppressHotkeys = true;
        _activeActions.Clear();
    }

    public void EndCapture()
    {
        _isCapturingHotkey = false;
        _captureModifiers = HotkeyModifiers.None;
        SuppressHotkeys = false;
        _activeActions.Clear();
    }

    private void AddHotkey(HotkeyAction action, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (HotkeyGesture.TryParse(text, out var gesture))
        {
            if (_hotkeys.Values.Any(existing => string.Equals(existing.ToString(), gesture.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

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
                    if (_isCapturingHotkey)
                    {
                        HandleCapture(message, (int)data.vkCode);
                        return 1;
                    }

                    if (SuppressHotkeys)
                    {
                        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                    }

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

    private void HandleCapture(int message, int key)
    {
        var isKeyDown = message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
        var isKeyUp = message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
        if (!isKeyDown && !isKeyUp)
        {
            return;
        }

        if (TryGetModifier(key, out var modifier))
        {
            if (isKeyDown)
            {
                _captureModifiers |= modifier;
            }
            else
            {
                _captureModifiers &= ~modifier;
            }

            return;
        }

        if (!isKeyDown)
        {
            return;
        }

        if (key == 0x1B) // Esc
        {
            EndCapture();
            HotkeyCaptureCanceled?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (key is 0x08 or 0x2E) // Backspace / Delete
        {
            EndCapture();
            HotkeyCaptureCleared?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Because captured modifier key events are swallowed, Windows async key state is not
        // always updated before this hook runs. Track modifiers from the hook stream itself
        // and OR with async state for modifiers that were already held before capture began.
        var modifiers = _captureModifiers | GetCurrentModifiers();
        if (modifiers == HotkeyModifiers.None)
        {
            HotkeyCaptureNeedsModifier?.Invoke(this, EventArgs.Empty);
            return;
        }

        var gesture = HotkeyGesture.Create(modifiers, key);
        EndCapture();
        HotkeyCaptured?.Invoke(this, gesture);
    }

    private static bool TryGetModifier(int key, out HotkeyModifiers modifier)
    {
        modifier = key switch
        {
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Ctrl,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            0x5B or 0x5C => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None
        };

        return modifier != HotkeyModifiers.None;
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
        return (IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3)) == modifiers.HasFlag(HotkeyModifiers.Ctrl)
            && (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5)) == modifiers.HasFlag(HotkeyModifiers.Alt)
            && (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1)) == modifiers.HasFlag(HotkeyModifiers.Shift)
            && (IsDown(0x5B) || IsDown(0x5C)) == modifiers.HasFlag(HotkeyModifiers.Win);
    }

    private static HotkeyModifiers GetCurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(0x5B) || IsDown(0x5C))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    public void Dispose()
    {
        EndCapture();
        if (_keyboardHook != nint.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }
    }
}
