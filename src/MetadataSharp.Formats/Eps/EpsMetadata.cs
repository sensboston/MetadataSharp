using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Eps;

/// <summary>
/// Read-only metadata of an Encapsulated PostScript file. EPS has a place for metadata in the
/// document itself: the reference tool (Exiv2) reads XMP that is embedded as a DSC comment block and
/// nothing else of it - no EXIF, no IPTC and no comment - and this reader does the same. The
/// PostScript program is never interpreted or executed.
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
        Packet = packet;
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
    /// what the first bytes are asked, and the line as a whole is checked by <see cref="Read"/> when the
    /// whole file is at hand.
    /// </summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[..4].SequenceEqual([0xC5, 0xD0, 0xD3, 0xC6])) return true;
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
        bool dos = data.Length >= 4 && data[..4].SequenceEqual([0xC5, 0xD0, 0xD3, 0xC6]);
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
                    if (!postScript[trailerAt..].StartsWith(Encoding.ASCII.GetBytes(trailer))) continue;
                    // A trailer marked r states a packet its writer means to be read only. The
                    // reference refuses such a file rather than report a packet it may not keep.
                    if (PacketTrailers[t].ReadOnly) throw Bad("The XMP trailer states a read-only packet.", trailerAt);
                    int close = postScript[(trailerAt + trailer.Length)..].IndexOf("?>"u8);
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

    /// <summary>The header that begins at <paramref name="at"/>, or null when none does.</summary>
    private static string? Match(ReadOnlySpan<byte> data, int at, string[] headers) =>
        headers.FirstOrDefault(text => data[at..].StartsWith(Encoding.ASCII.GetBytes(text)));

    /// <summary>
    /// The first line as a whole: one of the three the reference accepts and nothing after it but its
    /// line ending. The reference compares the line, so a longer first line is not an EPS file to it.
    /// </summary>
    private static bool StartsAsEps(ReadOnlySpan<byte> postScript) =>
        FirstLines.Any(line => postScript.StartsWith(Encoding.ASCII.GetBytes(line)) &&
            (postScript.Length == line.Length || postScript[line.Length] is (byte)'\r' or (byte)'\n'));

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
