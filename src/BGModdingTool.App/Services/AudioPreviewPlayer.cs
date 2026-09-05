using NAudio.Vorbis;
using NAudio.Wave;

namespace BGModdingTool.App.Services;

/// <summary>
/// In-app player for previews (Windows, NAudio): WAV/MP3/AIFF via
/// AudioFileReader, OGG via NAudio.Vorbis. Interplay ACM/MUS cannot be
/// decoded here (proprietary codec) — those are handed to the system player.
/// </summary>
public sealed class AudioPreviewPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private WaveStream? _reader;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public string? CurrentFile { get; private set; }

    public static bool CanPlayInApp(string path) =>
        OperatingSystem.IsWindows() &&
        Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".aiff" or ".aif" or ".ogg";

    /// <summary>
    /// Starts playback. <paramref name="onStopped"/> receives the playback
    /// error (null on normal completion) on NAudio's callback thread.
    /// </summary>
    public void Play(string path, Action<Exception?>? onStopped = null)
    {
        Stop();
        if (!CanPlayInApp(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }
        if (WaveOut.DeviceCount == 0)
            throw new InvalidOperationException("No audio output device found (WaveOut device count is 0).");

        _reader = Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? OpenVorbis(path)
            : new AudioFileReader(path);
        var output = new WaveOutEvent();
        output.PlaybackStopped += (_, e) => onStopped?.Invoke(e.Exception);
        output.Init(_reader);
        output.Play();
        _output = output;
        CurrentFile = path;
    }

    // Kept in its own method so a type-load problem in the Vorbis package can
    // only break OGG playback, not the whole Play() method (JIT compiles a
    // method's referenced types eagerly).
    private static WaveStream OpenVorbis(string path) => new VorbisWaveReader(path);

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        CurrentFile = null;
    }

    public void Dispose() => Stop();
}
