using System.IO;

namespace VolumeOSD.Services;

/// <summary>Append-only debug log at %Temp%\V^2.log — read it to see if Show/Hide/hook fire.</summary>
public static class DebugLog
{
    private static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "V^2.log");

    public static void Write(string msg)
    {
        try
        {
            File.AppendAllText(Path,
                $"{DateTime.Now:HH:mm:ss.fff} [pid:{Environment.ProcessId}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public static string LogPath => Path;
}
