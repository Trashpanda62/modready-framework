// BetaDeps.Harmony -- AudioMetadata
//
// Minimal, dependency-free reader for the two facts the PSAI soundtrack
// generator must get right per audio file: SampleRate and TotalLengthInSamples
// (PCM samples per channel). PSAI uses these to schedule a segment's
// post-beat / loop point; a wrong TotalLengthInSamples is the documented
// cause of premature looping or a cut-off track (see the community findings
// in V1.0-MUSIC-PICKER-PLAN.md §"Community Findings").
//
// .ogg (Vorbis): sample rate + channels come from the identification header
// packet in the first Ogg page; total samples is the granule position of the
// LAST Ogg page (Vorbis granulepos == absolute PCM sample count).
// .wav (RIFF/PCM): sample rate + block align from the fmt chunk; total samples
// = data-chunk byte size / block align.
//
// Everything is wrapped so a malformed or unreadable file yields Parsed=false
// and a conservative fallback rather than throwing -- a bad track must never
// take the game down; it just gets a best-effort length.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;
using System.Text;

using BetaDeps.Foundation;

namespace BetaDeps.Harmony.Music;

public readonly struct AudioInfo
{
    public int SampleRate { get; }
    public long TotalSamples { get; }
    public int Channels { get; }
    public bool Parsed { get; }

    public AudioInfo(int sampleRate, long totalSamples, int channels, bool parsed)
    {
        SampleRate = sampleRate;
        TotalSamples = totalSamples;
        Channels = channels;
        Parsed = parsed;
    }
}

public static class AudioMetadata
{
    private const string Tag = "AudioMetadata";

    // Conservative fallback when parsing fails: 44.1 kHz, ~10 min of samples so
    // PSAI doesn't loop a still-playing track early. Better to slightly overrun
    // than to cut a track off mid-phrase.
    private const int FallbackSampleRate = 44100;
    private const long FallbackTotalSamples = 44100L * 600L;

    public static AudioInfo Read(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            AudioInfo info = ext switch
            {
                ".ogg" => ReadOgg(path),
                ".wav" => ReadWav(path),
                _ => default,
            };
            if (info.Parsed && info.SampleRate > 0 && info.TotalSamples > 0)
                return info;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Read({Path.GetFileName(path)})", ex);
        }
        DiagLog.Log(Tag, $"could not parse '{Path.GetFileName(path)}'; using fallback {FallbackSampleRate}Hz / {FallbackTotalSamples} samples.");
        return new AudioInfo(FallbackSampleRate, FallbackTotalSamples, 2, false);
    }

    // ---- Ogg / Vorbis -------------------------------------------------

    private static AudioInfo ReadOgg(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Sample rate + channels from the first page's Vorbis id header.
        var head = new byte[4096];
        int headRead = fs.Read(head, 0, head.Length);
        if (!ParseOggIdHeader(head, headRead, out int sampleRate, out int channels))
            return default;

        // Total samples = granulepos of the last Ogg page. Scan the file tail.
        long totalSamples = ReadLastGranulePos(fs);
        if (totalSamples <= 0) return default;

        return new AudioInfo(sampleRate, totalSamples, channels, parsed: true);
    }

    private static bool ParseOggIdHeader(byte[] buf, int len, out int sampleRate, out int channels)
    {
        sampleRate = 0; channels = 0;
        // First page must start with the "OggS" capture pattern.
        if (len < 27 || buf[0] != 'O' || buf[1] != 'g' || buf[2] != 'g' || buf[3] != 'S')
            return false;

        int pageSegments = buf[26];
        int dataStart = 27 + pageSegments;                       // first packet byte
        // Vorbis id packet: 0x01 "vorbis" <version:4> <channels:1> <sampleRate:4 LE>
        if (dataStart + 16 > len) return false;
        if (buf[dataStart] != 0x01) return false;
        if (Encoding.ASCII.GetString(buf, dataStart + 1, 6) != "vorbis") return false;

        channels = buf[dataStart + 11];
        sampleRate = BitConverter.ToInt32(buf, dataStart + 12);
        return sampleRate > 0 && channels > 0;
    }

    private static long ReadLastGranulePos(FileStream fs)
    {
        // Read up to the last 64 KiB; the final Ogg page's header (and thus its
        // granulepos) lives there for any normal track.
        const int tailLen = 65536;
        long len = fs.Length;
        int toRead = (int)Math.Min(tailLen, len);
        var tail = new byte[toRead];
        fs.Seek(len - toRead, SeekOrigin.Begin);
        ReadFully(fs, tail, toRead);

        long granule = -1;
        // Walk every "OggS" capture pattern; keep the last page whose granulepos
        // is a real value (not -1, which marks a page with no packet boundary).
        for (int i = 0; i + 27 <= tail.Length; i++)
        {
            if (tail[i] != 'O' || tail[i + 1] != 'g' || tail[i + 2] != 'g' || tail[i + 3] != 'S')
                continue;
            // granulepos is 8 bytes LE at offset 6 within the page header.
            long g = BitConverter.ToInt64(tail, i + 6);
            if (g >= 0) granule = g;
        }
        return granule;
    }

    // ---- WAV / RIFF ---------------------------------------------------

    private static AudioInfo ReadWav(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        if (new string(br.ReadChars(4)) != "RIFF") return default;
        br.ReadInt32();                                          // overall size
        if (new string(br.ReadChars(4)) != "WAVE") return default;

        int sampleRate = 0, blockAlign = 0, channels = 0;
        long dataBytes = 0;

        while (fs.Position + 8 <= fs.Length)
        {
            string chunkId = new string(br.ReadChars(4));
            int chunkSize = br.ReadInt32();
            long next = fs.Position + chunkSize + (chunkSize & 1); // chunks are word-aligned

            if (chunkId == "fmt ")
            {
                br.ReadInt16();                                  // audio format
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32();                                  // byte rate
                blockAlign = br.ReadInt16();
                br.ReadInt16();                                  // bits per sample
            }
            else if (chunkId == "data")
            {
                dataBytes = chunkSize;
            }

            if (next <= fs.Position) break;                      // malformed guard
            fs.Seek(next, SeekOrigin.Begin);
        }

        if (sampleRate <= 0 || blockAlign <= 0 || dataBytes <= 0) return default;
        long totalSamples = dataBytes / blockAlign;
        return new AudioInfo(sampleRate, totalSamples, channels, parsed: true);
    }

    // ---- helpers ------------------------------------------------------

    private static void ReadFully(Stream s, byte[] buf, int count)
    {
        int off = 0;
        while (off < count)
        {
            int r = s.Read(buf, off, count - off);
            if (r <= 0) break;
            off += r;
        }
    }
}
