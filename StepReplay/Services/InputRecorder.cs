using StepReplay.Models;
using StepReplay.Native;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StepReplay.Services;

public sealed class InputRecorder : IDisposable
{
    private readonly Win32.HookProc _mouseHookProc;
    private readonly Win32.HookProc _keyboardHookProc;
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;
    private readonly Stopwatch _stopwatch = new();

    private nint _mouseHook;
    private nint _keyboardHook;
    private bool _isRecording;
    private int _lastMoveX = int.MinValue;
    private int _lastMoveY = int.MinValue;
    private long _lastMoveMs;

    public event EventHandler<InputEvent>? EventRecorded;

    public InputRecorder()
    {
        _mouseHookProc = MouseHookCallback;
        _keyboardHookProc = KeyboardHookCallback;
    }

    public void Start()
    {
        if (_isRecording)
        {
            return;
        }

        _lastMoveX = int.MinValue;
        _lastMoveY = int.MinValue;
        _lastMoveMs = 0;
        _stopwatch.Restart();

        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseHookProc, nint.Zero, 0);
        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardHookProc, nint.Zero, 0);

        if (_mouseHook == nint.Zero || _keyboardHook == nint.Zero)
        {
            Stop();
            throw new InvalidOperationException("安装全局鼠标/键盘钩子失败。");
        }

        _isRecording = true;
    }

    public void Stop()
    {
        _isRecording = false;
        _stopwatch.Stop();

        if (_mouseHook != nint.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }

        if (_keyboardHook != nint.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }
    }

    private nint MouseHookCallback(int nCode, nuint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0 && _isRecording)
            {
                var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                if ((data.flags & Win32.LLMHF_INJECTED) == 0 && !IsPointInCurrentProcess(data.pt))
                {
                    var action = ToMouseAction(unchecked((int)wParam));
                    if (action is not null && ShouldRecordMouseEvent(action.Value, data.pt))
                    {
                        EventRecorded?.Invoke(this, new InputEvent
                        {
                            OffsetMs = _stopwatch.ElapsedMilliseconds,
                            Kind = InputEventKind.Mouse,
                            MouseAction = action,
                            X = data.pt.X,
                            Y = data.pt.Y,
                            MouseData = ExtractMouseData(action.Value, data.mouseData)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 低级钩子回调不能让异常逃逸；否则 CLR 会终止进程（常见退出码 0xc000041d）。
            Debug.WriteLine($"Mouse hook callback failed: {ex}");
        }

        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0 && _isRecording)
            {
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                if ((data.flags & Win32.LLKHF_INJECTED) == 0 && !IsForegroundInCurrentProcess())
                {
                    var action = ToKeyboardAction(unchecked((int)wParam));
                    if (action is not null)
                    {
                        EventRecorded?.Invoke(this, new InputEvent
                        {
                            OffsetMs = _stopwatch.ElapsedMilliseconds,
                            Kind = InputEventKind.Keyboard,
                            KeyboardAction = action,
                            VirtualKey = (ushort)data.vkCode
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 低级钩子回调不能让异常逃逸；否则 CLR 会终止进程（常见退出码 0xc000041d）。
            Debug.WriteLine($"Keyboard hook callback failed: {ex}");
        }

        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private bool ShouldRecordMouseEvent(MouseAction action, Win32.POINT point)
    {
        if (action != MouseAction.Move)
        {
            return true;
        }

        var elapsed = _stopwatch.ElapsedMilliseconds;
        if (_lastMoveX == int.MinValue || _lastMoveY == int.MinValue)
        {
            _lastMoveX = point.X;
            _lastMoveY = point.Y;
            _lastMoveMs = elapsed;
            return true;
        }

        var distance =
            Math.Abs((long)point.X - _lastMoveX) +
            Math.Abs((long)point.Y - _lastMoveY);
        if (distance < 4 || elapsed - _lastMoveMs < 20)
        {
            return false;
        }

        _lastMoveX = point.X;
        _lastMoveY = point.Y;
        _lastMoveMs = elapsed;
        return true;
    }

    private bool IsPointInCurrentProcess(Win32.POINT point)
    {
        var hwnd = Win32.WindowFromPoint(point);
        if (hwnd == nint.Zero)
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == _currentProcessId;
    }

    private bool IsForegroundInCurrentProcess()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == _currentProcessId;
    }

    private static MouseAction? ToMouseAction(int message) => message switch
    {
        Win32.WM_MOUSEMOVE => MouseAction.Move,
        Win32.WM_LBUTTONDOWN => MouseAction.LeftDown,
        Win32.WM_LBUTTONUP => MouseAction.LeftUp,
        Win32.WM_RBUTTONDOWN => MouseAction.RightDown,
        Win32.WM_RBUTTONUP => MouseAction.RightUp,
        Win32.WM_MBUTTONDOWN => MouseAction.MiddleDown,
        Win32.WM_MBUTTONUP => MouseAction.MiddleUp,
        Win32.WM_MOUSEWHEEL => MouseAction.Wheel,
        Win32.WM_XBUTTONDOWN => MouseAction.XButtonDown,
        Win32.WM_XBUTTONUP => MouseAction.XButtonUp,
        _ => null
    };

    private static KeyboardAction? ToKeyboardAction(int message) => message switch
    {
        Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN => KeyboardAction.Down,
        Win32.WM_KEYUP or Win32.WM_SYSKEYUP => KeyboardAction.Up,
        _ => null
    };

    private static int ExtractMouseData(MouseAction action, uint mouseData)
    {
        if (action == MouseAction.Wheel)
        {
            return unchecked((short)((mouseData >> 16) & 0xffff));
        }

        if (action is MouseAction.XButtonDown or MouseAction.XButtonUp)
        {
            return (int)((mouseData >> 16) & 0xffff);
        }

        return 0;
    }

    public void Dispose()
    {
        Stop();
    }
}
