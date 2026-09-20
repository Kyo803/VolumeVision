using System.Runtime.InteropServices;
using System.Windows.Interop;
using VolumeOSD.Services;

namespace VolumeOSD;

public static class AcrylicHelper
{
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Applies acrylic/blur behind to mimic CSS backdrop-filter: blur(6px) + #000 at 73%.
    /// Call after window SourceInitialized. Falls back silently if DWM is unavailable.
    /// </summary>
    public static void EnableBlur(System.Windows.Window window, bool acrylic = true)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            // 0xBA000000 = 73% opaque black (0xBA = 186 alpha)
            int gradientColor = unchecked((int)0xBA000000);
            var policy = new AccentPolicy
            {
                AccentState = acrylic ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND : AccentState.ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 0,
                GradientColor = gradientColor,
                AnimationId = 0,
            };
            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19, // WCA_ACCENT_POLICY
                Data = ptr,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(ptr);
        }
        catch { /* non-fatal: pill still renders with solid #BA000000 */ }
    }
}
