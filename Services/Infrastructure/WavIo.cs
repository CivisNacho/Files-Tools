using System;
using System.IO;
using System.Text;

namespace Files_Tools.Services.Infrastructure;

/// <summary>
/// Minimal helpers for reading and writing uncompressed mono WAV files used by the ML pipeline
/// stages (DeepFilterNet, FlashSR, Wav2Vec2).
/// </summary>
internal static class WavIo
{
    /// <summary>
    /// Reads a mono WAV file (16-bit PCM or 32-bit float) and returns the samples as
    /// <see cref="float"/>. Multi-channel files are downmixed to mono by averaging channels.
    /// Returns an empty array if the file has no data chunk.
    /// </summary>
    public static float[] ReadMonoFloatWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (ReadTag(reader) != "RIFF")
        {
            throw new InvalidDataException("Not a RIFF/WAV file.");
        }

        reader.ReadInt32(); // overall size
        if (ReadTag(reader) != "WAVE")
        {
            throw new InvalidDataException("Not a WAVE file.");
        }

        short audioFormat = 1;
        short channels = 1;
        short bitsPerSample = 16;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadTag(reader);
            var chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                reader.ReadInt32(); // sample rate
                reader.ReadInt32(); // byte rate
                reader.ReadInt16(); // block align
                bitsPerSample = reader.ReadInt16();
                var consumed = 16;
                if (chunkSize > consumed)
                {
                    reader.ReadBytes(chunkSize - consumed);
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
                if (chunkSize % 2 != 0 && stream.Position < stream.Length)
                {
                    reader.ReadByte(); // RIFF word-alignment padding byte
                }
            }
            else
            {
                reader.ReadBytes(chunkSize + (chunkSize & 1)); // skip, honouring word alignment
            }
        }

        if (data is null)
        {
            return Array.Empty<float>();
        }

        return DecodeToMono(data, audioFormat, channels, bitsPerSample);
    }

    /// <summary>
    /// Writes mono 32-bit IEEE float PCM as a standard WAV file.
    /// </summary>
    public static void WriteMonoFloat32Wav(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        int dataBytes = samples.Length * 4;
        const short channels = 1;
        const short bitsPerSample = 32;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                       // fmt chunk size
        w.Write((short)3);                 // format = IEEE float
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * (bitsPerSample / 8))); // block align
        w.Write(bitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write(s);
        }
    }

    private static string ReadTag(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private static float[] DecodeToMono(byte[] data, short audioFormat, short channels, short bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0 || channels <= 0)
        {
            return Array.Empty<float>();
        }

        var frameCount = data.Length / (bytesPerSample * channels);
        var result = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var index = ((frame * channels) + channel) * bytesPerSample;
                sum += ReadSample(data, index, audioFormat, bitsPerSample);
            }

            result[frame] = (float)(sum / channels);
        }

        return result;
    }

    private static double ReadSample(byte[] data, int index, short audioFormat, short bitsPerSample)
    {
        if (audioFormat == 3 && bitsPerSample == 32)
        {
            return BitConverter.ToSingle(data, index);
        }

        return bitsPerSample switch
        {
            16 => BitConverter.ToInt16(data, index) / 32768.0,
            32 => BitConverter.ToInt32(data, index) / 2147483648.0,
            8 => (data[index] - 128) / 128.0,
            _ => 0
        };
    }
}
