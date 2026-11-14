using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.LockScreen;

public class InputBlocker : IDisposable
{
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private Win32.LowLevelKeyboardProc? _keyboardProc;
    private Win32.LowLevelMouseProc? _mouseProc;

    private static readonly IntPtr IntPtrZero = IntPtr.Zero;

    private static readonly int[] BlockedKeys = new[]
    {
        Win32.VK_LWIN, Win32.VK_RWIN,
        Win32.VK_ESCAPE,
        Win32.VK_TAB,
        Win32.VK_DELETE_KEY,
        Win32.VK_CONTROL, Win32.VK_MENU
    };

    public void StartBlocking()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;

        if (curModule?.ModuleName != null)
        {
            _keyboardHookId = Win32.SetWindowsHookEx(
                Win32.WH_KEYBOARD_LL,
                _keyboardProc,
                Win32.GetModuleHandle(curModule.ModuleName),
                0);

            _mouseHookId = Win32.SetWindowsHookEx(
                Win32.WH_MOUSE_LL,
                _mouseProc,
                Win32.GetModuleHandle(curModule.ModuleName),
                0);
        }
    }

    public void StopBlocking()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        if (_mouseHookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)hookStruct.vkCode;

                if (IsBlockedKey(vkCode, msg))
                {
                    return (IntPtr)1;
                }

                if ((vkCode == Win32.VK_F4) && IsAltPressed())
                {
                    return (IntPtr)1;
                }

                if (vkCode == Win32.VK_DELETE && IsCtrlAltPressed())
                {
                    return (IntPtr)1;
                }
            }
        }

        return Win32.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            }
        }

        return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private bool IsBlockedKey(int vkCode, int msg)
    {
        foreach (var key in BlockedKeys)
        {
            if (vkCode == key)
                return true;
        }

        if (vkCode == 'r' && IsCtrlPressed())
            return true;

        if (vkCode == 'l' && IsWinPressed())
            return true;

        if (vkCode == 'p' && IsCtrlPressed())
            return true;

        if (vkCode == 'i' && IsCtrlPressed() && IsWinPressed())
            return true;

        return false;
    }

    private bool IsAltPressed()
    {
        return (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;
    }

    private bool IsCtrlPressed()
    {
        return (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
    }

    private bool IsWinPressed()
    {
        return (Win32.GetAsyncKeyState(Win32.VK_LWIN) & 0x8000) != 0 ||
               (Win32.GetAsyncKeyState(Win32.VK_RWIN) & 0x8000) != 0;
    }

    private bool IsCtrlAltPressed()
    {
        return IsCtrlPressed() && IsAltPressed();
    }

    public void Dispose()
    {
        StopBlocking();
        GC.SuppressFinalize(this);
    }
}
