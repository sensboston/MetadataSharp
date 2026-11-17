using System.Buffers.Binary;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// Deterministic, project-authored tiny video-container inputs; no media samples or copied metadata code. Formerly
// tools/reference/video_fixtures.py.
internal static class VideoFixtures
{
    private static byte[] Le(long value, int width)
    {
        var bytes = new byte[width];
        for (int i = 0; i < width; i++) bytes[i] = (byte)(value >> (8 * i));
        return bytes;
    }

    private static byte[] Be(long value, int width)
    {
        var bytes = new byte[width];
        for (int i = 0; i < width; i++) bytes[width - 1 - i] = (byte)(value >> (8 * i));
        return bytes;
    }

    private static byte[] Le(ulong value) => BitConverter.GetBytes(value);

    public static byte[] Chunk(byte[] tag, byte[] payload)
    {
        if (tag.Length != 4) throw new PyException("AssertionError", "");
        return [.. tag, .. Le(payload.Length, 4), .. payload, .. (payload.Length & 1) != 0 ? new byte[] { 0 } : []];
    }

    public static byte[] Riff(byte[] form, byte[] body)
    {
        byte[] payload = [.. form, .. body];
        return [.. "RIFF"u8, .. Le(payload.Length, 4), .. payload];
    }

    public static byte[] Atom(byte[] tag, byte[] payload, bool extended = false, bool zero = false)
    {
        if (tag.Length != 4) throw new PyException("AssertionError", "");
        if (zero) return [0, 0, 0, 0, .. tag, .. payload];
        if (extended) return [.. Be(1, 4), .. tag, .. Be(16 + payload.Length, 8), .. payload];
        return [.. Be(8 + payload.Length, 4), .. tag, .. payload];
    }

