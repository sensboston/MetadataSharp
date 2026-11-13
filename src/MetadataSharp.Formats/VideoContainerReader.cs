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
        private uint movieTimeScale = 1;
        private uint mediaTimeScale = 1;
        private string currentQuickTimeStream = "Xmp.video";
        private double timeCodeScale = 1.0;

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

        private string Number(double value) => value.ToString("G6", CultureInfo.InvariantCulture).Replace('E', 'e');
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
        private string CString(int start, int length, Encoding encoding)
        {
            if (length > limits.MaxValueBytes) throw Limit("A video text value exceeds the value budget.", start);
            ReadOnlySpan<byte> bytes = data.Slice(start, length);
            int zero = bytes.IndexOf((byte)0);
            if (zero >= 0) bytes = bytes[..zero];
            return encoding.GetString(bytes);
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
        // GUIDs are compared in their on-disk little-endian field order.
        private static readonly byte[] AsfVideoMedia = [0xC0,0xEF,0x19,0xBC,0x4D,0x5B,0xCF,0x11,0xA8,0xFD,0,0x80,0x5F,0x5C,0x44,0x2B];
        private static readonly byte[] AsfAudioMedia = [0x40,0x9E,0x69,0xF8,0x4D,0x5B,0xCF,0x11,0xA8,0xFD,0,0x80,0x5F,0x5C,0x44,0x2B];
        private void ReadAsfCodec(int p, int end)
        {
            Need(p, 20, end, "ASF codec list");
            uint count = U32LE(p + 16);
            if (count > limits.MaxEntriesPerDirectory || count > limits.MaxSegments - structures)
                throw Limit("The ASF codec count exceeds its budget.", p);
            p += 20;
            for (uint i = 0; i < count; i++)
            {
                Count(ref structures, p);
                Need(p, 6, end, "ASF codec entry");
                ushort type = U16LE(p);
                ushort nameChars = U16LE(p + 2);
                p += 4;
                int nameBytes = checked(nameChars * 2);
                Need(p, nameBytes + 2, end, "ASF codec name");
                // Match the pinned reader's printed key: it doubles the codec type before testing for 1,
                // so the resulting even value routes every codec-list entry to the audio namespace.
                string prefix = (ushort)(type * 2) == 1 ? "Xmp.video." : "Xmp.audio.";
                if (nameBytes != 0) Add(prefix + "CodecName", Text(p, nameBytes, Encoding.Unicode));
                p += nameBytes;
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
            Need(p, 52, end, "ASF extended stream properties");
            Add("Xmp.video.StartTimecode", U64LE(p).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.EndTimecode", U16LE(p + 8).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.FrameRate", U16LE(p + 50).ToString(CultureInfo.InvariantCulture));
        }

        internal IReadOnlyList<VideoProperty> QuickTime()
        {
            if (data.Length < 12) throw Invalid("The QuickTime header is truncated.", 0);
            Common(true, false);
            WalkAtoms(0, data.Length, 0, string.Empty, "Xmp.video");
            Add("Xmp.video.AspectRatio", Ratio(width, height));
            return Result();
        }

        private void WalkAtoms(int start, int end, int depth, string parent, string streamPrefix)
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
                else if (type == "tkhd") ReadTrackHeader(payload, atomEnd, streamPrefix);
                else if (type == "mdhd") ReadMediaHeader(payload, atomEnd, streamPrefix);
                else if (type == "hdlr") ReadHandler(payload, atomEnd, streamPrefix);
                else if (type == "stsd") ReadSampleDescription(payload, atomEnd, streamPrefix);
                else if (type == "vmhd") ReadVideoHeader(payload, atomEnd);
                else if (type == "stts") ReadTimeToSample(payload, atomEnd, streamPrefix);
                else if (type == "url " && streamPrefix != "Xmp.ignored") Add(streamPrefix + ".URL", CString(payload, atomEnd - payload, Encoding.Latin1));
                else if (type == "urn " && streamPrefix != "Xmp.ignored") Add(streamPrefix + ".URN", CString(payload, atomEnd - payload, Encoding.Latin1));
                else if (type == "dcom") Add("Xmp.video.Compressor", CString(payload, atomEnd - payload, Encoding.Latin1));
                else if (parent == "udta" && ReadUserDataTag(type, payload, atomEnd)) { }
                else if (type == "smhd")
                {
                    Need(payload, 8, atomEnd, "QuickTime sound media header");
                    if (streamPrefix == "Xmp.audio") Add("Xmp.audio.Balance", U16BE(payload + 6).ToString(CultureInfo.InvariantCulture));
                }
                else if (type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "dinf" or "dref" or "edts" or "udta" or "tapt" or "ilst" or "meta" or "moof" or "traf")
                {
                    int prefixBytes = type switch { "meta" => 4, "dref" => 8, _ => 0 };
                    if (atomEnd - payload < prefixBytes) throw Invalid("A QuickTime child-table prefix exceeds its atom.", payload);
                    int childStart = payload + prefixBytes;
                    string childStream = streamPrefix;
                    if (type == "trak")
                    {
                        childStream = FindTrackStream(childStart, atomEnd, depth + 1);
                        if (childStream is "Xmp.audio" or "Xmp.video") currentQuickTimeStream = childStream;
                    }
                    else if (type == "udta") childStream = currentQuickTimeStream;
                    WalkAtoms(childStart, atomEnd, depth + 1, type, childStream);
                }
                p = atomEnd;
            }
        }

        private string FindTrackStream(int start, int end, int depth)
        {
            Depth(depth, start);
            int p = start;
            while (p < end)
            {
                Count(ref structures, p);
                if (end - p < 8) throw Invalid("A QuickTime track atom header is truncated.", p);
                uint size32 = U32BE(p);
                string type = FourCC(p + 4);
                int header = 8;
                ulong size = size32;
                if (size32 == 1)
                {
                    Need(p, 16, end, "extended QuickTime track atom header");
                    size = U64BE(p + 8); header = 16;
                }
                else if (size32 == 0) size = (ulong)(end - p);
                if (size < (ulong)header || size > (ulong)(end - p))
                    throw Invalid("A QuickTime track atom size exceeds its enclosing extent.", p);
                int payload = p + header, atomEnd = p + (int)size;
                if (type == "hdlr")
                {
                    Need(payload, 12, atomEnd, "QuickTime media handler");
                    return FourCC(payload + 8) switch
                    {
                        "soun" => "Xmp.audio",
                        "vide" => "Xmp.video",
                        _ => "Xmp.ignored"
                    };
                }
                if (type == "mdia")
                {
                    string selected = FindTrackStream(payload, atomEnd, depth + 1);
                    if (selected == "Xmp.audio" || selected == "Xmp.video") return selected;
                }
                p = atomEnd;
            }
            return "Xmp.ignored";
        }

        private bool ReadUserDataTag(string type, int p, int end)
        {
            string normalized = type.Length == 4 && type[0] == '\u00A9' ? " " + type[1..] : type;
            string? key = normalized switch
            {
                " swr" => "Xmp.video.SoftwareVersion", " nam" => "Xmp.video.Title", " cmt" => "Xmp.video.Comment",
                " cpy" => "Xmp.video.Copyright", " ART" => "Xmp.video.Artist", " alb" => "Xmp.video.Album",
                " com" => "Xmp.video.Composer", " dir" => "Xmp.video.Director", " gen" => "Xmp.video.Genre",
                " too" => "Xmp.video.Encoder", " trk" => "Xmp.video.Track", " xyz" => "Xmp.video.GPSCoordinates",
                _ => null
            };
            if (key is null) return false;
            Need(p, 4, end, "QuickTime user-data text prefix");
            Add(key, CString(p + 4, end - p - 4, Encoding.Latin1));
            return true;
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
            "qt  " => "Apple QuickTime (.MOV/QT)",
            "isom" => "MP4 Base Media v1 [IS0 14496-12:2003]",
            "iso2" => "MP4 Base Media v2 [ISO 14496-12:2005]",
            "mp41" => "MP4 v1 [ISO 14496-1:ch13]",
            "mp42" => "MP4 v2 [ISO 14496-14]",
            "avc1" => "MP4 Base w/ AVC ext [ISO 14496-12:2005]",
            "M4A " => "Apple iTunes AAC-LC (.M4A) Audio",
            "M4V " => "Apple iTunes Video (.M4V) Video",
            "3gp4" => "3GPP Media (.3GP) Release 4",
            "3gp5" => "3GPP Media (.3GP) Release 5",
            _ => brand
        };
        private static string? VendorName(string vendor) => vendor switch
        {
            "FFMP" => "FFmpeg", "appl" => "Apple", "olym" => "Olympus", "GIC " => "General Imaging Co.",
            "fe20" => "Olympus (fe20)", "pana" => "Panasonic", "KMPI" => "Konica-Minolta", "kdak" => "Kodak",
            "pent" => "Pentax", "NIKO" or "niko" => "Nikon", "leic" => "Leica", "SMI " => "Sorenson Media Inc.",
            "mino" => "Minolta", "sany" => "Sanyo", _ => null
        };

        private void ReadMovieHeader(int p, int end)
        {
            Need(p, 100, end, "QuickTime movie header");
            Add("Xmp.video.MovieHeaderVersion", data[p].ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.DateUTC", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.ModificationDate", U32BE(p + 8).ToString(CultureInfo.InvariantCulture));
            movieTimeScale = Math.Max(1U, U32BE(p + 12));
            Add("Xmp.video.TimeScale", U32BE(p + 12).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.Duration", ((ulong)U32BE(p + 16) * 1000UL / movieTimeScale).ToString(CultureInfo.InvariantCulture));
            Add("Xmp.video.PreferredRate", Number(U16BE(p + 20) + U16BE(p + 22) / 100.0));
            Add("Xmp.video.PreferredVolume", Number((data[p + 24] + data[p + 26] * 0.1) * 100));
            string[] tailKeys = ["PreviewTime", "PreviewDuration", "PosterTime", "SelectionTime", "SelectionDuration", "CurrentTime", "NextTrackID"];
            for (int i = 0; i < tailKeys.Length; i++)
                Add("Xmp.video." + tailKeys[i], U32BE(p + 72 + i * 4).ToString(CultureInfo.InvariantCulture));
        }
        private void ReadTrackHeader(int p, int end, string prefix)
        {
            Need(p, 84, end, "QuickTime track header");
            if (prefix == "Xmp.ignored") return;
            byte version = data[p];
            int baseAt = p + (version == 1 ? 20 : 12);
            Need(baseAt, 72, end, "QuickTime track header fields");
            Add(prefix + ".TrackHeaderVersion", version.ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".TrackCreateDate", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".TrackModifyDate", U32BE(p + 8).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".TrackID", U32BE(baseAt).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".TrackDuration", (U32BE(baseAt + 8) / movieTimeScale).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".TrackLayer", U16BE(baseAt + 20).ToString(CultureInfo.InvariantCulture));
            ushort volume = U16BE(baseAt + 24);
            Add("Xmp.video.TrackVolume", Number(((volume >> 8) + (volume & 0xFF) * 0.1) * 100));
            if (prefix == "Xmp.video")
            {
                uint w = U32BE(baseAt + 64) >> 16, h = U32BE(baseAt + 68) >> 16;
                if (w != 0) { width = (int)w; Add("Xmp.video.Width", w.ToString(CultureInfo.InvariantCulture)); }
                if (h != 0) { height = (int)h; Add("Xmp.video.Height", h.ToString(CultureInfo.InvariantCulture)); }
            }
        }
        private void ReadMediaHeader(int p, int end, string prefix)
        {
            Need(p, 24, end, "QuickTime media header");
            if (prefix == "Xmp.ignored") return;
            Add(prefix + ".MediaHeaderVersion", data[p].ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".MediaCreateDate", U32BE(p + 4).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".MediaModifyDate", U32BE(p + 8).ToString(CultureInfo.InvariantCulture));
            mediaTimeScale = Math.Max(1U, U32BE(p + 12));
            Add(prefix + ".MediaTimeScale", U32BE(p + 12).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".MediaDuration", (U32BE(p + 16) / mediaTimeScale).ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".MediaLangCode", U16BE(p + 20).ToString(CultureInfo.InvariantCulture));
        }
        private void ReadHandler(int p, int end, string prefix)
        {
            Need(p, 20, end, "QuickTime handler");
            if (prefix == "Xmp.ignored") return;
            string handlerClass = FourCC(p + 4);
            string handler = FourCC(p + 8);
            string vendor = FourCC(p + 12);
            string? className = handlerClass switch { "dhlr" => "Data Handler", "mhlr" => "Media Handler", _ => null };
            string? handlerName = handler switch
            {
                "alis" => "Alias Data", "crsm" => "Clock Reference", "hint" => "Hint Track", "ipsm" => "IPMP",
                "m7sm" => "MPEG-7 Stream", "mdir" => "Metadata", "mdta" => "Metadata Tags", "mjsm" => "MPEG-J",
                "ocsm" => "Object Content", "odsm" => "Object Descriptor", "sdsm" => "Scene Description",
                "soun" => "Audio Track", "text" => "Text", "tmcd" => "Time Code", "url " => "URL", "vide" => "Video Track", _ => null
            };
            string? vendorName = vendor switch { "FFMP" => "FFmpeg", "appl" => "Apple", "olym" => "Olympus", "GIC " => "General Imaging Co.", "pana" => "Panasonic", "pent" => "Pentax", "NIKO" or "niko" => "Nikon", "leic" => "Leica", _ => null };
            if (className is not null) Add(prefix + ".HandlerClass", className);
            if (handlerName is not null) Add(prefix + ".HandlerType", handlerName);
            if (vendorName is not null) Add(prefix + ".HandlerVendorID", vendorName);
        }
        private void ReadSampleDescription(int p, int end, string prefix)
        {
            Need(p, 8, end, "QuickTime sample description");
            if (prefix == "Xmp.ignored") return;
            uint count = U32BE(p + 4);
            if (count > limits.MaxEntriesPerDirectory) throw Limit("The QuickTime sample-description count exceeds its budget.", p);
            int q = p + 8;
            for (uint i = 0; i < count && q < end; i++)
            {
                Need(q, 8, end, "QuickTime sample entry");
                uint size = U32BE(q); string codec = FourCC(q + 4);
                if (size < 8 || size > end - q) throw Invalid("A QuickTime sample entry exceeds its atom.", q);
                if (prefix == "Xmp.audio")
                {
                    if (size >= 36)
                    {
                        Add("Xmp.audio.Compressor", codec);
                        Add("Xmp.audio.ChannelType", U16BE(q + 24).ToString(CultureInfo.InvariantCulture));
                        Add("Xmp.audio.BitsPerSample", U16BE(q + 26).ToString(CultureInfo.InvariantCulture));
                        Add("Xmp.audio.SampleRate", Number(U16BE(q + 32) + U16BE(q + 34) / 100.0));
                        string? vendor = VendorName(FourCC(q + 20));
                        if (vendor is not null) Add("Xmp.audio.VendorID", vendor);
                    }
                }
                else
                {
                    Add("Xmp.video.Codec", BrandName(codec));
                    string? vendor = size >= 24 ? VendorName(FourCC(q + 20)) : null;
                    if (vendor is not null) Add("Xmp.video.VendorID", vendor);
                    if (size >= 36)
                    {
                        ushort w = U16BE(q + 32), h = U16BE(q + 34);
                        if (w != 0) { width = w; Add("Xmp.video.SourceImageWidth", w.ToString(CultureInfo.InvariantCulture)); }
                        if (h != 0) { height = h; Add("Xmp.video.SourceImageHeight", h.ToString(CultureInfo.InvariantCulture)); }
                    }
                    if (size >= 86)
                    {
                        Add("Xmp.video.XResolution", Number(U32BE(q + 36) / 65536.0));
                        Add("Xmp.video.YResolution", Number(U32BE(q + 40) / 65536.0));
                        int compressorLength = Math.Min(data[q + 50], (byte)31);
                        Add("Xmp.video.Compressor", Text(q + 51, compressorLength, Encoding.Latin1));
                        Add("Xmp.video.BitDepth", U16BE(q + 82).ToString(CultureInfo.InvariantCulture));
                    }
                }
                q += (int)size;
            }
        }
        private void ReadTimeToSample(int p, int end, string prefix)
        {
            Need(p, 8, end, "QuickTime time-to-sample table");
            uint entries = U32BE(p + 4);
            if (entries > limits.MaxEntriesPerDirectory) throw Limit("The QuickTime sample timing count exceeds its budget.", p);
            Need(p + 8, checked((int)entries * 8), end, "QuickTime sample timing entries");
            ulong samples = 0, ticks = 0;
            int q = p + 8;
            for (uint i = 0; i < entries; i++, q += 8)
            {
                uint count = U32BE(q), delta = U32BE(q + 4);
                samples += count;
                ticks += (ulong)count * delta;
            }
            if (prefix == "Xmp.video" && ticks != 0)
                Add("Xmp.video.FrameRate", Number((double)samples * mediaTimeScale / ticks));
        }
        private void ReadVideoHeader(int p, int end)
        {
            Need(p, 12, end, "QuickTime video media header");
            ushort mode = U16BE(p + 4);
            Add("Xmp.video.GraphicsMode", mode switch
            {
                0 => "srcCopy", 1 => "srcOr", 2 => "srcXor", 3 => "srcBic", 4 => "notSrcCopy", 5 => "notSrcOr",
                6 => "notSrcXor", 7 => "notSrcBic", 8 => "blend", 9 => "transparent", _ => mode.ToString(CultureInfo.InvariantCulture)
            });
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
                    int nextTrackType = id.Id == 0xAE ? FindEbmlTrackType(payload, payloadEnd) : trackType;
                    ParseEbml(payload, payloadEnd, depth + 1, nextTrackType, path);
                }
                else ReadEbmlValue(id.Id, payload, payloadEnd, trackType);
                p = payloadEnd;
                if (size.Unknown) break;
            }
        }
        private int FindEbmlTrackType(int start, int end)
        {
            int p = start;
            while (p < end)
            {
                EbmlHeader id = EbmlNext(p, end, true);
                int sizeAt = p + id.HeaderLength;
                EbmlHeader size = EbmlNext(sizeAt, end, false);
                int payload = sizeAt + size.HeaderLength;
                if (size.Unknown || size.PayloadLength > end - payload)
                    throw Invalid("An EBML TrackEntry child exceeds its parent extent.", p);
                int payloadEnd = payload + size.PayloadLength;
                if (id.Id == 0x83 && size.PayloadLength is >= 1 and <= 8)
                    return (int)Math.Min(ReadUnsigned(payload, size.PayloadLength), int.MaxValue);
                p = payloadEnd;
            }
            return 0;
        }

        private void ReadEbmlValue(ulong id, int p, int end, int trackType)
        {
            int n = end - p; if (n > limits.MaxValueBytes) throw Limit("An EBML value exceeds the value budget.", p);
            string? key = id switch
            {
                0x4286 => "Xmp.video.EBMLVersion", 0x42F7 => "Xmp.video.EBMLReadVersion",
                0x4282 => "Xmp.video.DocType", 0x4287 => "Xmp.video.DocTypeVersion", 0x4285 => "Xmp.video.DocTypeReadVersion",
                0x2AD7B1 => "Xmp.video.TimecodeScale", 0x4489 => "Xmp.video.Duration", 0x4461 => "Xmp.video.DateUTC",
                0x4D80 => "Xmp.video.MuxingApp", 0x5741 => "Xmp.video.WritingApp", 0x7BA9 => "Xmp.video.Title",
                0x83 => null, 0x86 => trackType == 2 ? "Xmp.audio.Compressor" : "Xmp.video.Codec",
                0x23E383 => trackType == 2 ? "Xmp.audio.DefaultDuration" : "Xmp.video.FrameRate",
                0xB0 => "Xmp.video.Width", 0xBA => "Xmp.video.Height", 0x54B0 => "Xmp.video.DisplayWidth",
                0x54BA => "Xmp.video.DisplayHeight", 0x54AA => "Xmp.video.CropBottom", 0x54BB => "Xmp.video.CropTop",
                0x54CC => "Xmp.video.CropLeft", 0x54DD => "Xmp.video.CropRight", 0xB5 => "Xmp.audio.SampleRate",
                0x9F => "Xmp.audio.ChannelType", 0x6264 => "Xmp.audio.BitsPerSample", 0x22B59C => "Xmp.audio.CodecName",
                0x536E => "Xmp.video.TrackName", 0x9A => "Xmp.video.VideoScanTpye", _ => null
            };
            if (key is null) return;
            if (id == 0xB0 && n is >= 1 and <= 8) width = (int)Math.Min(ReadUnsigned(p, n), int.MaxValue);
            if (id == 0xBA && n is >= 1 and <= 8) height = (int)Math.Min(ReadUnsigned(p, n), int.MaxValue);
            if ((n is < 1 or > 8) && id is not (0x86 or 0x4D80 or 0x5741 or 0x7BA9 or 0x22B59C or 0x536E)) return;
            string value;
            if (id is 0x86 or 0x4D80 or 0x5741 or 0x7BA9 or 0x22B59C or 0x536E or 0x4282)
                value = Text(p, n, Encoding.UTF8);
            else if (id == 0x2AD7B1)
            {
                timeCodeScale = ReadUnsigned(p, n) / 1_000_000_000d;
                value = Number(timeCodeScale);
            }
            else if (id == 0x4489 && (n is 4 or 8))
            {
                double duration = n == 4 ? BitConverter.Int32BitsToSingle(unchecked((int)U32BE(p))) : BitConverter.Int64BitsToDouble(unchecked((long)U64BE(p)));
                value = Math.Truncate(duration * timeCodeScale * 1000).ToString(CultureInfo.InvariantCulture);
            }
            else if (id == 0x4461 && n is >= 1 and <= 8)
                value = (ReadUnsigned(p, n) / 1_000_000UL).ToString(CultureInfo.InvariantCulture);
            else if (id == 0x23E383 && n is >= 1 and <= 8)
            {
                ulong duration = ReadUnsigned(p, n);
                value = trackType == 2 ? Number(duration / 1000d) : (duration == 0 ? "Variable Bit Rate" : Number(1_000_000_000d / duration));
            }
            else if (id == 0x9F && n is >= 1 and <= 8) value = EbmlChannelType(ReadUnsigned(p, n));
            else if (id == 0x9A && n is >= 1 and <= 8) value = ReadUnsigned(p, n) switch { 0 => "Progressive", 1 => "Interlaced", _ => ReadUnsigned(p, n).ToString(CultureInfo.InvariantCulture) };
            else if (id == 0xB5 && (n is 4 or 8))
                value = Number(n == 4 ? BitConverter.Int32BitsToSingle(unchecked((int)U32BE(p))) : BitConverter.Int64BitsToDouble(unchecked((long)U64BE(p))));
            else if (n is >= 1 and <= 8) value = ReadUnsigned(p, n).ToString(CultureInfo.InvariantCulture);
            else return;
            if (id == 0x83) return;
            Add(key, value);
        }
        private static string EbmlChannelType(ulong channels) => channels switch
        {
            1 => "Mono", 2 => "Stereo", 5 => "5.1 Surround Sound", 7 => "7.1 Surround Sound",
            _ => channels.ToString(CultureInfo.InvariantCulture)
        };

        private ulong ReadUnsigned(int p, int n)
        {
            ulong value = 0; for (int i = 0; i < n; i++) value = (value << 8) | data[p + i]; return value;
        }
    }
}
