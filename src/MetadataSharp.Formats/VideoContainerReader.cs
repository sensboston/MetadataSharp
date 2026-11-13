using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Formats;

/// <summary>Bounded, independent parsers for the four video readers enabled by the pinned reference build.</summary>
internal static class VideoContainerReader
{
    internal static IReadOnlyList<VideoProperty> Read(ReadOnlySpan<byte> data, MetadataFormat format, MetadataLimits limits)
    {
        var reader = new Reader(data, limits, format);
        return format switch
        {
            MetadataFormat.Asf => reader.Asf(),
            MetadataFormat.Matroska => reader.Matroska(),
            MetadataFormat.QuickTime => reader.QuickTime(),
            MetadataFormat.Riff => reader.Riff(),
            _ => throw reader.Invalid("No video reader is registered for this format.", 0)
        };
    }

    private ref struct Reader(ReadOnlySpan<byte> source, MetadataLimits budgets, MetadataFormat format)
    {
        private readonly ReadOnlySpan<byte> data = source;
        private readonly MetadataLimits limits = budgets;
        private readonly MetadataFormat kind = format;
        private readonly Dictionary<string, VideoProperty> values = new(StringComparer.Ordinal);
        private int structures;
        private int retained;
        private int width = 1;
        private int height = 1;

        private IReadOnlyList<VideoProperty> Result() => values.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();

        private void Add(string key, string value, IReadOnlyList<string>? items = null)
        {
            int bytes = Encoding.UTF8.GetByteCount(value);
            if (bytes > limits.MaxValueBytes || bytes > limits.MaxRetainedMetadataBytes - retained)
                throw Limit("A video property exceeds the value or retained-metadata budget.", 0);
            if (!values.ContainsKey(key) && values.Count >= limits.MaxTotalEntries)
                throw Limit("The video property count budget was exceeded.", 0);
            retained += bytes;
            values[key] = new VideoProperty(key, value, items);
        }

        private void Common(bool mib, bool ratio)
        {
            Add("Xmp.video.FileSize", mib ? Number(data.Length / 1048576d) : data.Length.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MimeType", kind switch
            {
                MetadataFormat.Asf => "video/asf",
                MetadataFormat.Matroska => "video/matroska",
                MetadataFormat.QuickTime => "video/quicktime",
                _ => "video/riff"
            });
            if (ratio) Add("Xmp.video.AspectRatio", Ratio(width, height));
        }

        private string Number(double value) => value.ToString("G7", CultureInfo.InvariantCulture);
        private void Count(ref int count, long offset)
        {
            if (++count > limits.MaxSegments) throw Limit("The video structure count budget was exceeded.", offset);
        }
        private void Depth(int depth, long offset)
        {
            if (depth > limits.MaxDirectoryDepth) throw Limit("The video nesting budget was exceeded.", offset);
        }
        internal MetadataException Invalid(string message, long offset) =>
            new(MetadataErrorCode.InvalidVideoContainer, message, offset >= 0 && offset <= data.Length ? offset : null);
        private MetadataException Limit(string message, long offset) =>
            new(MetadataErrorCode.LimitExceeded, message, offset >= 0 && offset <= data.Length ? offset : null);
        private void Need(int start, int length, int end, string what)
        {
            if (start < 0 || length < 0 || end < start || end > data.Length || length > end - start)
                throw Invalid("A " + what + " lies outside its enclosing video data.", start);
        }
        private uint U32LE(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4));
        private uint U32BE(int at) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        private ulong U64LE(int at) => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8));
        private ulong U64BE(int at) => BinaryPrimitives.ReadUInt64BigEndian(data.Slice(at, 8));
        private ushort U16LE(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at, 2));
        private ushort U16BE(int at) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));
        private static string Ratio(int w, int h)
        {
            if (w <= 0 || h <= 0) return "1:1";
            int a = w, b = h;
            while (b != 0) (a, b) = (b, a % b);
            return (w / a).ToString(CultureInfo.InvariantCulture) + ":" + (h / a).ToString(CultureInfo.InvariantCulture);
        }
        private string Text(int start, int length, Encoding encoding, bool trimZero = true)
        {
            if (length > limits.MaxValueBytes) throw Limit("A video text value exceeds the value budget.", start);
            ReadOnlySpan<byte> bytes = data.Slice(start, length);
            string result = encoding.GetString(bytes);
            if (trimZero) result = result.TrimEnd('\0', ' ');
            return result;
        }
        private string FourCC(int start) => Encoding.Latin1.GetString(data.Slice(start, 4));

        internal IReadOnlyList<VideoProperty> Riff()
        {
            if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8)) throw Invalid("The RIFF header is truncated or absent.", 0);
            uint declared = U32LE(4);
            long formEndLong = 8L + declared;
            if (declared < 4 || formEndLong > data.Length) throw Invalid("The RIFF form size exceeds the input.", 4);
            int end = (int)formEndLong;
            string form = FourCC(8);
            Add("Xmp.video.FileSize", data.Length.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MimeType", "video/riff");
            Add("Xmp.video.Container", "RIFF");
            Add("Xmp.video.FileType", form);
            if (form == "AVI ") WalkRiff(12, end, 0, false);
            else if (form == "WAVE") WalkRiff(12, end, 0, true);
            else WalkRiff(12, end, 0, false);
            return Result();
        }

        private void WalkRiff(int start, int end, int depth, bool audio)
        {
            Depth(depth, start);
            int pos = start;
            while (pos < end)
            {
                Count(ref structures, pos);
                if (end - pos < 8) throw Invalid("A RIFF chunk header is truncated.", pos);
                string id = FourCC(pos);
                uint length32 = U32LE(pos + 4);
                long payloadEnd64 = pos + 8L + length32;
                if (payloadEnd64 > end) throw Invalid("A RIFF chunk exceeds its parent extent.", pos);
                int payload = pos + 8, payloadEnd = (int)payloadEnd64;
                if (id == "LIST")
                {
                    if (length32 < 4) throw Invalid("A RIFF LIST has no form type.", pos);
                    string listType = FourCC(payload);
                    if (listType == "hdrl" || listType == "strl") WalkRiff(payload + 4, payloadEnd, depth + 1, audio);
                    else if (listType == "INFO") ReadInfo(payload + 4, payloadEnd);
                }
                else if (id == "avih" && length32 >= 40) ReadAviHeader(payload, payloadEnd);
                else if (id == "strh" && length32 >= 56) ReadStreamHeader(payload, payloadEnd);
                else if (id == "strf") ReadStreamFormat(payload, payloadEnd, audio);
                else if (id == "fmt " && audio) ReadStreamFormat(payload, end, true);
                int next = payloadEnd + (int)(length32 & 1);
                if (next < payloadEnd || next > end) throw Invalid("A RIFF chunk pad byte lies outside its parent.", payloadEnd);
                pos = next;
            }
        }

        private void ReadAviHeader(int p, int end)
        {
            Need(p, 40, end, "AVI main header");
            uint micro = U32LE(p);
            Add("Xmp.video.MicroSecPerFrame", micro.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MaxDataRate", U32LE(p + 4).ToString(CultureInfo.InvariantCulture));
            uint frames = U32LE(p + 16);
            Add("Xmp.video.FrameCount", frames.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.audio.ChannelType", StreamType(U32LE(p + 24)));
            Add("Xmp.video.StreamCount", U32LE(p + 28).ToString(CultureInfo.InvariantCulture));
            width = checked((int)Math.Min(U32LE(p + 32), int.MaxValue));
            height = checked((int)Math.Min(U32LE(p + 36), int.MaxValue));
            Add("Xmp.video.Width", width.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.Height", height.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.AspectRatio", Ratio(width, height));
            double rate = micro == 0 ? 0 : 1_000_000d / micro;
            if (rate != 0)
            {
                double duration = (ulong)(frames * 1000d / rate);
                Add("Xmp.video.FileDataRate", Number(data.Length / (1048576d * duration)));
                Add("Xmp.video.Duration", Number(duration));
            }
        }

        private string streamKind = "VIDS";
        private void ReadStreamHeader(int p, int end)
        {
            Need(p, 56, end, "AVI stream header");
            streamKind = FourCC(p).ToUpperInvariant() == "VIDS" ? "VIDS" : "AUDS";
            Add("Xmp.video.Codec", FourCC(p + 4));
            uint scale = U32LE(p + 20), rate = U32LE(p + 24);
            if (scale != 0)
            {
                double speed = (double)rate / scale;
                Add(streamKind == "VIDS" ? "Xmp.video.FrameRate" : "Xmp.audio.SampleRate", Number(speed));
                Add(streamKind == "VIDS" ? "Xmp.video.FrameCount" : "Xmp.audio.FrameCount", Number((double)U32LE(p + 32) / scale));
            }
            Add(streamKind == "VIDS" ? "Xmp.video.VideoQuality" : "Xmp.video.StreamQuality", U32LE(p + 40).ToString(CultureInfo.InvariantCulture));
            Add(streamKind == "VIDS" ? "Xmp.video.VideoSampleSize" : "Xmp.video.StreamSampleSize", U32LE(p + 44).ToString(CultureInfo.InvariantCulture));
        }

        private void ReadStreamFormat(int p, int end, bool audio)
        {
            if (audio)
            {
                Need(p, 22, end, "WAVE format header");
                ushort tag = U16LE(p);
                Add("Xmp.audio.Compressor", WavCodec(tag));
                // The pinned reader uses these offsets for its four output fields; retain that print behavior.
                Add("Xmp.audio.ChannelType", StreamType(U32LE(p + 2)));
                Add("Xmp.audio.SampleRate", U32LE(p + 6).ToString(CultureInfo.InvariantCulture));
                Add("Xmp.audio.SampleType", U32LE(p + 14).ToString(CultureInfo.InvariantCulture));
                Add("Xmp.audio.BitsPerSample", U32LE(p + 18).ToString(CultureInfo.InvariantCulture));
                return;
            }
            Need(p, 40, end, "AVI bitmap format header");
            Add("Xmp.video.Planes", U16LE(p + 12).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.PixelDepth", U16LE(p + 14).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.Compressor", FourCC(p + 16));
            Add("Xmp.video.ImageLength", U32LE(p + 20).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.PixelPerMeterX", U32LE(p + 24).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.PixelPerMeterY", U32LE(p + 28).ToString(CultureInfo.InvariantCulture));
            uint colours = U32LE(p + 32), important = U32LE(p + 36);
            Add("Xmp.video.NumOfColours", colours == 0 ? "Unspecified" : colours.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.NumIfImpColours", important == 0 ? "All" : important.ToString(CultureInfo.InvariantCulture));
        }

        private void ReadInfo(int p, int end)
        {
            var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
            while (p < end)
            {
                Count(ref structures, p);
                if (end - p < 8) throw Invalid("A RIFF INFO entry header is truncated.", p);
                string four = FourCC(p);
                uint length = U32LE(p + 4);
                long next64 = p + 8L + length + (length & 1);
                if (next64 > end) throw Invalid("A RIFF INFO entry exceeds its LIST.", p);
                string? key = InfoKey(four);
                if (key is not null)
                {
                    if (found.ContainsKey(key)) throw Invalid("A RIFF INFO key occurs more than once.", p);
                    found.Add(key, Text(p + 8, (int)length, Encoding.Latin1));
                }
                p = (int)next64;
            }
            foreach ((string key, string value) in found) Add(key, value);
        }

        private static string? InfoKey(string four) => four switch
        {
            "IARL" => "Xmp.video.ArchivalLocation", "IART" => "Xmp.video.Artist", "ICMS" => "Xmp.video.Commissioned",
            "ICMT" => "Xmp.video.Comment", "ICOP" => "Xmp.video.Copyright", "ICRD" => "Xmp.video.DateTimeOriginal",
            "ICRP" => "Xmp.video.Cropped", "IDIM" => "Xmp.video.Dimensions", "IDPI" => "Xmp.video.DotsPerInch",
            "IENG" => "Xmp.video.Engineer", "IGNR" => "Xmp.video.Genre", "IKEY" => "Xmp.video.Subject",
            "ILGT" => "Xmp.video.Lightness", "IMED" => "Xmp.video.Medium", "INAM" => "Xmp.video.Title",
            "IPLT" => "Xmp.video.NumOfParts", "IPRD" => "Xmp.video.Product", "IPRO" => "Xmp.video.ProducedBy",
            "IRIP" => "Xmp.video.RippedBy", "IRTD" => "Xmp.video.Rating", "ISBJ" => "Xmp.video.Subject",
            "ISFT" => "Xmp.video.Software", "ISGN" => "Xmp.video.SecondaryGenre", "ISHP" => "Xmp.video.Sharpness",
            "ISRC" => "Xmp.video.Source", "ISRF" => "Xmp.video.SourceForm", "ISTD" => "Xmp.video.ProductionStudio",
            "ISTR" => "Xmp.video.Starring", "ITCH" => "Xmp.video.Technician", "IWMU" => "Xmp.video.WatermarkURL",
            "IWRI" => "Xmp.video.WrittenBy", "LANG" => "Xmp.video.Language", "LOCA" => "Xmp.video.LocationInfo",
            "PRT1" => "Xmp.video.Part", "PRT2" => "Xmp.video.NumOfParts", "RATE" => "Xmp.video.Rate",
            "STAR" => "Xmp.video.Starring", "STAT" => "Xmp.video.Statistics", "TAPE" => "Xmp.video.TapeName",
            "TCDO" => "Xmp.video.EndTimecode", "TCOD" => "Xmp.video.StartTimecode", "TITL" => "Xmp.video.Title",
            "TLEN" => "Xmp.video.Length", "TORG" => "Xmp.video.Organization", "TRCK" => "Xmp.video.TrackNumber",
            "TURL" => "Xmp.video.URL", "TVER" => "Xmp.video.SoftwareVersion", "VMAJ" => "Xmp.video.VegasVersionMajor",
            "VMIN" => "Xmp.video.VegasVersionMinor", "YEAR" => "Xmp.video.Year", _ => null
        };

        private static string StreamType(uint count) => count switch
        {
            1 => "Mono", 2 => "Stereo", 5 => "5.1 Surround Sound", 7 => "7.1 Surround Sound", _ => "Mono"
        };
        private static string WavCodec(ushort tag) => tag switch { 1 => "Microsoft PCM", 3 => "IEEE Float", 0x55 => "MPEG Layer 3", _ => tag.ToString(CultureInfo.InvariantCulture) };

        internal IReadOnlyList<VideoProperty> Asf()
        {
            if (data.Length < 24 || !data[..16].SequenceEqual(AsfHeaderGuid)) throw Invalid("The ASF header object is truncated or absent.", 0);
            Common(true, false);
            int pos = 0;
            ReadAsfObject(ref pos, data.Length, 0);
            Add("Xmp.video.AspectRatio", Ratio(width, height));
            return Result();
        }

        private static readonly byte[] AsfHeaderGuid = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0, 0xAA, 0, 0x62, 0xCE, 0x6C];
        private void ReadAsfObject(ref int pos, int parentEnd, int depth)
        {
            Depth(depth, pos); Count(ref structures, pos);
            Need(pos, 24, parentEnd, "ASF object header");
            int start = pos;
            ulong size = U64LE(pos + 16);
            if (size < 24 || size > (ulong)(parentEnd - start)) throw Invalid("An ASF object has an invalid size.", start);
            int end = start + (int)size;
            ReadOnlySpan<byte> guid = data.Slice(start, 16);
            pos = start + 24;
            if (guid.SequenceEqual(AsfHeaderGuid))
            {
                Need(pos, 6, end, "ASF header prefix");
                uint objects = U32LE(pos); pos += 6;
                if (objects > limits.MaxSegments - structures) throw Limit("The ASF child-object count exceeds the segment budget.", start);
                for (uint i = 0; i < objects; i++) ReadAsfObject(ref pos, end, depth + 1);
            }
            else if (guid.SequenceEqual(AsfFileProperties)) ReadAsfFileProperties(pos, end);
            else if (guid.SequenceEqual(AsfContentDescription)) ReadAsfContent(pos, end);
            else if (guid.SequenceEqual(AsfStreamProperties)) ReadAsfStream(pos, end);
            else if (guid.SequenceEqual(AsfCodecList)) ReadAsfCodec(pos, end);
            else if (guid.SequenceEqual(AsfExtendedContent)) ReadAsfExtendedContent(pos, end);
            else if (guid.SequenceEqual(AsfExtendedStream)) ReadAsfExtendedStream(pos, end);
            pos = end;
        }
        private static readonly byte[] AsfFileProperties = [0xA1,0xDC,0xAB,0x8C,0x47,0xA9,0xCF,0x11,0x8E,0xE4,0,0xC0,0x0C,0x20,0x53,0x65];
        private static readonly byte[] AsfContentDescription = [0x33,0x26,0xB2,0x75,0x8E,0x66,0xCF,0x11,0xA6,0xD9,0,0xAA,0,0x62,0xCE,0x6C];
        private static readonly byte[] AsfStreamProperties = [0x91,0x07,0xDC,0xB7,0xB7,0xA9,0xCF,0x11,0x8E,0xE6,0,0xC0,0x0C,0x20,0x53,0x65];
        private static readonly byte[] AsfCodecList = [0x40,0x52,0xD1,0x86,0x1D,0x31,0xD0,0x11,0xA3,0xA4,0,0xA0,0xC9,0x03,0x48,0xF6];
        private static readonly byte[] AsfExtendedContent = [0x40,0xA4,0xD0,0xD2,0x07,0xE3,0xD2,0x11,0x97,0xF0,0,0xA0,0xC9,0x5E,0xA8,0x50];
        private static readonly byte[] AsfExtendedStream = [0xCB,0xA5,0xE6,0x14,0x72,0xC6,0x32,0x43,0x83,0x99,0xA9,0x69,0x52,0x06,0x5B,0x5A];

        private void ReadAsfFileProperties(int p, int end)
        {
            Need(p, 80, end, "ASF file properties");
            Add("Xmp.video.FileID", GuidText(data.Slice(p, 16)));
            Add("Xmp.video.FileLength", U64LE(p + 16).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.CreationDate", U64LE(p + 24).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.DataPackets", U64LE(p + 32).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.duration", U64LE(p + 40).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.SendDuration", U64LE(p + 48).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.Preroll", U64LE(p + 56).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MaxBitRate", U32LE(p + 76).ToString(CultureInfo.InvariantCulture));
        }
        private string GuidText(ReadOnlySpan<byte> bytes)
        {
            Guid value = new(bytes);
            return value.ToString("D", CultureInfo.InvariantCulture);
        }
        private void ReadAsfContent(int p, int end)
        {
            Need(p, 10, end, "ASF content description");
            ushort[] lengths = [U16LE(p), U16LE(p + 2), U16LE(p + 4), U16LE(p + 6), U16LE(p + 8)];
            string[] keys = ["Title", "Author", "Copyright", "Description", "Rating"];
            p += 10;
            for (int i = 0; i < lengths.Length; i++)
            {
                Need(p, lengths[i], end, "ASF text field");
                if (lengths[i] != 0) Add("Xmp.video." + keys[i], Text(p, lengths[i], Encoding.Unicode));
                p += lengths[i];
            }
        }
        private void ReadAsfStream(int p, int end)
        {
            Need(p, 54, end, "ASF stream properties");
            bool video = data.Slice(p, 16).SequenceEqual(AsfVideoMedia);
            bool audio = data.Slice(p, 16).SequenceEqual(AsfAudioMedia);
            ulong offset = U64LE(p + 32);
            if (video) Add("Xmp.video.TimeOffset", offset.ToString(CultureInfo.InvariantCulture));
            if (audio) Add("Xmp.audio.TimeOffset", offset.ToString(CultureInfo.InvariantCulture));
            uint typeLength = U32LE(p + 40), correctionLength = U32LE(p + 44);
            long skip = p + 54L + typeLength + correctionLength;
            if (skip > end) throw Invalid("ASF stream type data exceeds its object.", p);
            if (video && typeLength >= 11)
            {
                int v = p + 54;
                uint w = U32LE(v + 4), h = U32LE(v + 8);
                width = (int)Math.Min(w, int.MaxValue); height = (int)Math.Min(h, int.MaxValue);
                Add("Xmp.video.Width", w.ToString(CultureInfo.InvariantCulture));
                Add("Xmp.video.Height", h.ToString(CultureInfo.InvariantCulture));
            }
        }
        private static readonly byte[] AsfVideoMedia = [0xBC,0x19,0xEF,0xC0,0x5B,0x4D,0xCF,0x11,0xA8,0xFD,0,0x80,0x5F,0x5C,0x44,0x2B];
        private static readonly byte[] AsfAudioMedia = [0x40,0x99,0x69,0xF8,0x4D,0x5B,0xCF,0x11,0xA8,0xFD,0,0x80,0x5F,0x5C,0x44,0x2B];
        private void ReadAsfCodec(int p, int end)
        {
            Need(p, 20, end, "ASF codec list");
            ushort n = U16LE(p + 16); p += 20;
            for (int i = 0; i < n; i++)
            {
                Need(p, 6, end, "ASF codec entry");
                ushort type = U16LE(p); ushort nameLength = U16LE(p + 2); p += 4;
                Need(p, nameLength + 2, end, "ASF codec name");
                string prefix = type == 1 ? "Xmp.video." : "Xmp.audio.";
                if (nameLength != 0) Add(prefix + "CodecName", Text(p, nameLength, Encoding.Unicode));
                p += nameLength;
                ushort descriptionLength = U16LE(p); p += 2;
                Need(p, descriptionLength + 2, end, "ASF codec description");
                if (descriptionLength != 0) Add(prefix + "CodecDescription", Text(p, descriptionLength, Encoding.Unicode));
                p += descriptionLength;
                ushort infoLength = U16LE(p); p += 2; Need(p, infoLength, end, "ASF codec info");
                if (infoLength != 0) Add(prefix + "CodecInfo", Text(p, infoLength, Encoding.Latin1, false));
                p += infoLength;
            }
        }
        private void ReadAsfExtendedContent(int p, int end)
        {
            Need(p, 2, end, "ASF extended content description");
            ushort count = U16LE(p); p += 2; var output = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                Need(p, 2, end, "ASF descriptor name length"); ushort nl = U16LE(p); p += 2;
                Need(p, nl + 4, end, "ASF descriptor"); string name = Text(p, nl, Encoding.Unicode); p += nl;
                ushort type = U16LE(p), length = U16LE(p + 2); p += 4; Need(p, length, end, "ASF descriptor value");
                string val = type switch
                {
                    0 => Text(p, length, Encoding.Unicode),
                    1 => Text(p, length, Encoding.Latin1, false),
                    2 when length >= 2 => U16LE(p).ToString(CultureInfo.InvariantCulture),
                    3 when length >= 4 => U32LE(p).ToString(CultureInfo.InvariantCulture),
                    4 when length >= 8 => U64LE(p).ToString(CultureInfo.InvariantCulture),
                    5 when length >= 2 => U16LE(p).ToString(CultureInfo.InvariantCulture),
                    _ => string.Empty
                };
                output.Append(name).Append(": ").Append(val).Append(", "); p += length;
            }
            Add("Xmp.video.ExtendedContentDescription", output.ToString());
        }
        private void ReadAsfExtendedStream(int p, int end)
        {
            Need(p, 64, end, "ASF extended stream properties");
            Add("Xmp.video.StartTimecode", U64LE(p).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.EndTimecode", U16LE(p + 8).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.FrameRate", U16LE(p + 56).ToString(CultureInfo.InvariantCulture));
        }

        internal IReadOnlyList<VideoProperty> QuickTime()
        {
            if (data.Length < 12) throw Invalid("The QuickTime header is truncated.", 0);
            Common(true, false);
            WalkAtoms(0, data.Length, 0, string.Empty);
            Add("Xmp.video.AspectRatio", Ratio(width, height));
            return Result();
        }

        private void WalkAtoms(int start, int end, int depth, string parent)
        {
            Depth(depth, start); int p = start;
            while (p < end)
            {
                Count(ref structures, p);
                if (end - p < 8) throw Invalid("A QuickTime atom header is truncated.", p);
                uint size32 = U32BE(p); string type = FourCC(p + 4); int header = 8; ulong size;
                if (size32 == 1)
                {
                    Need(p, 16, end, "extended QuickTime atom header"); size = U64BE(p + 8); header = 16;
                }
                else if (size32 == 0) size = (ulong)(end - p);
                else size = size32;
                if (size < (ulong)header || size > (ulong)(end - p)) throw Invalid("A QuickTime atom size exceeds its enclosing extent.", p);
                int payload = p + header, atomEnd = p + (int)size;
                if (type == "ftyp") ReadFileType(payload, atomEnd);
                else if (type == "mvhd") ReadMovieHeader(payload, atomEnd);
                else if (type == "tkhd") ReadTrackHeader(payload, atomEnd);
                else if (type == "mdhd") ReadMediaHeader(payload, atomEnd);
                else if (type == "hdlr") ReadHandler(payload, atomEnd, parent);
                else if (type == "stsd") ReadSampleDescription(payload, atomEnd);
                else if (type == "vmhd") ReadVideoHeader(payload, atomEnd);
                else if (type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "dinf" or "edts" or "udta" or "tapt" or "ilst" or "meta" or "moof" or "traf")
                {
                    int childStart = payload + (type == "meta" ? 4 : 0);
                    if (childStart > atomEnd) throw Invalid("A QuickTime meta prefix exceeds its atom.", payload);
                    WalkAtoms(childStart, atomEnd, depth + 1, type);
                }
                p = atomEnd;
            }
        }

        private void ReadFileType(int p, int end)
        {
            Need(p, 8, end, "QuickTime file type");
            string major = FourCC(p);
            Add("Xmp.video.MajorBrand", BrandName(major));
            Add("Xmp.video.MinorVersion", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            int count = (end - p - 8) / 4;
            if (count > limits.MaxEntriesPerDirectory) throw Limit("The QuickTime brand count exceeds its budget.", p);
            var brands = new List<string>(count);
            for (int i = 0; i < count; i++) brands.Add(BrandName(FourCC(p + 8 + 4 * i)));
            Add("Xmp.video.CompatibleBrands", string.Join(", ", brands), brands);
        }
        private static string BrandName(string brand) => brand switch
        {
            "qt  " => "Apple QuickTime (.MOV/QT)", "isom" => "MP4 Base Media v1 [IS0 14496-12:2003]",
            "iso2" => "MP4 Base Media v2 [ISO 14496-12:2005]", "mp41" => "MP4 v1 [ISO 14496-1:ch13]",
            "avc1" => "MP4 Base w/ AVC ext [ISO 14496-12:2005]", _ => brand
        };
        private void ReadMovieHeader(int p, int end)
        {
            Need(p, 20, end, "QuickTime movie header"); byte version = data[p];
            Add("Xmp.video.MovieHeaderVersion", version.ToString(CultureInfo.InvariantCulture));
            int scaleAt = p + (version == 1 ? 20 : 12), durationAt = scaleAt + 4;
            Need(scaleAt, version == 1 ? 12 : 8, end, "QuickTime movie time values");
            uint scale = U32BE(scaleAt), duration = U32BE(durationAt);
            Add("Xmp.video.DateUTC", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.ModificationDate", U32BE(p + 8).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.TimeScale", scale.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.Duration", (scale == 0 ? 0 : duration * 1000UL / scale).ToString(CultureInfo.InvariantCulture));
        }
        private void ReadTrackHeader(int p, int end)
        {
            Need(p, 84, end, "QuickTime track header"); byte version = data[p];
            int baseAt = p + (version == 1 ? 20 : 12);
            Need(baseAt, 72, end, "QuickTime track header fields");
            Add("Xmp.video.TrackHeaderVersion", version.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.TrackCreateDate", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.TrackModifyDate", U32BE(p + 8).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.TrackID", U32BE(baseAt).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.TrackDuration", U32BE(baseAt + 8).ToString(CultureInfo.InvariantCulture));
            uint w = U32BE(baseAt + 64) >> 16, h = U32BE(baseAt + 68) >> 16;
            if (w != 0) width = (int)w;
            if (h != 0) height = (int)h;
            if (w != 0) Add("Xmp.video.Width", w.ToString(CultureInfo.InvariantCulture));
            if (h != 0) Add("Xmp.video.Height", h.ToString(CultureInfo.InvariantCulture));
        }
        private void ReadMediaHeader(int p, int end)
        {
            Need(p, 24, end, "QuickTime media header"); byte version = data[p];
            Add("Xmp.video.MediaHeaderVersion", version.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MediaCreateDate", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MediaModifyDate", U32BE(p + 8).ToString(CultureInfo.InvariantCulture));
            uint scale = U32BE(p + 12), duration = U32BE(p + 16);
            Add("Xmp.video.MediaTimeScale", scale.ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MediaDuration", (scale == 0 ? 0 : duration / scale).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.MediaLangCode", U16BE(p + 20).ToString(CultureInfo.InvariantCulture));
        }
        private void ReadHandler(int p, int end, string parent)
        {
            Need(p, 24, end, "QuickTime handler");
            string handler = FourCC(p + 8);
            Add("Xmp.video.HandlerType", handler);
        }
        private void ReadSampleDescription(int p, int end)
        {
            Need(p, 8, end, "QuickTime sample description");
            uint count = U32BE(p + 4);
            if (count > limits.MaxEntriesPerDirectory) throw Limit("The QuickTime sample-description count exceeds its budget.", p);
            int q = p + 8;
            for (uint i = 0; i < count && q < end; i++)
            {
                Need(q, 8, end, "QuickTime sample entry");
                uint size = U32BE(q); string codec = FourCC(q + 4);
                if (size < 8 || size > end - q) throw Invalid("A QuickTime sample entry exceeds its atom.", q);
                Add("Xmp.video.Codec", codec);
                if (size >= 36)
                {
                    ushort w = U16BE(q + 32), h = U16BE(q + 34);
                    if (w != 0) { width = w; Add("Xmp.video.SourceImageWidth", w.ToString(CultureInfo.InvariantCulture)); }
                    if (h != 0) { height = h; Add("Xmp.video.SourceImageHeight", h.ToString(CultureInfo.InvariantCulture)); }
                }
                q += (int)size;
            }
        }
        private void ReadVideoHeader(int p, int end)
        {
            Need(p, 12, end, "QuickTime video media header");
            Add("Xmp.video.GraphicsMode", U16BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.OpColor", U16BE(p + 6).ToString(CultureInfo.InvariantCulture));
        }

        internal IReadOnlyList<VideoProperty> Matroska()
        {
            if (data.Length < 4 || data[0] != 0x1A || data[1] != 0x45 || data[2] != 0xDF || data[3] != 0xA3)
                throw Invalid("The EBML header ID is absent.", 0);
            Common(true, false);
            ParseEbml(0, data.Length, 0, 0, "");
            Add("Xmp.video.AspectRatio", Ratio(width, height));
            return Result();
        }

        private readonly record struct EbmlHeader(ulong Id, int HeaderLength, int PayloadLength, bool Unknown);
        private EbmlHeader EbmlNext(int p, int end, bool identifier)
        {
            if (p >= end) throw Invalid("An EBML variable integer is truncated.", p);
            byte first = data[p]; byte mask = 0x80; int length = 1;
            while (length <= 8 && (first & mask) == 0) { mask >>= 1; length++; }
            if (length > 8 || (identifier && length > 4)) throw Invalid("An EBML variable integer has an invalid width.", p);
            Need(p, length, end, "EBML variable integer");
            ulong value = first;
            for (int i = 1; i < length; i++) value = (value << 8) | data[p + i];
            ulong payload = value & ((1UL << (7 * length)) - 1);
            ulong all = (1UL << (7 * length)) - 1;
            return identifier
                ? new EbmlHeader(value, length, 0, false)
                : new EbmlHeader(0, length, payload > int.MaxValue ? int.MaxValue : (int)payload, payload == all);
        }
        private static readonly HashSet<ulong> EbmlMasters = [
            0x1A45DFA3, 0x18538067, 0x1549A966, 0x1654AE6B, 0xAE, 0xE0, 0xE1, 0x1254C367, 0x7373, 0x63C0, 0x67C8,
            0x1043A770, 0x45B9, 0xB6, 0x80, 0x6240, 0x5034, 0x5035];
        private void ParseEbml(int start, int end, int depth, int trackType, string path)
        {
            Depth(depth, start); int p = start;
            while (p < end)
            {
                Count(ref structures, p);
                EbmlHeader id = EbmlNext(p, end, true); int sizeAt = p + id.HeaderLength;
                EbmlHeader size = EbmlNext(sizeAt, end, false);
                int payload = sizeAt + size.HeaderLength;
                int payloadEnd;
                if (size.Unknown)
                {
                    if (!EbmlMasters.Contains(id.Id)) throw Invalid("An unknown-size EBML element is not a recognized master.", p);
                    payloadEnd = end;
                }
                else
                {
                    if (size.PayloadLength > end - payload) throw Invalid("An EBML element exceeds its parent extent.", p);
                    payloadEnd = payload + size.PayloadLength;
                }
                if (EbmlMasters.Contains(id.Id))
                {
                    int nextTrackType = id.Id == 0xAE ? 0 : trackType;
                    ParseEbml(payload, payloadEnd, depth + 1, nextTrackType, path);
                }
                else ReadEbmlValue(id.Id, payload, payloadEnd, trackType);
                p = payloadEnd;
                if (size.Unknown) break;
            }
        }
        private void ReadEbmlValue(ulong id, int p, int end, int trackType)
        {
            int n = end - p; if (n > limits.MaxValueBytes) throw Limit("An EBML value exceeds the value budget.", p);
            string? key = id switch
            {
                0x2AD7B1 => "Xmp.video.TimecodeScale", 0x4489 => "Xmp.video.Duration", 0x4461 => "Xmp.video.DateUTC",
                0x4D80 => "Xmp.video.MuxingApp", 0x5741 => "Xmp.video.WritingApp", 0x7BA9 => "Xmp.video.Title",
                0x83 => trackType == 2 ? "Xmp.audio.ChannelType" : "Xmp.video.TrackType",
                0x86 => trackType == 2 ? "Xmp.audio.Compressor" : "Xmp.video.Codec",
                0x23E383 => trackType == 2 ? "Xmp.audio.DefaultDuration" : "Xmp.video.FrameRate",
                0xB0 => "Xmp.video.Width", 0xBA => "Xmp.video.Height", 0x54B0 => "Xmp.video.DisplayWidth",
                0x54BA => "Xmp.video.DisplayHeight", 0xB5 => "Xmp.audio.SampleRate", 0x9F => "Xmp.audio.ChannelType",
                0x6264 => "Xmp.audio.BitsPerSample", 0x22B59C => "Xmp.audio.CodecName", _ => null
            };
            if (key is null) return;
            if (id == 0xB0 && n <= 8) width = (int)Math.Min(ReadUnsigned(p, n), int.MaxValue);
            if (id == 0xBA && n <= 8) height = (int)Math.Min(ReadUnsigned(p, n), int.MaxValue);
            string value;
            if (id is 0x86 or 0x4D80 or 0x5741 or 0x7BA9 or 0x22B59C) value = Text(p, n, Encoding.UTF8);
            else if (n is >= 1 and <= 8) value = ReadUnsigned(p, n).ToString(CultureInfo.InvariantCulture);
            else if (n is 4 or 8) value = Number(n == 4 ? BitConverter.Int32BitsToSingle(unchecked((int)U32BE(p))) : BitConverter.Int64BitsToDouble(unchecked((long)U64BE(p))));
            else return;
            Add(key, value);
        }
        private ulong ReadUnsigned(int p, int n)
        {
            ulong value = 0; for (int i = 0; i < n; i++) value = (value << 8) | data[p + i]; return value;
        }
    }
}