    public static byte[] VintSize(long value)
    {
        for (int width = 1; width < 9; width++)
        {
            if (value < (1L << (7 * width)) - 1) return Be((1L << (7 * width)) | value, width);
        }
        throw new PyException("ValueError", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static byte[] EbmlElement(byte[] identifier, byte[] payload, bool unknown = false) =>
        [.. identifier, .. unknown ? new byte[] { 0xFF } : VintSize(payload.Length), .. payload];

    public static byte[] AsfObject(byte[] guid, byte[] payload)
    {
        if (guid.Length != 16) throw new PyException("AssertionError", "");
        return [.. guid, .. Le(24 + payload.Length, 8), .. payload];
    }

    private static byte[] Hex(string text) => Convert.FromHexString(text);

    private static byte[] Utf16(string text) => System.Text.Encoding.Unicode.GetBytes(text);

    public static PyDict Fixtures()
    {
        byte[] filePropsGuid = Hex("a1dcab8c47a9cf118ee400c00c205365");
        byte[] contentGuid = Hex("3326b2758e66cf11a6d900aa0062ce6c");
        byte[] streamGuid = Hex("9107dcb7b7a9cf118ee600c00c205365");
        byte[] codecListGuid = Hex("4052d1861d31d011a3a400a0c90348f6");
        byte[] fileId = Hex("00112233445566778899aabbccddeeff");
        byte[] fileProps = [.. fileId, .. Le(123456, 8), .. Le(0, 8), .. Le(12, 8), .. Le(50000000, 8), .. Le(0, 8), .. Le(3000, 8),
                            .. Le(2, 4), .. Le(1024, 4), .. Le(2048, 4), .. Le(800000, 4)];
        // Exiv2's ASF helper drops the final UTF-16 code unit, so include a standard terminator to preserve the
        // authored title in both readers.
        byte[] title = Utf16("Video fixture\0");
        byte[] content = [.. Le(title.Length, 2), 0, 0, 0, 0, 0, 0, 0, 0, .. title];
        byte[] codecName = Utf16("msmpeg4v3\0");
        byte[] codecList = [.. new byte[16], .. Le(1, 4), .. Le(1, 2), .. Le(codecName.Length / 2, 2), .. codecName, .. Le(0, 2), .. Le(4, 2), .. "MP43"u8];
        byte[] streamData = [.. Hex("c0ef19bc4d5bcf11a8fd00805f5c442b"), .. Hex("3526b2758e66cf11a6d900aa0062ce6c"), .. Le(777, 8), .. Le(12, 4),
                             .. Le(0, 4), .. new byte[6], .. new byte[4], .. Le(320, 4), .. Le(240, 4)];
        byte[] children = [.. AsfObject(filePropsGuid, fileProps), .. AsfObject(contentGuid, content), .. AsfObject(codecListGuid, codecList),
                           .. AsfObject(streamGuid, streamData)];
        byte[] asfHeaderGuid = Hex("3026b2758e66cf11a6d900aa0062ce6c");
        byte[] dataGuid = Hex("3626b2758e66cf11a6d900aa0062ce6c");
        byte[] asfPayload = [.. Le(4, 4), 0x01, 0x02, .. children];
        byte[] asfHeader = AsfObject(asfHeaderGuid, asfPayload);
        // ASF requires a Data Object after its Header Object. Even a zero-packet data object makes the container
        // structurally complete for Exiv2.
        byte[] asfData = AsfObject(dataGuid, [.. fileId, .. new byte[8], .. new byte[8], 0x01, 0x01]);
        byte[] asf = [.. asfHeader, .. asfData];

        static byte[] MatroskaFile(byte[] docType, byte[] title)
        {
            // Exiv2 0.28.9 reads integer payloads as 64-bit regardless of EBML width; use valid eight-byte integers
            // so the oracle and the spec value agree.
            static byte[] Integer(long value) => Be(value, 8);
            var duration = new byte[8];
            BinaryPrimitives.WriteDoubleBigEndian(duration, 1200.0);
            byte[] ebmlPayload = [.. EbmlElement(Hex("4286"), Integer(1)), .. EbmlElement(Hex("42f7"), Integer(1)), .. EbmlElement(Hex("4282"), docType),
                                  .. EbmlElement(Hex("4287"), Integer(4)), .. EbmlElement(Hex("4285"), Integer(2))];
            byte[] ebmlHeader = EbmlElement(Hex("1a45dfa3"), ebmlPayload);
            byte[] info = [.. EbmlElement(Hex("2ad7b1"), Integer(1_000_000)), .. EbmlElement(Hex("4489"), duration), .. EbmlElement(Hex("7ba9"), title)];
            byte[] videoTrack = [.. EbmlElement(Hex("83"), [0x01]), .. EbmlElement(Hex("86"), "V_VP8"u8.ToArray()),
                                 .. EbmlElement(Hex("e0"), [.. EbmlElement(Hex("b0"), Integer(320)), .. EbmlElement(Hex("ba"), Integer(240))])];
            byte[] tracks = EbmlElement(Hex("1654ae6b"), EbmlElement(Hex("ae"), videoTrack));
            byte[] segment = EbmlElement(Hex("18538067"), [.. EbmlElement(Hex("1549a966"), info), .. tracks]);
            return [.. ebmlHeader, .. segment];
        }

        byte[] webm = MatroskaFile("webm"u8.ToArray(), "Fixture WebM"u8.ToArray());
        byte[] matroska = MatroskaFile("matroska"u8.ToArray(), "Fixture Matroska"u8.ToArray());

        byte[] ftyp = Atom("ftyp"u8.ToArray(), [.. "isom"u8, .. Be(512, 4), .. "isommp42"u8]);
        var mvhd = new byte[100];
        mvhd[0] = 0;
        Be(1000, 4).CopyTo(mvhd, 12);
        Be(12000, 4).CopyTo(mvhd, 16);
        Be(1 << 16, 4).CopyTo(mvhd, 20);
        Be(1 << 8, 2).CopyTo(mvhd, 24);
        byte[] mvhdAtom = Atom("mvhd"u8.ToArray(), mvhd);
        byte[] xmpUuid = Hex("be7acfcb97a942e89c71999491e3afac");
        byte[] xmpPacket = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray();
        byte[] qt = [.. ftyp, .. Atom("moov"u8.ToArray(), [.. mvhdAtom, .. Atom("free"u8.ToArray(), "authored"u8.ToArray())]),
                     .. Atom("uuid"u8.ToArray(), [.. xmpUuid, .. xmpPacket])];

        byte[] fmt = [.. Le(1, 2), .. Le(2, 2), .. Le(44100, 4), .. Le(176400, 4), .. Le(4, 2), .. Le(16, 2)];
        byte[] wav = Riff("WAVE"u8.ToArray(), [.. Chunk("fmt "u8.ToArray(), fmt), .. Chunk("data"u8.ToArray(), new byte[32])]);

        byte[] avih = [.. Le(33333, 4), .. Le(100000, 4), .. Le(0, 4), .. Le(0, 4), .. Le(30, 4), .. Le(0, 4), .. Le(1, 4), .. Le(0, 4),
                       .. Le(320, 4), .. Le(240, 4), .. new byte[16]];
        byte[] strh = [.. "vids"u8, .. "DIB "u8, .. new byte[8], .. Le(1, 4), .. Le(30, 4), .. new byte[4], .. Le(30, 4), .. new byte[4], .. Le(30, 4),
                       .. new byte[4], .. Le(0xFFFFFFFF, 4), .. new byte[8]]; // RECT rcFrame is four signed 16-bit values.
        // Exiv2 0.28.9 reads the two pixels-per-meter DWORDs as QWORDs, so provide the consumed eight-byte extension
        // in the authored DIB header.
        byte[] strf = [.. Le(48, 4), .. Le(320, 4), .. Le(240, 4), .. Le(1, 2), .. Le(24, 2), .. "DIB "u8, .. Le(320 * 240 * 3, 4), .. new byte[24]];

        byte[] hdrl = [.. Chunk("avih"u8.ToArray(), avih), .. Chunk("LIST"u8.ToArray(), [.. "strl"u8, .. Chunk("strh"u8.ToArray(), strh), .. Chunk("strf"u8.ToArray(), strf)])];
        byte[] avi = Riff("AVI "u8.ToArray(), [.. Chunk("LIST"u8.ToArray(), [.. "hdrl"u8, .. hdrl]), .. Chunk("LIST"u8.ToArray(), "movi"u8.ToArray())]);

        return PyDict.Of(
            ("authored-asf.asf", asf),
            ("authored-webm.webm", webm),
            ("authored-matroska.mkv", matroska),
            ("authored-quicktime.mp4", qt),
            ("authored-wave.wav", wav),
            ("authored-avi.avi", avi),
            ("malformed-asf.asf", (byte[])[.. asf[..16], .. Le(0xFFFFFFFFFFFFFFFFUL), .. asf[24..]]),
            ("malformed-ebml.webm", (byte[])[0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]),
            ("malformed-quicktime.mov", (byte[])[.. Atom("ftyp"u8.ToArray(), [.. "isom"u8, 0, 0, 0, 0]), 0, 0, 0, 1, .. "moov"u8, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]),
            ("malformed-riff.wav", (byte[])[.. "RIFF"u8, 0xFF, 0xFF, 0xFF, 0xFF, .. "WAVEfmt "u8, 0xFF, 0xFF, 0xFF, 0xFF]));
    }

    public static int Run(string[] argv)
    {
        if (argv.Length != 2 || argv[0] is not ("generate" or "verify")) throw SystemExit.WithMessage("usage: video-fixtures generate|verify OUTPUT_DIRECTORY");
        string mode = argv[0], root = argv[1];
        var contents = new PyDict();
        foreach (var (key, value) in Fixtures())
        {
            string name = (string)key;
            string relative = name.StartsWith("malformed-", StringComparison.Ordinal) ? "malformed/" + name["malformed-".Length..] : "valid/" + name;
            contents[relative] = value;
        }
        if (mode == "generate")
        {
            foreach (var (name, data) in contents)
            {
                string path = PyPath.Join(root, (string)name);
                PyFile.MakeDirs(PyPath.Parent(path));
                PyFile.WriteBytes(path, (byte[])data!);
            }
        }
        foreach (var (name, data) in contents)
        {
            string path = PyPath.Join(root, (string)name);
            if (!File.Exists(path) || !PyFile.ReadBytes(path).AsSpan().SequenceEqual((byte[])data!)) throw SystemExit.WithMessage($"fixture differs: {name}");
        }
        var files = new PyDict();
        foreach (var (name, data) in contents.OrderBy(pair => (string)pair.Key, Comparer<string>.Create(Py.CompareStrings)))
            files[name] = PyDict.Of(("bytes", (long)((byte[])data!).Length), ("sha256", JpegOwnedFixtures.Sha256((byte[])data!)));
        var manifest = PyDict.Of(("provenance", "Authored by tools/MetadataSharp.Harness/Reference/VideoFixtures.cs; no media, copied files, or copied parser code."), ("files", files));
        string manifestPath = PyPath.Join(root, "manifest.json");
        if (mode == "generate")
        {
            PyFile.MakeDirs(root);
            PyFile.WriteText(manifestPath, PyJson.Dumps(manifest, indent: 2) + "\n");
        }
        else if (!File.Exists(manifestPath) || !Py.Eq(PyJson.Loads(PyFile.ReadText(manifestPath)), manifest))
        {
            throw SystemExit.WithMessage("video fixture manifest differs");
        }
        Out.Print($"video fixtures verified: {contents.Count} files");
        return 0;
    }
}
