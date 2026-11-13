using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Eps;

/// <summary>
/// Metadata of an Encapsulated PostScript file. EPS has a place for metadata in the document itself:
/// the reference tool (Exiv2) reads and writes XMP embedded as a DSC comment block and nothing else
/// of it - no EXIF, no IPTC and no comment - and this reader and writer do the same. The PostScript
/// program is never interpreted or executed.
/// </summary>
/// <remarks>
/// <para>
/// Two framings are read. A plain EPS file begins with one of the three first lines the reference
/// accepts (<c>%!PS-Adobe-3.0 EPSF-3.0</c>, the same with a trailing space, and
/// <c>%!PS-Adobe-3.1 EPSF-3.0</c>). A DOS EPS file begins with the four bytes <c>C5 D0 D3 C6</c> and
/// a thirty-byte header that places its PostScript section and, at most one of, its WMF and TIFF
/// sections; the PostScript section is then read as a plain EPS file.
/// </para>
/// <para>
/// XMP is looked for in the way the reference looks for it: a line that begins with one of the eight
/// packet headers it accepts, up to a line that begins with one of its four trailers, and the
/// <c>?&gt;</c> that closes the trailer. The packet is the bytes from the header to the end of the
/// trailer. A trailer marked <c>r</c> states a packet its writer means to be read only; the reference
/// refuses such a file and this reader refuses it too.
/// </para>
/// <para>
/// The layout the reference writes is read for the same reason: <c>%ADO_ContainsXMP: MainFirst</c>
/// announces a packet, <c>%begin_xml_packet: N</c> states its length, and the markers around it say
/// where it lies. A <c>%ADO_ContainsXMP:</c> line that says there is no packet is read as a file
/// without XMP.
/// </para>
/// </remarks>
public sealed class EpsMetadata
{
    /// <summary>The three first lines the reference accepts, in its order.</summary>
    private static readonly string[] FirstLines =
    [
        "%!PS-Adobe-3.0 EPSF-3.0",
        "%!PS-Adobe-3.0 EPSF-3.0 ",
        "%!PS-Adobe-3.1 EPSF-3.0",
    ];

    /// <summary>
    /// The packet headers the reference accepts: the <c>begin</c> attribute in any of its four
    /// spellings, its deprecated empty form among them, and the <c>id</c> attribute in either quote.
    /// </summary>
    private static readonly string[] PacketHeaders =
    [
        "<?xpacket begin=\"\u00ef\u00bb\u00bf\" id=\"W5M0MpCehiHzreSzNTczkc9d\"",
        "<?xpacket begin=\"\u00ef\u00bb\u00bf\" id='W5M0MpCehiHzreSzNTczkc9d'",
        "<?xpacket begin='\u00ef\u00bb\u00bf' id=\"W5M0MpCehiHzreSzNTczkc9d\"",
        "<?xpacket begin='\u00ef\u00bb\u00bf' id='W5M0MpCehiHzreSzNTczkc9d'",
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"",
        "<?xpacket begin=\"\" id='W5M0MpCehiHzreSzNTczkc9d'",
        "<?xpacket begin='' id=\"W5M0MpCehiHzreSzNTczkc9d\"",
        "<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'",
    ];

    /// <summary>The trailers the reference accepts, and whether each states a read-only packet.</summary>
    private static readonly (string Text, bool ReadOnly)[] PacketTrailers =
    [
        ("<?xpacket end=\"r\"", true),
        ("<?xpacket end='r'", true),
        ("<?xpacket end=\"w\"", false),
        ("<?xpacket end='w'", false),
    ];

    /// <summary>The bytes that close every trailer.</summary>
    private const string TrailerEnd = "?>";

    private EpsMetadata(bool dosEps, long postScriptOffset, long postScriptLength, long? wmfOffset, long wmfLength,
        long? tiffOffset, long tiffLength, XmpPacket? xmp, byte[]? packet)
    {
        IsDosEps = dosEps;
        PostScriptOffset = postScriptOffset;
        PostScriptLength = postScriptLength;
        WmfOffset = wmfOffset;
        WmfLength = wmfLength;
        TiffOffset = tiffOffset;
        TiffLength = tiffLength;
        Xmp = xmp;
        // The conditional is written out with its nullable type: left to itself it takes the type of
        // the second branch, and the null of the first becomes an empty memory that has a value.
        Packet = packet is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(packet);
    }

