using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Core.Sync;
using MetadataSharp.Formats.Png;

namespace MetadataSharp.Formats.Pgf;

/// <summary>
/// Metadata of a PGF file. The file starts with the three bytes <c>PGF</c>, a version byte
/// and the length of its header as a 32-bit word; after the sixteen bytes of the header structure -
/// and the colour table, for an indexed image - the rest of the header region up to the length the
/// word gives is <em>one whole image file</em> that carries the metadata, and the image data of the
/// PGF file follows it. The reference tool (Exiv2) reads that embedded file with its own readers and
/// takes its EXIF, IPTC and XMP; this reader hands the bytes to the readers of this library, so a PGF
/// file reports what its carrier reports.
/// </summary>
/// <remarks>
/// <para>
/// The region is not decoded as a picture here: each part is what the reader of that container reads
/// of it, and a part the carrier lacks is absent. A carrier of a kind no reader of this library knows
/// is a refusal of the whole region, which is what the reference reports as well.
/// </para>
/// <para>
/// The declared size is the pair of 32-bit words at offsets 8 and 12 of the file, the width then the
/// height, as the reference reads them. The colour table follows the structure when byte 20 of the
/// file, the colour mode of the structure, is 2.
/// </para>
/// </remarks>
public sealed class PgfMetadata
{
    /// <summary>Where the header structure ends: the four bytes before it and its own sixteen.</summary>
    private const int StructureEnd = 24;

    /// <summary>The bytes of the colour table an indexed image puts after the structure.</summary>
    private const int ColourTableBytes = 256 * 3;

    /// <summary>Where the colour mode of the structure stands, counted from the start of the file.</summary>
    private const int ColourModeOffset = 20;

    /// <summary>The colour mode of an image that keeps a colour table.</summary>
    private const byte IndexedColourMode = 2;

    private PgfMetadata(uint width, uint height, long imageOffset, ExifMetadata? exif, IptcPacket? iptc, XmpPacket? xmp)
    {
        Width = width;
        Height = height;
        ImageOffset = imageOffset;
        Exif = exif;
        Iptc = iptc;
        Xmp = xmp;
    }

    /// <summary>The width the PGF header states, in pixels.</summary>
    public uint Width { get; }

    /// <summary>The height the PGF header states, in pixels.</summary>
    public uint Height { get; }

    /// <summary>Where the image data of the PGF file starts, which is where the metadata region ends.</summary>
    public long ImageOffset { get; }

    /// <summary>The EXIF of the embedded image, with its MakerNote, or null when the image has none.</summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The IPTC of the embedded image, or null when it has none.</summary>
    public IptcPacket? Iptc { get; }

    /// <summary>The XMP of the embedded image, or null when it has none.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>The type the reference tool gives a PGF file.</summary>
    public const string MimeType = "image/pgf";

    /// <summary>Whether the bytes begin as a PGF file does.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header) => header.Length >= 3 && header[..3].SequenceEqual("PGF"u8);

