using System.Media;

namespace CodexTray;

internal enum AgentSoundMinimumLifetime
{
    ZeroSeconds = 0,
    TenSeconds = 10,
    ThirtySeconds = 30,
    SixtySeconds = 60,
    FiveMinutes = 300
}

internal sealed record CompletionSound(string Id, string Title);

internal sealed class AgentCompletionSound : IDisposable
{
    internal const string CustomSoundId = "Custom";
    private SoundPlayer? _player;
    private Stream? _stream;

    internal static string CustomSoundPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTray", "sounds", "custom-completion.wav");

    internal static IReadOnlyList<CompletionSound> Choices { get; } = new CompletionSound[]
    {
        new("None", "None (default)"),
        new("01_robot_construction_complete", "Robot — Construction complete"),
        new("04_task_complete_chime", "Task completion chime"),
        new("05_arcade_completion", "Arcade completion"),
        new("06_confirmation_short", "Confirmation — Short"),
        new("07_confirmation_alt", "Confirmation — Alternate"),
        new("12_ludvig_all_done", "Ludvig — All done"),
        new("13_royston_finished", "Royston — Finished"),
        new("14_brenda_objective_complete", "Brenda — Objective complete"),
        new("15_damien_construction_complete", "Damien — Construction complete")
    };

    internal static string ParseSoundId(object? value) =>
        value is string id && (id == CustomSoundId || Choices.Any(sound => sound.Id == id)) ? id : "None";

    internal static bool Qualifies(TimeSpan lifetime, AgentSoundMinimumLifetime minimum) =>
        lifetime >= TimeSpan.FromSeconds((int)minimum);

    internal static string LifetimeTitle(AgentSoundMinimumLifetime minimum) => minimum switch
    {
        AgentSoundMinimumLifetime.ZeroSeconds => "0 seconds",
        AgentSoundMinimumLifetime.TenSeconds => "10 seconds",
        AgentSoundMinimumLifetime.ThirtySeconds => "30 seconds (default)",
        AgentSoundMinimumLifetime.SixtySeconds => "60 seconds",
        AgentSoundMinimumLifetime.FiveMinutes => "5 minutes",
        _ => throw new ArgumentOutOfRangeException(nameof(minimum))
    };

    internal static Stream? OpenSound(string id) => ParseSoundId(id) switch
    {
        "None" => null,
        CustomSoundId => OpenCustomSound(CustomSoundPath),
        _ => typeof(AgentCompletionSound).Assembly.GetManifestResourceStream($"CodexTray.assets.sounds.{id}.wav")
    };

    // Keep playback in memory so the saved file can be replaced during a preview.
    internal static Stream OpenCustomSound(string path) => new MemoryStream(File.ReadAllBytes(path), writable: false);

    internal static void ImportCustomSound(string sourcePath, string? destinationPath = null)
    {
        var destination = destinationPath ?? CustomSoundPath;
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            // Stage and validate before replacing the user's previous custom sound.
            using (var source = File.OpenRead(sourcePath))
            using (var copy = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                source.CopyTo(copy);
                copy.Position = 0;
                ValidateCustomWave(copy);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void ValidateCustomWave(Stream stream)
    {
        const string message = "Choose an uncompressed PCM WAV file with valid audio data.";
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 12 || reader.ReadUInt32() != 0x46464952) // RIFF
        {
            throw new InvalidDataException(message);
        }
        var end = 8L + reader.ReadUInt32();
        if (end > stream.Length || end < 12 || reader.ReadUInt32() != 0x45564157) // WAVE
        {
            throw new InvalidDataException(message);
        }
        var hasFormat = false;
        uint dataLength = 0;
        ushort blockAlign = 0;
        while (stream.Position + 8 <= end)
        {
            var chunk = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            var next = stream.Position + length;
            if (next > end)
            {
                throw new InvalidDataException(message);
            }
            if (chunk == 0x20746d66) // fmt
            {
                if (length < 16 || reader.ReadUInt16() != 1) // PCM
                {
                    throw new InvalidDataException(message);
                }
                var channels = reader.ReadUInt16();
                var sampleRate = reader.ReadUInt32();
                var byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                var bits = reader.ReadUInt16();
                if (channels == 0 || sampleRate == 0 || bits is not (8 or 16 or 24 or 32) ||
                    blockAlign != channels * (bits / 8) || byteRate != (ulong)sampleRate * blockAlign)
                {
                    throw new InvalidDataException(message);
                }
                hasFormat = true;
            }
            else if (chunk == 0x61746164) // data
            {
                dataLength = length;
            }
            stream.Position = next + (length & 1);
        }
        if (!hasFormat || dataLength == 0 || dataLength % blockAlign != 0)
        {
            throw new InvalidDataException(message);
        }
    }

    internal void Play(string id)
    {
        Stop();
        if (ParseSoundId(id) == "None")
        {
            return;
        }

        try
        {
            _stream = OpenSound(id) ?? throw new IOException("Completion sound is unavailable.");
            _player = new SoundPlayer(_stream);
            // Load first so playback failures are caught here.
            _player.Load();
            _player.Play();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            AppLog.Write($"Completion sound playback failed: {exception.Message}");
            Stop();
        }
    }

    private void Stop()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Stop();
}