    /// <summary>Whether the file is framed by the DOS EPS header.</summary>
    public bool IsDosEps { get; }

    /// <summary>Where the PostScript section begins, from the start of the file.</summary>
    public long PostScriptOffset { get; }

    /// <summary>The length of the PostScript section: what its header states, or the rest of the file.</summary>
    public long PostScriptLength { get; }

    /// <summary>Where the WMF section begins, or null when the file has none.</summary>
    public long? WmfOffset { get; }

    /// <summary>The length of the WMF section: zero when the file has none.</summary>
    public long WmfLength { get; }

    /// <summary>Where the TIFF section begins, or null when the file has none.</summary>
    public long? TiffOffset { get; }

    /// <summary>The length of the TIFF section: zero when the file has none.</summary>
    public long TiffLength { get; }

    /// <summary>The XMP packet the file embeds, or null when it embeds none.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>The bytes of the packet as the file holds them, or null when it holds none.</summary>
    public ReadOnlyMemory<byte>? Packet { get; }

    /// <summary>The type the reference tool gives an EPS file.</summary>
    public const string MimeType = "application/postscript";

    /// <summary>
    /// Whether the bytes begin as an EPS file does, in either framing. A plain file begins with a first
    /// line the reference accepts; the three of them all start with <c>%!PS-Adobe-3.</c>, so that is
    /// what the first bytes are asked, and the line as a whole is checked by
    /// the reader of the whole file when the whole file is at hand.
    /// </summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == 0xC5 && header[1] == 0xD0 && header[2] == 0xD3 && header[3] == 0xC6) return true;
        return header.StartsWith("%!PS-Adobe-3."u8);
    }

    /// <summary>Reads the XMP an EPS file carries.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="limits">The budgets, or null for the defaults of a whole file.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidEps"/> when the file is not one of the two framings, a
    /// section it places leaves the file, the packet trailer states a read-only packet, or the packet
    /// does not parse;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded.
    /// </exception>
    public static EpsMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) => Read(data, limits, null);

    internal static EpsMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits, KindRefusals? refusals)
    {
        limits?.Validate();
        if (limits is not null && data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EPS file exceeds the input budget.", 0);
        bool dos = data.Length >= 4 && data[0] == 0xC5 && data[1] == 0xD0 && data[2] == 0xD3 && data[3] == 0xC6;
        long start = 0;
        long end = data.Length;
        long? wmfOffset = null, tiffOffset = null;
        long wmfLength = 0, tiffLength = 0;
        if (dos)
        {
            if (data.Length < 30) throw Bad("The DOS EPS header is not complete.", 0);
            // Every number is a 32-bit word, little endian: the offset and the length of the
            // PostScript section, then those of the WMF and of the TIFF section.
            long psOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            long psLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
            long wOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            long wLength = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
            long tOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
            long tLength = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
            if (psOffset < 30 || psLength > data.Length - psOffset) throw Bad("The PostScript section of the DOS EPS file leaves the file.", 4);
            if (wLength != 0 && (wOffset < 30 || wLength > data.Length - wOffset)) throw Bad("The WMF section of the DOS EPS file leaves the file.", 12);
            if (tLength != 0 && (tOffset < 30 || tLength > data.Length - tOffset)) throw Bad("The TIFF section of the DOS EPS file leaves the file.", 20);
            start = psOffset;
            end = psOffset + psLength;
            if (wLength != 0) wmfOffset = wOffset;
            if (tLength != 0) tiffOffset = tOffset;
            wmfLength = wLength;
            tiffLength = tLength;
        }
        ReadOnlySpan<byte> postScript = data.Slice((int)start, (int)(end - start));
        if (!StartsAsEps(postScript)) throw Bad("The file does not begin with a first line an EPS file has.", start);

        byte[]? packet = null;
        // The reference searches for a packet only when the comment block announces one with
        // %ADO_ContainsXMP: MainFirst. Without that line, or with NoMain, it does not search at all,
        // and a packet elsewhere in the document is not the metadata of the file to it.
        if (Announcement(postScript) == AnnouncementKind.Packet &&
            FindPacket(postScript, out int packetStart, out int packetLength, out bool readOnly))
        {
            // A trailer marked r states a packet the writer means to be read only; the reference
            // refuses such a file, and so does this reader rather than report a packet it may not keep.
            if (readOnly) throw Bad("The XMP trailer states a read-only packet.", start + packetStart + packetLength);
            packet = postScript.Slice(packetStart, packetLength).ToArray();
        }
        XmpPacket? xmp = null;
        if (packet is not null)
        {
            try { xmp = XmpPacket.Parse(packet); }
            catch (MetadataException refusal) when (refusals is not null) { refusals.Xmp = refusal; }
        }
        return new EpsMetadata(dos, start, end - start, wmfOffset, wmfLength, tiffOffset, tiffLength, xmp, packet);
    }

    /// <summary>
    /// Writes an XMP edit to a plain or DOS-framed EPS file, returning a verified copy. Packet
    /// replacement changes only the packet range; when a DOS wrapper is present its section offsets
    /// and lengths are adjusted and its WMF and TIFF preview bytes are retained exactly.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidEps"/> when framing or section bounds are invalid;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when a read-only or uneditable packet
    /// cannot be safely changed; <see cref="MetadataErrorCode.UnsafeRelocation"/> when the planned
    /// result does not read back as the requested edit.
    /// </exception>
    public static byte[] WriteXmp(ReadOnlySpan<byte> data, XmpEdit edit, MetadataLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        limits?.Validate();
        if (limits is not null && data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EPS file exceeds the input budget.", 0);
        byte[] source = data.ToArray();
        EpsMetadata before = Read(source, limits);
        ValidateSections(before);
        if (edit.IsEmpty) return source;

        long psStart = before.PostScriptOffset;
        ReadOnlySpan<byte> ps = source.AsSpan((int)psStart, (int)before.PostScriptLength);
        AnnouncementKind announcement = Announcement(ps);
        int packetStart = 0, packetLength = 0;
        bool hasPacket = announcement == AnnouncementKind.Packet && FindPacket(ps, out packetStart, out packetLength, out _);
        XmpPacket? desired = edit.RemovesAll ? null : (before.Xmp ?? XmpPacket.CreateEmpty()).Apply(edit);
        if (!hasPacket && desired is null) return source;

        byte[] replacement;
        if (desired is not null)
        {
            replacement = new byte[desired.ByteLength];
            desired.CopyRawTo(replacement);
        }
        else replacement = [];

        byte[] newPs;
        if (hasPacket && desired is not null && HasExiv2Embedding(ps, packetStart))
        {
            newPs = Splice(ps, packetStart, packetLength, replacement);
        }
        else if (desired is not null)
        {
            ReadOnlySpan<byte> clean = ps;
            if (hasPacket) clean = Splice(ps, packetStart, packetLength, []);
            newPs = InsertCanonicalPacket(clean, replacement);
        }
        else if (hasPacket)
        {
            newPs = Splice(ps, packetStart, packetLength, []);
        }
        else newPs = ps.ToArray();

        if (desired is null) newPs = SetAnnouncement(newPs, AnnouncementKind.NoPacket);
        else newPs = SetAnnouncement(newPs, AnnouncementKind.Packet);
        newPs = MovePageBeforeImplicitContent(newPs);

        long delta = newPs.Length - ps.Length;
        byte[] output = Reframe(source, before, newPs, delta);
        if (limits is not null && output.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EPS output exceeds the input budget.");
        EpsMetadata after = Read(output, limits);
        if (!SameEntries(desired, after.Xmp))
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The EPS XMP edit did not read back as planned.");
        VerifyPreview(source, output, before.WmfOffset, before.WmfLength, before, delta, tiff: false);
        VerifyPreview(source, output, before.TiffOffset, before.TiffLength, before, delta, tiff: true);
        return output;
    }

    private static bool HasExiv2Embedding(ReadOnlySpan<byte> postScript, int packetStart) =>
        postScript[..packetStart].LastIndexOf("%Exiv2BeginXMP: Before"u8) >= 0;

    private static byte[] InsertCanonicalPacket(ReadOnlySpan<byte> original, byte[] packet)
    {
        (int announcementAt, _) = DscInsertPoints(original);
        byte[] postScript = SetAnnouncement(original.ToArray(), AnnouncementKind.Packet, announcementAt);
        byte[] ending = LineEnding(original, EndOfLine(original, 0));

        int endSetup = FindLine(postScript, "%%EndPageSetup");
        int insertSetupAt;
        bool addSetupMarkers = endSetup < 0;
        if (addSetupMarkers)
        {
            insertSetupAt = FindAnyLine(postScript, "%%PageTrailer", "%%Trailer", "%%EOF");
            if (insertSetupAt < 0) insertSetupAt = postScript.Length;
        }
        else insertSetupAt = endSetup;

        byte[] block = PdfMarkPacket(packet, ending);
        if (addSetupMarkers)
        {
            byte[] wrapped = [.. Encoding.ASCII.GetBytes("%%BeginPageSetup"), .. ending,
                .. block, .. Encoding.ASCII.GetBytes("%%EndPageSetup"), .. ending];
            postScript = Splice(postScript, insertSetupAt, 0, wrapped);
        }
        else
        {
            int beginSetup = FindLine(postScript, "%%BeginPageSetup");
            byte[] prefix = beginSetup < 0 ? [.. Encoding.ASCII.GetBytes("%%BeginPageSetup"), .. ending] : [];
            postScript = Splice(postScript, insertSetupAt, 0, [.. prefix, .. block]);
        }

        int pageTrailer = FindLine(postScript, "%%PageTrailer");
        if (pageTrailer < 0)
        {
            int trailerAt = FindAnyLine(postScript, "%%Trailer", "%%EOF");
            if (trailerAt < 0) trailerAt = postScript.Length;
            postScript = Splice(postScript, trailerAt, 0, [.. Encoding.ASCII.GetBytes("%%PageTrailer"), .. ending]);
            pageTrailer = trailerAt;
        }
        int trailerEnd = AfterLine(postScript, EndOfLine(postScript, pageTrailer));
        byte[] close = Lines(ending,
            "%Exiv2BeginXMP: After %%PageTrailer",
            "[/EMC Exiv2_pdfmark",
            "[/NamespacePop Exiv2_pdfmark",
            "%Exiv2EndXMP");
        return Splice(postScript, trailerEnd, 0, close);
    }

    // The reference treats ordinary percent comments after EndComments as page content. Move an
    // existing explicit page marker ahead of such content so its XMP scanner does not see two pages.
    private static byte[] MovePageBeforeImplicitContent(byte[] postScript)
    {
        int at = 0;
        while (at < postScript.Length)
        {
            int end = EndOfLine(postScript, at);
            if (postScript.AsSpan(at, end - at).SequenceEqual("%%EndComments"u8))
            {
                at = AfterLine(postScript, end);
                break;
            }
            at = AfterLine(postScript, end);
        }

        bool inProlog = false;
        int implicitContentAt = -1;
        int pageAt = -1;
        while (at < postScript.Length)
        {
            int end = EndOfLine(postScript, at);
            ReadOnlySpan<byte> line = postScript.AsSpan(at, end - at);
            if (line.StartsWith("%%Page:"u8))
            {
                pageAt = at;
                break;
            }
            if (line.SequenceEqual("%%BeginDefaults"u8) || line.SequenceEqual("%%BeginProlog"u8) ||
                line.SequenceEqual("%%BeginSetup"u8)) inProlog = true;
            else if (line.SequenceEqual("%%EndDefaults"u8) || line.SequenceEqual("%%EndProlog"u8) ||
                     line.SequenceEqual("%%EndSetup"u8)) inProlog = false;
            else if (!inProlog && implicitContentAt < 0 && !OnlyWhitespace(line) &&
                     !line.StartsWith("%%"u8)) implicitContentAt = at;
            at = AfterLine(postScript, end);
        }

        if (implicitContentAt < 0 || pageAt < 0 || implicitContentAt > pageAt) return postScript;
        int pageEnd = EndOfLine(postScript, pageAt);
        int pageAfter = AfterLine(postScript, pageEnd);
        byte[] pageLine = postScript.AsSpan(pageAt, pageAfter - pageAt).ToArray();
        byte[] withoutPage = Splice(postScript, pageAt, pageAfter - pageAt, []);
        return Splice(withoutPage, implicitContentAt, 0, pageLine);
    }

    private static bool OnlyWhitespace(ReadOnlySpan<byte> line)
    {
        foreach (byte value in line)
            if (value != (byte)' ' && value != (byte)'\t' && value != (byte)'\r' && value != (byte)'\n') return false;
        return true;
    }

    private static byte[] PdfMarkPacket(byte[] packet, byte[] ending)
    {
        string[] before =
        [
            "%Exiv2BeginXMP: Before %%EndPageSetup",
            "/currentdistillerparams where",
            "{pop currentdistillerparams /CoreDistVersion get 5000 lt} {true} ifelse",
            "{userdict /Exiv2_pdfmark /cleartomark load put",
            "    userdict /Exiv2_metafile_pdfmark {flushfile cleartomark} bind put}",
            "{userdict /Exiv2_pdfmark /pdfmark load put",
            "    userdict /Exiv2_metafile_pdfmark {/PUT pdfmark} bind put} ifelse",
            "[/NamespacePush Exiv2_pdfmark",
            "[/_objdef {Exiv2_metadata_stream} /type /stream /OBJ Exiv2_pdfmark",
            "[{Exiv2_metadata_stream} 2 dict begin",
            "    /Type /Metadata def /Subtype /XML def currentdict end /PUT Exiv2_pdfmark",
            "[{Exiv2_metadata_stream}",
            "    currentfile 0 (% &&end XMP packet marker&&)",
            "    /SubFileDecode filter Exiv2_metafile_pdfmark",
        ];
        using var result = new MemoryStream();
        foreach (string line in before) { result.Write(Encoding.ASCII.GetBytes(line)); result.Write(ending); }
        result.Write(packet);
        result.Write(ending);
        foreach (string line in new[]
        {
            "% &&end XMP packet marker&&",
            "[/Document 1 dict begin",
            "    /Metadata {Exiv2_metadata_stream} def currentdict end /BDC Exiv2_pdfmark",
            "%Exiv2EndXMP",
        }) { result.Write(Encoding.ASCII.GetBytes(line)); result.Write(ending); }
        return result.ToArray();
    }

    private static byte[] Lines(byte[] ending, params string[] lines)
    {
        using var result = new MemoryStream();
        foreach (string line in lines) { result.Write(Encoding.ASCII.GetBytes(line)); result.Write(ending); }
        return result.ToArray();
    }

    private static int FindLine(ReadOnlySpan<byte> data, string line) => FindAnyLine(data, line);

    private static int FindAnyLine(ReadOnlySpan<byte> data, params string[] lines)
    {
        int at = 0;
        while (at < data.Length)
        {
            int end = EndOfLine(data, at);
            foreach (string expected in lines)
                if (data[at..end].SequenceEqual(Encoding.ASCII.GetBytes(expected))) return at;
            at = AfterLine(data, end);
        }
        return -1;
    }

    private static void ValidateSections(EpsMetadata metadata)
    {
        if (!metadata.IsDosEps) return;
        (long Start, long End, string Name)[] sections =
        [
            (metadata.PostScriptOffset, metadata.PostScriptOffset + metadata.PostScriptLength, "PostScript"),
            (metadata.WmfOffset ?? 0, (metadata.WmfOffset ?? 0) + metadata.WmfLength, "WMF"),
            (metadata.TiffOffset ?? 0, (metadata.TiffOffset ?? 0) + metadata.TiffLength, "TIFF"),
        ];
        for (int i = 0; i < sections.Length; i++)
        for (int j = i + 1; j < sections.Length; j++)
        {
            if (sections[i].End > sections[i].Start && sections[j].End > sections[j].Start &&
                sections[i].Start < sections[j].End && sections[j].Start < sections[i].End)
                throw Bad("The " + sections[i].Name + " and " + sections[j].Name + " sections overlap.", sections[j].Start);
        }
    }

    private static byte[] Reframe(byte[] source, EpsMetadata before, byte[] newPs, long delta)
    {
        int start = checked((int)before.PostScriptOffset);
        int oldLength = checked((int)before.PostScriptLength);
        byte[] output = Splice(source, start, oldLength, newPs);
        if (!before.IsDosEps) return output;
        long psLength = before.PostScriptLength + delta;
        if (psLength < 0 || psLength > uint.MaxValue) throw Bad("The rewritten PostScript section does not fit the DOS EPS header.", 8);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), (uint)psLength);
        AdjustSectionOffset(output, 12, before.WmfOffset, before.WmfLength, before, delta);
        AdjustSectionOffset(output, 20, before.TiffOffset, before.TiffLength, before, delta);
        return output;
    }

    private static void AdjustSectionOffset(byte[] output, int field, long? offset, long length, EpsMetadata before, long delta)
    {
        if (offset is null || length == 0) return;
        long psEnd = before.PostScriptOffset + before.PostScriptLength;
        if (offset.Value < psEnd && offset.Value + length > before.PostScriptOffset)
            throw Bad("A DOS EPS preview overlaps the PostScript section.", field);
        long adjusted = offset.Value >= psEnd ? offset.Value + delta : offset.Value;
        if (adjusted < 30 || adjusted > uint.MaxValue) throw Bad("A rewritten DOS EPS section offset does not fit the header.", field);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(field), (uint)adjusted);
    }

    private static void VerifyPreview(byte[] source, byte[] output, long? offset, long length, EpsMetadata before, long delta, bool tiff)
    {
        if (offset is null || length == 0) return;
        long psEnd = before.PostScriptOffset + before.PostScriptLength;
        if (offset.Value < psEnd && offset.Value + length > before.PostScriptOffset)
            throw Bad("A DOS EPS preview overlaps the PostScript section.", offset.Value);
        long target = before.IsDosEps && offset.Value >= psEnd ? offset.Value + delta : offset.Value;
        if (offset.Value < 0 || target < 0 || length > source.Length - offset.Value || length > output.Length - target ||
            !source.AsSpan((int)offset.Value, (int)length).SequenceEqual(output.AsSpan((int)target, (int)length)))
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                tiff ? "The TIFF preview changed during EPS rewriting." : "The WMF preview changed during EPS rewriting.");
    }

    private static bool SameEntries(XmpPacket? expected, XmpPacket? actual)
    {
        if (expected is null) return actual is null;
        if (actual is null || expected.Entries.Count != actual.Entries.Count) return false;
        for (int i = 0; i < expected.Entries.Count; i++)
        {
            XmpEntry left = expected.Entries[i], right = actual.Entries[i];
            if (left.Path != right.Path || left.NamespaceUri != right.NamespaceUri || left.Kind != right.Kind ||
                left.HasChildEntries != right.HasChildEntries || !left.Values.SequenceEqual(right.Values) ||
                !left.Languages.SequenceEqual(right.Languages)) return false;
        }
        return true;
    }

    private static (int AnnouncementAt, int PacketAt) DscInsertPoints(ReadOnlySpan<byte> postScript)
    {
        int at = AfterLine(postScript, EndOfLine(postScript, 0));
        while (at < postScript.Length)
        {
            int end = EndOfLine(postScript, at);
            ReadOnlySpan<byte> line = postScript[at..end];
            if (line.SequenceEqual("%%EndComments"u8)) return (at, AfterLine(postScript, end));
            bool comment = line.Length >= 2 && line[0] == (byte)'%' && line[1] >= 0x21 && line[1] <= 0x7E;
            if (!comment || line.SequenceEqual("%%EOF"u8)) return (at, at);
            at = AfterLine(postScript, end);
        }
        return (postScript.Length, postScript.Length);
    }

    private static byte[] SetAnnouncement(byte[] postScript, AnnouncementKind value, int? insertionAt = null)
    {
        string target = value == AnnouncementKind.Packet ? "%ADO_ContainsXMP: MainFirst" : "%ADO_ContainsXMP: NoMain";
        int at = 0;
        while (at < postScript.Length)
        {
            int end = EndOfLine(postScript, at);
            ReadOnlySpan<byte> line = postScript.AsSpan(at, end - at);
            if (line.StartsWith("%ADO_ContainsXMP:"u8))
            {
                int colon = line.IndexOf((byte)':');
                int valueAt = colon + 1;
                while (valueAt < line.Length && line[valueAt] == (byte)' ') valueAt++;
                int valueEnd = line.Length;
                string valueText = value == AnnouncementKind.Packet ? "MainFirst" : "NoMain";
                byte[] replacement = Encoding.ASCII.GetBytes(valueText);
                return Splice(postScript, at + valueAt, valueEnd - valueAt, replacement);
            }
            if (line.SequenceEqual("%%EndComments"u8) || line.SequenceEqual("%%EOF"u8) ||
                line.Length == 0 || line[0] != (byte)'%') break;
            at = AfterLine(postScript, end);
        }
        if (value != AnnouncementKind.Packet) return postScript;
        int firstEnd = EndOfLine(postScript, 0);
        int insertion = insertionAt ?? AfterLine(postScript, firstEnd);
        return Splice(postScript, insertion, 0, [.. Encoding.ASCII.GetBytes(target), .. LineEnding(postScript, firstEnd)]);
    }

    private static byte[] LineEnding(ReadOnlySpan<byte> data, int lineEnd) =>
        lineEnd < data.Length && data[lineEnd] == (byte)'\r' && lineEnd + 1 < data.Length && data[lineEnd + 1] == (byte)'\n'
            ? [0x0D, 0x0A]
            : lineEnd < data.Length ? [data[lineEnd]] : [0x0A];

    private static byte[] Splice(ReadOnlySpan<byte> source, int start, int remove, ReadOnlySpan<byte> inserted)
    {
        byte[] result = new byte[checked(source.Length - remove + inserted.Length)];
        source[..start].CopyTo(result);
        inserted.CopyTo(result.AsSpan(start));
        source[(start + remove)..].CopyTo(result.AsSpan(start + inserted.Length));
        return result;
    }

    /// <summary>What the file says about a packet in its header comments.</summary>
    private enum AnnouncementKind
    {
        /// <summary>No line of the header speaks of a packet, so no packet is looked for.</summary>
        None,
        /// <summary>A <c>%ADO_ContainsXMP: MainFirst</c> line says a packet is there.</summary>
        Packet,
        /// <summary>A <c>%ADO_ContainsXMP: NoMain</c> line says there is no packet.</summary>
        NoPacket,
    }

    /// <summary>
    /// What the comment block says about a packet, read the way the reference reads it: only the
    /// first <c>%ADO_ContainsXMP:</c> line before the end of the comments counts, the two comments
    /// it accepts in each of their two spellings are taken, and any other value is a refusal.
    /// </summary>
    private static AnnouncementKind Announcement(ReadOnlySpan<byte> postScript)
    {
        int at = 0;
        while (at < postScript.Length)
        {
            int lineStart = at;
            int lineEnd = EndOfLine(postScript, at);
            ReadOnlySpan<byte> line = postScript[lineStart..lineEnd];
            // The comment block ends at %%EndComments, at %%EOF, or at the first line that is not a
            // DSC comment at all: the reference takes an implicit end of comments there too.
            bool comment = line.Length >= 2 && line[0] == (byte)'%' && line[1] >= 0x21 && line[1] <= 0x7E;
            if (!comment || line.SequenceEqual("%%EndComments"u8) || line.SequenceEqual("%%EOF"u8)) return AnnouncementKind.None;
            if (line.StartsWith("%ADO_ContainsXMP:"u8))
            {
                string text = Encoding.Latin1.GetString(line);
                return text switch
                {
                    "%ADO_ContainsXMP: MainFirst" or "%ADO_ContainsXMP:MainFirst" => AnnouncementKind.Packet,
                    "%ADO_ContainsXMP: NoMain" or "%ADO_ContainsXMP:NoMain" => AnnouncementKind.NoPacket,
                    _ => throw Bad("The line " + text + " does not say whether the file holds a packet.", lineStart),
                };
            }
            at = AfterLine(postScript, lineEnd);
        }
        return AnnouncementKind.None;
    }

    /// <summary>
    /// Finds the first packet of the document, scanning its bytes as the reference scans them: the
    /// first header anywhere in it, then the first trailer after that header, then the <c>?&gt;</c>
    /// that closes the trailer. A header with no trailer, or a trailer that is not closed, is a
    /// refusal; the bytes from the header to the end of the trailer are the packet.
    /// </summary>
    private static bool FindPacket(ReadOnlySpan<byte> postScript, out int start, out int length, out bool readOnly)
    {
        start = 0;
        length = 0;
        readOnly = false;
        for (int at = 0; at < postScript.Length; at++)
        {
            if (postScript[at] != (byte)'\0' && postScript[at] != (byte)'<') continue;
            string? header = Match(postScript, at, PacketHeaders);
            if (header is null) continue;
            int afterHeader = at + header.Length;
            for (int trailerAt = afterHeader; trailerAt < postScript.Length; trailerAt++)
            {
                if (postScript[trailerAt] != (byte)'\0' && postScript[trailerAt] != (byte)'<') continue;
                for (int t = 0; t < PacketTrailers.Length; t++)
                {
                    string trailer = PacketTrailers[t].Text;
                    if (!postScript[trailerAt..].StartsWith(Encoding.Latin1.GetBytes(trailer))) continue;
                    // A trailer marked r states a packet its writer means to be read only. The
                    // reference refuses such a file rather than report a packet it may not keep.
                    if (PacketTrailers[t].ReadOnly) throw Bad("The XMP trailer states a read-only packet.", trailerAt);
                    int close = IndexOf(postScript[(trailerAt + trailer.Length)..], "?>"u8);
                    if (close < 0) throw Bad("The XMP trailer of the file is not closed.", trailerAt);
                    start = at;
                    length = trailerAt + trailer.Length + close + 2 - at;
                    return true;
                }
            }
            throw Bad("The file has a packet header and no packet trailer.", at);
        }
        return false;
    }

    /// <summary>Where <paramref name="needle"/> first stands in <paramref name="data"/>, or -1.</summary>
    private static int IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle) =>
        data.IndexOf(needle);

    /// <summary>
    /// The header that begins at <paramref name="at"/>, or null when none does. The bytes of a header
    /// are read as ISO-8859-1, which is the one-byte-per-character reading the BOM of the <c>begin</c>
    /// attribute needs: as ASCII those three bytes become question marks and no header ever matches.
    /// </summary>
    private static string? Match(ReadOnlySpan<byte> data, int at, string[] headers)
    {
        foreach (string text in headers)
        {
            if (data[at..].StartsWith(Encoding.Latin1.GetBytes(text))) return text;
        }
        return null;
    }

    /// <summary>
    /// The first line as a whole: one of the three the reference accepts and nothing after it but its
    /// line ending. The reference compares the line, so a longer first line is not an EPS file to it.
    /// </summary>
    private static bool StartsAsEps(ReadOnlySpan<byte> postScript)
    {
        foreach (string line in FirstLines)
        {
            // The line is ISO-8859-1 throughout, including its one non-ASCII spelling with a trailing
            // space, so the length of the string is the length of the bytes it stands for.
            byte[] bytes = Encoding.Latin1.GetBytes(line);
            if (!postScript.StartsWith(bytes)) continue;
            if (postScript.Length == bytes.Length || postScript[bytes.Length] is (byte)'\r' or (byte)'\n') return true;
        }
        return false;
    }

    /// <summary>The end of the line that starts at <paramref name="at"/>: where its line ending begins, or the end of the data.</summary>
    private static int EndOfLine(ReadOnlySpan<byte> data, int at)
    {
        while (at < data.Length && data[at] != (byte)'\n' && data[at] != (byte)'\r') at++;
        return at;
    }

    /// <summary>Where the line after the one that ends at <paramref name="lineEnd"/> begins, for either ending.</summary>
    private static int AfterLine(ReadOnlySpan<byte> data, int lineEnd)
    {
        if (lineEnd >= data.Length) return data.Length;
        int at = lineEnd + 1;
        if (data[lineEnd] == (byte)'\r' && at < data.Length && data[at] == (byte)'\n') at++;
        return at;
    }

    private static MetadataException Bad(string message, long offset) => new(MetadataErrorCode.InvalidEps, message, offset);
}