    /// <summary>Reads the metadata a PGF file carries.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="limits">The budgets, or null for the defaults of a whole file.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidPgf"/> when the file does not begin as a PGF file, its
    /// header does not lie inside it, or the region it places holds no container this library reads;
    /// the refusal of the carrier's own reader, as it raised it, when the region holds a file that
    /// reader refused; <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded.
    /// </exception>
    public static PgfMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null)
    {
        limits?.Validate();
        if (!HasSignature(data)) throw Bad("The file does not begin with the signature of a PGF file.", 0);
        if (data.Length < StructureEnd) throw Bad("The header of the PGF file is not complete.", data.Length);
        long headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (headerSize == 0) throw Bad("The PGF file declares a header of no length.", 4);
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        // The structure states the colour mode in its thirteenth byte, at offset 20 of the file; an
        // indexed image puts its colour table after the structure, inside the header region.
        int structureBytes = data[ColourModeOffset] == IndexedColourMode ? StructureEnd + ColourTableBytes : StructureEnd;
        long regionStart = structureBytes;
        if (headerSize + 8 < regionStart) throw Bad("The header of the PGF file is shorter than its own structure.", 4);
        long regionLength = headerSize + 8 - regionStart;
        if (regionLength > data.Length - regionStart) throw Bad("The header of the PGF file is longer than the file.", 4);
        // A header that ends where its structure ends holds no image, and then the file has no
        // metadata at all - the reference returns from its read without one rather than refusing.
        if (regionLength == 0) return new PgfMetadata(width, height, regionStart, null, null, null);
        ReadOnlySpan<byte> region = data.Slice((int)regionStart, (int)regionLength);
        if (limits is not null && region.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The embedded image of the PGF file exceeds the input budget.", regionStart);

        // The region is one file of its own, and what the reader of its container gives is the answer.
        // A refusal of the region is passed on as it was raised: it names the kind of the carrier, which
        // is more use to a caller than a refusal of the PGF file saying only that its region failed.
        if (ImageMetadata.Detect(region) == MetadataFormat.Unknown)
            throw Bad("The header of the PGF file does not hold a container this library reads.", regionStart);
        ImageMetadata embedded = ImageMetadata.Read(region, limits);
        // The snapshot a reader of the region refuses records the refusal rather than raising it, so
        // the refusal is raised here, as the carrier's own reader raised it: a caller of a PGF file is
        // told what its carrier failed of, which is what the reference reports of the same file.
        foreach (MetadataReadError error in embedded.Errors)
        {
            if (error.Family is MetadataFamily.Exif or MetadataFamily.IptcIim or MetadataFamily.Xmp)
                throw error.Exception;
        }
        return new PgfMetadata(width, height, regionStart + regionLength, embedded.Exif, embedded.Iptc, embedded.Xmp);
    }

    /// <summary>
    /// Writes metadata edits through the PNG image held in the PGF header region. The existing PNG
    /// writer plans and verifies its output; this method then updates the PGF header length, copies
    /// the PGF image-data suffix unchanged, and reads the complete result back before returning it.
    /// </summary>
    /// <param name="data">The complete PGF file.</param>
    /// <param name="edits">EXIF, IPTC and XMP edits to apply to the embedded PNG.</param>
    /// <param name="comment">When <paramref name="replaceComment"/> is true, the embedded PNG comment to write, or null to remove.</param>
    /// <param name="replaceComment">Whether to apply <paramref name="comment"/>.</param>
    /// <param name="limits">The budgets, or null for the defaults.</param>
    public static byte[] Write(ReadOnlySpan<byte> data, SyncEdits edits, string? comment = null,
        bool replaceComment = false, MetadataLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(edits);
        limits?.Validate();
        if (limits is not null && data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The PGF file exceeds the input budget.", 0);
        byte[] source = data.ToArray();
        PgfMetadata before = Read(source, limits);
        if (edits.IsEmpty && !replaceComment) return source;

        int regionStart = source[ColourModeOffset] == IndexedColourMode ? StructureEnd + ColourTableBytes : StructureEnd;
        int regionEnd = checked((int)before.ImageOffset);
        if (regionEnd == regionStart)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The PGF header region has no image to edit.");
        byte[] originalRegion = source.AsSpan(regionStart, regionEnd - regionStart).ToArray();
        if (ImageMetadata.Detect(originalRegion) != MetadataFormat.Png)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "PGF writes are supported when the header region contains a PNG image.");

        byte[] changedRegion;
        using (var input = new MemoryStream(originalRegion, writable: false))
        using (PngMetadataDocument document = PngMetadataDocument.Open(input,
            limits is null ? null : new PngReadOptions { Limits = limits }))
        {
            using var changed = new MemoryStream();
            document.SaveAll(changed, edits);
            changedRegion = changed.ToArray();
        }
        if (replaceComment)
        {
            // Exiv2 reads a PNG comment from Description, while the public PNG comment API uses Comment.
            using var input = new MemoryStream(changedRegion, writable: false);
            using PngMetadataDocument document = PngMetadataDocument.Open(input,
                limits is null ? null : new PngReadOptions { Limits = limits });
            using var changed = new MemoryStream();
            document.SaveReferenceComment(changed, comment);
            changedRegion = changed.ToArray();
        }

        long newHeaderSize = (long)regionStart + changedRegion.Length - 8;
        long newLength = (long)source.Length - originalRegion.Length + changedRegion.Length;
        if (newHeaderSize < 0 || newHeaderSize > uint.MaxValue || newLength > int.MaxValue ||
            limits is not null && newLength > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The rewritten PGF file exceeds its size budget.");
        byte[] output = new byte[(int)newLength];
        source.AsSpan(0, regionStart).CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)newHeaderSize);
        changedRegion.CopyTo(output, regionStart);
        source.AsSpan(regionEnd).CopyTo(output.AsSpan(regionStart + changedRegion.Length));

        PgfMetadata after = Read(output, limits);
        if (before.Width != after.Width || before.Height != after.Height ||
            !source.AsSpan(regionEnd).SequenceEqual(output.AsSpan(regionStart + changedRegion.Length)))
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The PGF rewrite changed image data or header dimensions.");
        VerifyEdits(before, after, edits, output, comment, replaceComment, limits);
        return output;
    }

    private static void VerifyEdits(PgfMetadata before, PgfMetadata after, SyncEdits edits, byte[] output,
        string? comment, bool replaceComment, MetadataLimits? limits)
    {
        ExifEdit.EditSnapshot exif = edits.Exif.Snapshot();
        if (exif.RemoveAll && after.Exif is not null)
            throw Unsafe("The PGF rewrite retained EXIF after whole-block removal.");
        foreach ((ExifTagKey key, ExifValue? value) in exif.Changes)
        {
            ExifEntry? entry = null;
            bool found = after.Exif is ExifMetadata actualExif && actualExif.TryGetEntry(key, out entry);
            if (value is null ? found : !found || !SameValue(value, entry!.Value))
                throw Unsafe("The PGF EXIF edit did not read back as planned.");
        }

        XmpPacket? expectedXmp = edits.Xmp.IsEmpty ? before.Xmp :
            (before.Xmp ?? XmpPacket.CreateEmpty()).Apply(edits.Xmp);
        if (!SamePacket(expectedXmp, after.Xmp))
            throw Unsafe("The PGF XMP edit did not read back as planned.");

        IptcPacket? expectedIptc = edits.Iptc.IsEmpty ? before.Iptc :
            (before.Iptc ?? IptcPacket.CreateEmpty()).Apply(edits.Iptc);
        if (!SamePacket(expectedIptc, after.Iptc))
            throw Unsafe("The PGF IPTC edit did not read back as planned.");

        if (replaceComment)
        {
            IReadOnlyList<string> comments = Comments.Read(output, limits);
            if (comment is null ? comments.Count != 0 : comments.Count != 1 || comments[0] != comment)
                throw Unsafe("The PGF comment edit did not read back as planned.");
        }
    }

    private static bool SameValue(ExifValue expected, ExifValue actual) =>
        expected.FieldType == actual.FieldType && expected.Count == actual.Count && expected.RawBytes.SequenceEqual(actual.RawBytes);

    private static bool SamePacket(XmpPacket? expected, XmpPacket? actual)
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

    private static bool SamePacket(IptcPacket? expected, IptcPacket? actual)
    {
        if (expected is null) return actual is null;
        if (actual is null || expected.Datasets.Count != actual.Datasets.Count) return false;
        for (int i = 0; i < expected.Datasets.Count; i++)
            if (expected.Datasets[i].Key != actual.Datasets[i].Key ||
                !expected.Datasets[i].Value.Span.SequenceEqual(actual.Datasets[i].Value.Span)) return false;
        return true;
    }

    private static MetadataException Unsafe(string message) => new(MetadataErrorCode.UnsafeRelocation, message);

    private static MetadataException Bad(string message, long offset) => new(MetadataErrorCode.InvalidPgf, message, offset);
}
