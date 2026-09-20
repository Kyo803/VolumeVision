using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VolumeOSD.Services;

/// <summary>
/// Keeps the background footprint tiny:
/// - EcoQoS (Task Manager leaf) while hidden, full speed while visible.
/// - Working-set trim on hide so idle RAM drops to a few MB.
/// </summary>
public static class EcoHelper
{
    private const int ProcessPowerThrottling = 4;
    private const uint EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess, int processInformationClass,
        ref POWER_THROTTLING_STATE processInformation, uint processInformationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static void SetEco(bool on)
    {
        try
        {
            var state = new POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = EXECUTION_SPEED,
                StateMask = on ? EXECUTION_SPEED : 0,
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling,
                ref state, (uint)Marshal.SizeOf(state));
        }
        catch { }
    }

    /// <returns>Working set in MB after trim.</returns>
    public static double Trim()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(GetCurrentProcess());
        }
        catch { }
        try { return Process.GetCurrentProcess().WorkingSet64 / 1048576.0; }
        catch { return -1; }
    }
}
