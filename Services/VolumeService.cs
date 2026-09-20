using NAudio.CoreAudioApi;

namespace VolumeOSD.Services;

/// <summary>
/// System master volume via CoreAudio (MMDeviceEnumerator).
/// Same NAudio stack as Mp3Miniplayer.
/// </summary>
public sealed class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _device;

    public event Action<float, bool>? VolumeChanged;

    public VolumeService()
    {
        // At login the audio stack may not be ready yet — wait for it instead
        // of crashing the whole OSD before the desktop even settles.
        MMDevice? device = null;
        Exception? last = null;
        for (int i = 1; i <= 30; i++)
        {
            try
            {
                device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                DebugLog.Write($"audio endpoint not ready ({i}/30): {ex.Message}");
                Thread.Sleep(1000);
            }
        }
        _device = device ?? throw new InvalidOperationException("No audio render endpoint found.", last);
        _device.AudioEndpointVolume.OnVolumeNotification += OnNotif;
    }

    private void OnNotif(AudioVolumeNotificationData data)
    {
        VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
    }

    public float GetVolume() => _device.AudioEndpointVolume.MasterVolumeLevelScalar;

    public bool GetMute() => _device.AudioEndpointVolume.Mute;

    public void SetVolume(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = v;
    }

    public void ChangeBy(float delta) => SetVolume(GetVolume() + delta);

    public void ToggleMute() => _device.AudioEndpointVolume.Mute = !_device.AudioEndpointVolume.Mute;

    public void Dispose()
    {
        _device.AudioEndpointVolume.OnVolumeNotification -= OnNotif;
        _device.Dispose();
        _enumerator.Dispose();
    }
}
