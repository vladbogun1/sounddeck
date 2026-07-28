using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SoundDeck.Audio;

// Mirrors whatever plays on the source device onto a second device, so sound
// comes out of both at once. Loopback-captures the source and replays it.
// Device-level mirror (robust); per-app mirror needs process-loopback (future).
public class MirrorEngine : IDisposable
{
    WasapiLoopbackCapture? _capture;
    WasapiOut? _output;
    BufferedWaveProvider? _buffer;
    MediaFoundationResampler? _resampler;

    public bool Active { get; private set; }
    public string? TargetDeviceId { get; private set; }
    public string? LastError { get; private set; }

    public bool Start(MMDevice source, MMDevice target)
    {
        Stop();
        try
        {
            _capture = new WasapiLoopbackCapture(source);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(800),
                DiscardOnBufferOverflow = true,
            };
            _capture.DataAvailable += (_, e) => _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // Match the target device's mix format, resampling if it differs, so
            // shared-mode WasapiOut accepts the stream.
            var targetMix = target.AudioClient.MixFormat;
            IWaveProvider play = _buffer;
            if (!FormatsMatch(_capture.WaveFormat, targetMix))
            {
                _resampler = new MediaFoundationResampler(_buffer, targetMix) { ResamplerQuality = 60 };
                play = _resampler;
            }

            _output = new WasapiOut(target, AudioClientShareMode.Shared, true, 100);
            _output.Init(play);
            _capture.StartRecording();
            _output.Play();

            Active = true;
            TargetDeviceId = target.ID;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stop();
            return false;
        }
    }

    static bool FormatsMatch(WaveFormat a, WaveFormat b) =>
        a.SampleRate == b.SampleRate && a.Channels == b.Channels && a.BitsPerSample == b.BitsPerSample;

    public void Stop()
    {
        try { _capture?.StopRecording(); } catch { }
        try { _output?.Stop(); } catch { }
        try { _output?.Dispose(); } catch { }
        try { _resampler?.Dispose(); } catch { }
        try { _capture?.Dispose(); } catch { }
        _output = null;
        _capture = null;
        _buffer = null;
        _resampler = null;
        Active = false;
        TargetDeviceId = null;
    }

    public void Dispose() => Stop();
}
