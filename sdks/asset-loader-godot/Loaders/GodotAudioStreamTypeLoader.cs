using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;
using Godot;

namespace BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;

/// <summary>
/// IAssetTypeLoader for Godot AudioStream assets.
/// Supports WAV, OGG, and MP3 loading from raw byte buffers.
/// </summary>
public sealed class GodotAudioStreamTypeLoader : IAssetTypeLoader<AudioStream>
{
    private readonly Action<string>? _debugLog;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "audio/wav",
        "audio/wave",
        "audio/x-wav",
        "audio/ogg",
        "audio/vorbis",
        "audio/mpeg",
        "audio/mp3"
    };

    /// <inheritdoc />
    public Type AssetType => typeof(AudioStream);

    /// <summary>
    /// Creates a new Godot AudioStream type loader.
    /// </summary>
    /// <param name="debugLog">Optional debug logging callback.</param>
    public GodotAudioStreamTypeLoader(Action<string>? debugLog = null)
    {
        _debugLog = debugLog;
    }

    /// <inheritdoc />
    public async Task<AudioStream> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous Godot API - placeholder for future async implementation

        ct.ThrowIfCancellationRequested();

        AudioStream stream = metadata.ContentType switch
        {
            "audio/wav" or "audio/wave" or "audio/x-wav" => LoadWav(data, metadata.AssetId),
            "audio/ogg" or "audio/vorbis" => LoadOgg(data),
            "audio/mpeg" or "audio/mp3" => LoadMp3(data),
            _ => throw new InvalidOperationException($"Unsupported audio content type: {metadata.ContentType}")
        };

        _debugLog?.Invoke($"Loaded AudioStream: {metadata.AssetId} ({metadata.ContentType})");
        return stream;
    }

    private static AudioStreamWav LoadWav(ReadOnlyMemory<byte> data, string assetId)
    {
        var bytes = data.ToArray();

        // Parse WAV header to get format info
        // RIFF header: "RIFF" (4) + size (4) + "WAVE" (4)
        // fmt chunk: "fmt " (4) + size (4) + format data
        if (bytes.Length < 44)
            throw new InvalidOperationException($"WAV file too small: {assetId}");

        // Verify RIFF header
        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
            throw new InvalidOperationException($"Invalid WAV header: {assetId}");

        // Parse fmt chunk (assuming it starts at offset 12)
        var numChannels = BitConverter.ToInt16(bytes, 22);
        var sampleRate = BitConverter.ToInt32(bytes, 24);
        var bitsPerSample = BitConverter.ToInt16(bytes, 34);

        // Find data chunk
        var dataOffset = FindDataChunk(bytes);
        if (dataOffset < 0)
            throw new InvalidOperationException($"No data chunk found in WAV: {assetId}");

        var dataSize = BitConverter.ToInt32(bytes, dataOffset + 4);
        var audioData = new byte[dataSize];
        Array.Copy(bytes, dataOffset + 8, audioData, 0, dataSize);

        var wav = new AudioStreamWav();
        wav.Data = audioData;
        wav.Format = bitsPerSample switch
        {
            8 => AudioStreamWav.FormatEnum.Format8Bits,
            16 => AudioStreamWav.FormatEnum.Format16Bits,
            _ => AudioStreamWav.FormatEnum.Format16Bits
        };
        wav.MixRate = sampleRate;
        wav.Stereo = numChannels > 1;

        return wav;
    }

    private static int FindDataChunk(byte[] bytes)
    {
        // Search for "data" chunk starting after RIFF header
        for (var i = 12; i < bytes.Length - 8; i++)
        {
            if (bytes[i] == 'd' && bytes[i + 1] == 'a' && bytes[i + 2] == 't' && bytes[i + 3] == 'a')
                return i;
        }
        return -1;
    }

    private static AudioStreamOggVorbis LoadOgg(ReadOnlyMemory<byte> data)
    {
        // AudioStreamOggVorbis uses a static factory method in Godot 4.x
        var ogg = AudioStreamOggVorbis.LoadFromBuffer(data.ToArray());
        return ogg;
    }

    private static AudioStreamMP3 LoadMp3(ReadOnlyMemory<byte> data)
    {
        var mp3 = new AudioStreamMP3();
        mp3.Data = data.ToArray();
        return mp3;
    }

    /// <inheritdoc />
    public void Unload(AudioStream asset)
    {
        if (GodotObject.IsInstanceValid(asset))
        {
            asset.Free();
        }
    }
}
