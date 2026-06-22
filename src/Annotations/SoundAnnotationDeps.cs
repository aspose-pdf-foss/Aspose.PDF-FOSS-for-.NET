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

public class SoundSampleData
{
    public const long DefaultSamplingRate = 11025L;
    public const int DefaultOfSoundChannels = 1;
    public const int DefaultOfBitsPerChannel = 8;
    public const SoundSampleDataEncodingFormat DefaultEncodingFormat = SoundSampleDataEncodingFormat.Raw;

    public SoundSampleData(long samplingRate)
        : this(samplingRate, DefaultOfSoundChannels, DefaultOfBitsPerChannel, DefaultEncodingFormat) { }

    public SoundSampleData(long samplingRate, int numberOfSoundChannels)
        : this(samplingRate, numberOfSoundChannels, DefaultOfBitsPerChannel, DefaultEncodingFormat) { }

    public SoundSampleData(long samplingRate, int numberOfSoundChannels, int bitsPerChannel)
        : this(samplingRate, numberOfSoundChannels, bitsPerChannel, DefaultEncodingFormat) { }

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
