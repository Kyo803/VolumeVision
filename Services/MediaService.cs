using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace VolumeOSD.Services;

/// <summary>
/// Any-app media control with no API keys:
/// - Source: best SMTC session — Spotify preferred, otherwise the last active
///   audio source (Chrome, Edge, Apple Music, VLC, …).
/// - Transport + progress + art: Windows SMTC.
/// - Per-app volume: CoreAudio sessions matched to the SMTC app's process(es).
/// </summary>
public sealed class MediaService
{
    public record MediaState(
        bool AppRunning,
        bool HasSession,
        bool Playing,
        string AppName,
        string Title,
        string Artist,
        double PositionSec,
        double DurationSec,
        float AppVolume);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    // PID cache: resolving processes is expensive, refresh at most every 10s.
    private string _pidsForAumid = "";
    private HashSet<int> _pids = new();
    private DateTime _pidsAt = DateTime.MinValue;

    public async Task InitAsync()
    {
        try { _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { _manager = null; }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    private static string AumidOf(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.SourceAppUserModelId ?? ""; }
        catch { return ""; }
    }

    public async Task<GlobalSystemMediaTransportControlsSession?> GetMediaSessionAsync()
    {
        if (_manager == null) await InitAsync();
        if (_manager == null) return null;
        try
        {
            var sessions = _manager.GetSessions();
            GlobalSystemMediaTransportControlsSession? best = null;
            int bestScore = -1;
            foreach (var s in sessions)
            {
                try
                {
                    string id = AumidOf(s);
                    int score = (IsPlaying(s) ? 2 : 0)
                        + (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                    if (score > bestScore) { bestScore = score; best = s; }
                }
                catch { /* session gone */ }
            }
            // A playing session always wins; otherwise the last-active one.
            if (best != null && bestScore >= 2) return best;
            try
            {
                var current = _manager.GetCurrentSession();
                if (current != null) return current;
            }
            catch { }
            return best;
        }
        catch { return null; }
    }

    public async Task<MediaState> GetStateAsync()
    {
        var session = await GetMediaSessionAsync();
        if (session == null)
            return new MediaState(false, false, false, "", "", "", 0, 0, 1f);

        string aumid = AumidOf(session);
        string appName = FriendlyAppName(aumid);
        var pids = ResolvePids(aumid);
        bool running = pids.Count > 0;

        bool playing = false;
        string title = "", artist = "";
        double pos = 0, dur = 0;
        try
        {
            var pb = session.GetPlaybackInfo();
            playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var props = await session.TryGetMediaPropertiesAsync();
            title = props.Title ?? "";
            artist = props.Artist ?? "";
            var timeline = session.GetTimelineProperties();
            pos = timeline.Position.TotalSeconds;
            dur = timeline.EndTime.TotalSeconds;
        }
        catch { }
        return new MediaState(running, true, playing, appName, title, artist, pos, dur, GetAppVolume());
    }

    public async Task TogglePlayPauseAsync()
    {
        var s = await GetMediaSessionAsync();
        if (s == null) return;
        try
        {
            if (IsPlaying(s)) await s.TryPauseAsync();
            else await s.TryPlayAsync();
        }
        catch { }
    }

    public async Task NextAsync()
    {
        var s = await GetMediaSessionAsync();
        if (s != null) try { await s.TrySkipNextAsync(); } catch { }
    }

    public async Task PrevAsync()
    {
        var s = await GetMediaSessionAsync();
        if (s != null) try { await s.TrySkipPreviousAsync(); } catch { }
    }

    // ---- Per-app volume via CoreAudio audio sessions ----

    public float GetAppVolume()
    {
        float found = 1f;
        ForEachAppSession(s => { found = s.SimpleAudioVolume.Volume; });
        return found;
    }

    public void SetAppVolume(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        ForEachAppSession(s => { s.SimpleAudioVolume.Volume = v; });
    }

    private void ForEachAppSession(Action<AudioSessionControl> action)
    {
        if (_pids.Count == 0) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    if (_pids.Contains((int)s.GetProcessID))
                        action(s);
                }
                catch { }
            }
        }
        catch { }
    }

    // ---- App identity: SMTC app id -> process ids ----

    private HashSet<int> ResolvePids(string aumid)
    {
        if (!string.IsNullOrEmpty(aumid) && aumid == _pidsForAumid
            && (DateTime.UtcNow - _pidsAt).TotalSeconds < 10)
            return _pids;

        var ids = new HashSet<int>();
        if (!string.IsNullOrEmpty(aumid))
        {
            // 1. Process-name tokens from the id (covers Spotify, Chrome, …).
            foreach (var token in aumid.Split('.', '_', '!', '+', '-', ' '))
            {
                if (token.Length < 3) continue;
                try
                {
                    foreach (var p in Process.GetProcessesByName(token))
                    {
                        ids.Add(p.Id);
                        p.Dispose();
                    }
                }
                catch { }
                if (ids.Count > 0) break;
            }
            // 2. UWP package-family match (Apple Music, …).
            if (ids.Count == 0) UnionUwpPids(aumid, ids);
            // 3. Last resort: the single non-system audio session.
            if (ids.Count == 0) UnionSingleSessionPid(ids);
        }

        _pidsForAumid = aumid ?? "";
        _pids = ids;
        _pidsAt = DateTime.UtcNow;
        return ids;
    }

    private static void UnionUwpPids(string aumid, HashSet<int> ids)
    {
        try
        {
            string family = aumid.Split('!')[0];
            if (!family.Contains('_')) return; // not a packaged app id
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                using var proc = p;
                try
                {
                    if (proc.Id == self) continue;
                    if (string.Equals(GetPackageFamilyName(proc.Handle), family, StringComparison.OrdinalIgnoreCase))
                        ids.Add(proc.Id);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void UnionSingleSessionPid(HashSet<int> ids)
    {
        try
        {
            int self = Environment.ProcessId;
            var found = new HashSet<int>();
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    int pid = (int)sessions[i].GetProcessID;
                    if (pid > 0 && pid != self) found.Add(pid);
                }
                catch { }
            }
            if (found.Count == 1) ids.UnionWith(found);
        }
        catch { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr hProcess, ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    private static string? GetPackageFamilyName(IntPtr hProcess)
    {
        try
        {
            uint len = 0;
            const int ERROR_INSUFFICIENT_BUFFER = 122;
            if (GetPackageFamilyName(hProcess, ref len, null) != ERROR_INSUFFICIENT_BUFFER || len == 0)
                return null;
            var sb = new StringBuilder((int)len);
            return GetPackageFamilyName(hProcess, ref len, sb) == 0 ? sb.ToString() : null;
        }
        catch { return null; }
    }

    private static string FriendlyAppName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        string low = aumid.ToLowerInvariant();
        if (low.Contains("spotify")) return "Spotify";
        if (low.Contains("applemusic")) return "Apple Music";
        if (low.Contains("chrome")) return "Chrome";
        if (low.Contains("msedge") || low.Contains("edge")) return "Edge";
        if (low.Contains("firefox")) return "Firefox";
        if (low.Contains("brave")) return "Brave";
        if (low.Contains("opera")) return "Opera";
        if (low.Contains("vlc")) return "VLC";
        // Fallback: last meaningful token (e.g. exe name).
        string head = aumid.Split('!')[0];
        string[] parts = head.Split('.', '_', '-', ' ');
        for (int i = parts.Length - 1; i >= 0; i--)
            if (parts[i].Length >= 3 && !parts[i].Any(char.IsDigit))
                return parts[i];
        return head;
    }
}
