using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VolumeOSD.Services;

/// <summary>
/// Low-level keyboard hook that swallows hardware volume keys so the
/// native Windows OSD never appears. We change the volume ourselves
/// via <see cref="VolumeService"/> and show our pill instead.
/// This is how ModernFlyouts-style replacement works: the system bar
/// isn't deleted, it just never gets triggered for intercepted keys.
/// </summary>
public sealed class VolumeKeyHook : IDisposable
{
    public event Action? VolumeUp;
    public event Action? VolumeDown;
    public event Action? MuteToggle;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_VOLUME_MUTE = 0xAD;
    private const int VK_VOLUME_DOWN = 0xAE;
    private const int VK_VOLUME_UP = 0xAF;

    private IntPtr _hook;
    private LowLevelKeyboardProc? _proc;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        _proc = HookCallback;
        // NULL module handle is the reliable pattern for WH_KEYBOARD_LL + global (thread 0)
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == VK_VOLUME_UP) { VolumeUp?.Invoke(); return (IntPtr)1; } // swallow
            if (vk == VK_VOLUME_DOWN) { VolumeDown?.Invoke(); return (IntPtr)1; }
            if (vk == VK_VOLUME_MUTE) { MuteToggle?.Invoke(); return (IntPtr)1; }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
