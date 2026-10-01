using System.IO;

namespace Aspose.Pdf.Annotations;

public enum SoundIcon
{
    Mic,
    Speaker,
}

public enum SoundEncoding
{
    Raw,
    Signed,
    MuLaw,
    ALaw,
}

public enum SoundSampleDataEncodingFormat
{
    Raw,
    Signed,
    muLaw,
    ALaw,
}

public class SoundData
{
    private byte[] _bytes = System.Array.Empty<byte>();

    public int Bits { get; set; } = SoundSampleData.DefaultOfBitsPerChannel;
    public int Channels { get; set; } = SoundSampleData.DefaultOfSoundChannels;
    public int Rate { get; set; } = (int)SoundSampleData.DefaultSamplingRate;
    public SoundEncoding Encoding { get; set; } = SoundEncoding.Raw;

    public Stream Contents => new MemoryStream(_bytes, writable: false);

    internal void SetContents(byte[] bytes) => _bytes = bytes ?? System.Array.Empty<byte>();
}

/// <summary>Describes the format of embedded sound samples: sampling rate, number of channels, bits per channel and encoding.</summary>
public class SoundSampleData
{
    public const long DefaultSamplingRate = 11025L;
    public const int DefaultOfSoundChannels = 1;
    public const int DefaultOfBitsPerChannel = 8;
    public const SoundSampleDataEncodingFormat DefaultEncodingFormat = SoundSampleDataEncodingFormat.Raw;

    /// <summary>Creates a sample format with the given sampling rate (samples per second), 1 channel, 8 bits per channel and raw encoding.</summary>
    public SoundSampleData(long samplingRate)
        : this(samplingRate, DefaultOfSoundChannels, DefaultOfBitsPerChannel, DefaultEncodingFormat) { }

    /// <summary>Creates a sample format with the given sampling rate and channel count, 8 bits per channel and raw encoding.</summary>
    public SoundSampleData(long samplingRate, int numberOfSoundChannels)
        : this(samplingRate, numberOfSoundChannels, DefaultOfBitsPerChannel, DefaultEncodingFormat) { }

    /// <summary>Creates a sample format with the given sampling rate, channel count and bits per channel, using raw encoding.</summary>
    public SoundSampleData(long samplingRate, int numberOfSoundChannels, int bitsPerChannel)
        : this(samplingRate, numberOfSoundChannels, bitsPerChannel, DefaultEncodingFormat) { }

    /// <summary>Creates a sample format with the given sampling rate, channel count, bits per channel and encoding.</summary>
    public SoundSampleData(
        long samplingRate,
        int numberOfSoundChannels,
        int bitsPerChannel,
        SoundSampleDataEncodingFormat soundSampleDataEncodingFormat)
    {
        SamplingRate = samplingRate;
        NumberOfSoundChannels = numberOfSoundChannels;
        BitsPerChannel = bitsPerChannel;
        EncodingFormat = soundSampleDataEncodingFormat;
    }

    public long SamplingRate { get; set; }
    public int NumberOfSoundChannels { get; set; }
    public int BitsPerChannel { get; set; }
    public SoundSampleDataEncodingFormat EncodingFormat { get; set; }
}
