using StepReplay.Models;
using StepReplay.Native;
using System.Runtime.InteropServices;

namespace StepReplay.Services;

public sealed class InputReplayer
{
    public bool IsReplaying { get; private set; }

    public async Task ReplayAsync(IReadOnlyList<InputEvent> events, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0 || IsReplaying)
        {
            return;
        }

        IsReplaying = true;
        try
        {
            long previousOffset = 0;
            foreach (var inputEvent in events.OrderBy(e => e.OffsetMs))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var delay = Math.Max(0, inputEvent.OffsetMs - previousOffset);
                if (delay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
                }

                if (inputEvent.Kind == InputEventKind.Mouse)
                {
                    ReplayMouse(inputEvent);
                }
                else
                {
                    ReplayKeyboard(inputEvent);
                }

                previousOffset = inputEvent.OffsetMs;
            }
        }
        finally
        {
            IsReplaying = false;
        }
    }

    private static void ReplayMouse(InputEvent inputEvent)
    {
        var action = inputEvent.MouseAction ?? MouseAction.Move;
        Win32.SetCursorPos(inputEvent.X, inputEvent.Y);

        var flags = action switch
        {
            MouseAction.Move => 0u,
            MouseAction.LeftDown => Win32.MOUSEEVENTF_LEFTDOWN,
            MouseAction.LeftUp => Win32.MOUSEEVENTF_LEFTUP,
            MouseAction.RightDown => Win32.MOUSEEVENTF_RIGHTDOWN,
            MouseAction.RightUp => Win32.MOUSEEVENTF_RIGHTUP,
            MouseAction.MiddleDown => Win32.MOUSEEVENTF_MIDDLEDOWN,
            MouseAction.MiddleUp => Win32.MOUSEEVENTF_MIDDLEUP,
            MouseAction.Wheel => Win32.MOUSEEVENTF_WHEEL,
            MouseAction.XButtonDown => Win32.MOUSEEVENTF_XDOWN,
            MouseAction.XButtonUp => Win32.MOUSEEVENTF_XUP,
            _ => 0u
        };

        if (flags == 0)
        {
            return;
        }

        var input = new Win32.INPUT
        {
            type = Win32.INPUT_MOUSE,
            U = new Win32.INPUTUNION
            {
                mi = new Win32.MOUSEINPUT
                {
                    mouseData = action is MouseAction.Wheel or MouseAction.XButtonDown or MouseAction.XButtonUp
                        ? unchecked((uint)inputEvent.MouseData)
                        : 0,
                    dwFlags = flags
                }
            }
        };

        SendSingle(input);
    }

    private static void ReplayKeyboard(InputEvent inputEvent)
    {
        var input = new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            U = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = inputEvent.VirtualKey,
                    dwFlags = inputEvent.KeyboardAction == KeyboardAction.Up ? Win32.KEYEVENTF_KEYUP : 0
                }
            }
        };

        SendSingle(input);
    }

    private static void SendSingle(Win32.INPUT input)
    {
        var inputs = new[] { input };
        var sent = Win32.SendInput(1, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != 1)
        {
            throw new InvalidOperationException("SendInput 调用失败。");
        }
    }
}
