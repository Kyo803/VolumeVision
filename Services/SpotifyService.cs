using System.Diagnostics;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace VolumeOSD.Services;

/// <summary>
/// Spotify integration with no API key:
/// - Detection: Process.GetProcessesByName("Spotify") + SMTC session lookup
/// - Transport + progress: Windows SMTC (title/artist/thumb/position/duration/play-state)
/// - Per-app volume: CoreAudio audio-session enumeration for the Spotify PID
///   (ISimpleAudioVolume equivalent via NAudio).
/// </summary>
public sealed class SpotifyService
{
    public record SpotifyState(
        bool Running,
        bool HasSession,
        bool Playing,
        string Title,
        string Artist,
        double PositionSec,
        double DurationSec,
        float AppVolume);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task InitAsync()
    {
        try { _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { _manager = null; }
    }

    public static bool IsSpotifyProcessRunning()
        => Process.GetProcessesByName("Spotify").Length > 0;

    public async Task<GlobalSystemMediaTransportControlsSession?> GetSpotifySessionAsync()
    {
        if (_manager == null) await InitAsync();
        if (_manager == null) return null;
        try
        {
            var sessions = _manager.GetSessions();
            foreach (var s in sessions)
            {
                try
                {
                    if (s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                catch { /* session gone */ }
            }
            // Fallback: any playing session (Spotify usually the active one)
            return _manager.GetCurrentSession();
        }
        catch { return null; }
    }

    public async Task<SpotifyState> GetStateAsync()
    {
        bool running = IsSpotifyProcessRunning();
        var session = await GetSpotifySessionAsync();
        if (session == null)
            return new SpotifyState(running, false, false, "", "", 0, 0, GetSpotifyAppVolume());

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
        return new SpotifyState(running, true, playing, title, artist, pos, dur, GetSpotifyAppVolume());
    }

    public async Task TogglePlayPauseAsync()
    {
        var s = await GetSpotifySessionAsync();
        if (s == null) return;
        try
        {
            var pb = s.GetPlaybackInfo();
            if (pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await s.TryPauseAsync();
            else
                await s.TryPlayAsync();
        }
        catch { }
    }

    public async Task NextAsync()
    {
        var s = await GetSpotifySessionAsync();
        if (s != null) try { await s.TrySkipNextAsync(); } catch { }
    }

    public async Task PrevAsync()
    {
        var s = await GetSpotifySessionAsync();
        if (s != null) try { await s.TrySkipPreviousAsync(); } catch { }
    }

    // ---- Per-app (Spotify.exe) volume via audio sessions ----

    public float GetSpotifyAppVolume()
    {
        try
        {
            var pids = Process.GetProcessesByName("Spotify").Select(p => p.Id).ToHashSet();
            if (pids.Count == 0) return 1f;
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    uint pid = s.GetProcessID;
                    if (pids.Contains((int)pid))
                        return s.SimpleAudioVolume.Volume;
                }
                catch { }
            }
        }
        catch { }
        return 1f;
    }

    public void SetSpotifyAppVolume(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        try
        {
            var pids = Process.GetProcessesByName("Spotify").Select(p => p.Id).ToHashSet();
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    if (pids.Contains((int)s.GetProcessID))
                        s.SimpleAudioVolume.Volume = v;
                }
                catch { }
            }
        }
        catch { }
    }
}
