using System.Buffers.Binary;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Core;

/// <summary>
/// A mutable set of addressed EXIF changes: any tag of the primary, Exif, GPS, Interoperability and
/// thumbnail directories, with any TIFF field type and count. The last operation for a key wins.
/// </summary>
/// <remarks>
/// <para>A writer creates the Exif, GPS or Interoperability directory a set tag needs, with its pointer
/// tag, and drops a directory, with its pointer, when the edit removes its last tag. A thumbnail
/// directory is not created: a tag of <see cref="ExifDirectoryKind.Thumbnail"/> is set only in a file
/// that has that directory.</para>
/// <para>The tags that hold the structure of the file are not edited as values, and every method that
/// takes a key refuses them with <see cref="MetadataErrorCode.TagNotEditable"/>; see
/// <see cref="IsEditable"/>.</para>
/// </remarks>
public sealed class ExifEdit
{
    private readonly Dictionary<ExifTagKey, ExifValue?> changes = [];
    private bool removeAll;
    /// <summary>Creates an edit that holds no change.</summary>
    public ExifEdit() { }
    /// <summary>True when the edit holds no change; a save then copies the source.</summary>
    public bool IsEmpty => !removeAll && changes.Count == 0 && thumbnail is null && !removeThumbnail;

    /// <summary>
    /// Whether a tag can be set and removed through an edit. The tags that cannot are, in any
    /// directory: the directory pointers ExifIFD (0x8769), GPSInfo (0x8825), InteroperabilityIFD
    /// (0xA005) and SubIFDs (0x014A); the addresses and lengths of image and thumbnail data
    /// StripOffsets (0x0111), StripByteCounts (0x0117), TileOffsets (0x0144), TileByteCounts (0x0145),
    /// JPEGInterchangeFormat (0x0201) and JPEGInterchangeFormatLength (0x0202); MakerNote (0x927C);
    /// and DNGPrivateData (0xC634), where a DNG file keeps a MakerNote.
    /// </summary>
    public static bool IsEditable(ExifTagKey key) => Enum.IsDefined(key.Directory) && key.TagId is not
        (0x8769 or 0x8825 or 0xA005 or 0x014A or 0x0111 or 0x0117 or 0x0144 or 0x0145 or 0x0201 or 0x0202 or 0x927C or 0xC634);

    /// <summary>
    /// Sets a tag. A tag <see cref="ExifTags"/> lists is held to its definition: the field type is one
    /// of <see cref="ExifTagDefinition.Types"/> and the count one of
    /// <see cref="ExifTagDefinition.Counts"/>, where the definition states them, and an ASCII value
    /// ends with NUL and holds seven-bit characters. A tag the vocabulary does not list is written as
    /// given. <see cref="SetUnchecked"/> writes a listed tag as given too.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value holds no element, the value disagrees with the definition of the tag, the directory
    /// is not one of <see cref="ExifDirectoryKind"/>, or the edit already removes all EXIF.
    /// </exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.TagNotEditable"/> for a tag <see cref="IsEditable"/> excludes.
    /// </exception>
    public void Set(ExifTagKey key, ExifValue value) => Store(key, value, validate: true);

    /// <summary>
    /// Sets a tag to the value as given: no definition is consulted, so the caller answers for the
    /// field type, the count and the content. Everything else is as in
    /// <see cref="Set(ExifTagKey, ExifValue)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value holds no element, the directory is not one of <see cref="ExifDirectoryKind"/>, or the
    /// edit already removes all EXIF.
    /// </exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.TagNotEditable"/> for a tag <see cref="IsEditable"/> excludes.
    /// </exception>
    public void SetUnchecked(ExifTagKey key, ExifValue value) => Store(key, value, validate: false);

    /// <summary>
    /// Sets a tag to an ASCII value: the text and the NUL that ends it. Validated as
    /// <see cref="Set(ExifTagKey, ExifValue)"/> validates.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The text holds a NUL or a character above U+007F, or a failure of
    /// <see cref="Set(ExifTagKey, ExifValue)"/>.
    /// </exception>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.TagNotEditable"/>.</exception>
    public void Set(ExifTagKey key, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] bytes = new byte[text.Length + 1];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '\0' or > '\u007F')
                throw new ArgumentException("An ASCII value holds seven-bit characters and no NUL before its end.", nameof(text));
            bytes[i] = (byte)text[i];
        }
        Store(key, ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Sets a tag to one SHORT; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, ushort value) => Set(key, new ReadOnlySpan<ushort>(in value));

    /// <summary>Sets a tag to one LONG; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, uint value) => Set(key, new ReadOnlySpan<uint>(in value));

    /// <summary>Sets a tag to one RATIONAL; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, UnsignedRational value) => Set(key, new ReadOnlySpan<UnsignedRational>(in value));

    /// <summary>Sets a tag to one SRATIONAL; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, SignedRational value) => Set(key, new ReadOnlySpan<SignedRational>(in value));

    /// <summary>Sets a tag to SHORT values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, ReadOnlySpan<ushort> values)
    {
        byte[] bytes = new byte[Length(values.Length, 2)];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), values[i]);
        Store(key, ExifValue.FromRaw(TiffFieldType.Short, (uint)values.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Sets a tag to LONG values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, ReadOnlySpan<uint> values)
    {
        byte[] bytes = new byte[Length(values.Length, 4)];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * i), values[i]);
        Store(key, ExifValue.FromRaw(TiffFieldType.Long, (uint)values.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Sets a tag to RATIONAL values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, ReadOnlySpan<UnsignedRational> values)
    {
        byte[] bytes = new byte[Length(values.Length, 8)];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 * i), values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 * i + 4), values[i].Denominator);
        }
        Store(key, ExifValue.FromRaw(TiffFieldType.Rational, (uint)values.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Sets a tag to SRATIONAL values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    public void Set(ExifTagKey key, ReadOnlySpan<SignedRational> values)
    {
        byte[] bytes = new byte[Length(values.Length, 8)];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 * i), values[i].Numerator);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 * i + 4), values[i].Denominator);
        }
        Store(key, ExifValue.FromRaw(TiffFieldType.SRational, (uint)values.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Removes a tag. A tag the file does not have is no change.</summary>
    /// <exception cref="ArgumentException">
    /// The directory is not one of <see cref="ExifDirectoryKind"/>, or the edit already removes all EXIF.
    /// </exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.TagNotEditable"/> for a tag <see cref="IsEditable"/> excludes.
    /// </exception>
    public void Remove(ExifTagKey key)
    {
        CheckKey(key);
        changes[key] = null;
    }

    /// <summary>Removes the whole EXIF block.</summary>
    /// <exception cref="ArgumentException">The edit already holds a tag change.</exception>
    public void RemoveAllExif()
    {
        if (changes.Count != 0 || thumbnail is not null || removeThumbnail) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        removeAll = true;
    }

    private byte[]? thumbnail;
    private bool removeThumbnail;

    /// <summary>
    /// Sets the thumbnail of the EXIF block: a JPEG stream in the directory after the primary one
    /// (IFD1), which is created when the block has none, with Compression 6, a resolution of 72 dpi
    /// and the address and length of the stream. A thumbnail the block has is replaced: the other
    /// tags of its directory stay, and the bytes of the old stream are cleared where nothing else
    /// addresses them. A file without an EXIF
    /// block gets one. This is <c>exiv2 -it</c>.
    /// </summary>
    /// <remarks>
    /// Written by the documents of JPEG, PNG and WebP, where the EXIF block is a unit of its own.
    /// The documents of TIFF-based files and of BMFF files refuse it with
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/>: in a TIFF-based file the directory after
    /// the primary one is an image of the file, not a thumbnail of an EXIF block. The stream has
    /// to be one the reader takes for a JPEG (it is checked when the block is written) and no longer
    /// than <see cref="MetadataLimits.MaxThumbnailBytes"/>; with the rest of the block it has to fit
    /// the 64 KiB of a JPEG APP1 segment.
    /// </remarks>
    /// <exception cref="ArgumentException">The bytes do not start as a JPEG stream does, or the edit removes the whole block.</exception>
    public void SetThumbnail(ReadOnlySpan<byte> jpeg)
    {
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new ArgumentException("A thumbnail is a JPEG stream.", nameof(jpeg));
        thumbnail = jpeg.ToArray();
        removeThumbnail = false;
    }

    /// <summary>
    /// Removes the thumbnail of the EXIF block with its directory (IFD1): the primary directory no
    /// longer leads to it, and its bytes are cleared where nothing else addresses them. The block
    /// keeps its length. Nothing happens for a block without a thumbnail directory. This is
    /// <c>exiv2 -dt</c>. Written by the same documents as <see cref="SetThumbnail"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The edit removes the whole block.</exception>
    public void RemoveThumbnail()
    {
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        thumbnail = null;
        removeThumbnail = true;
    }

    private static int Length(int count, int width)
    {
        if (count > Array.MaxLength / width) throw new ArgumentException("The value is longer than an array can be.");
        return count * width;
    }

    private void Store(ExifTagKey key, ExifValue value, bool validate)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckKey(key);
        if (value.Count == 0) throw new ArgumentException("A value holds at least one element.", nameof(value));
        if (validate && ExifTags.Find(key) is ExifTagDefinition definition && Disagreement(definition, value) is string reason)
            throw new ArgumentException($"{definition.Name} (0x{key.TagId:X4}) {reason}. SetUnchecked writes the value as given.", nameof(value));
        changes[key] = value;
    }

    /// <summary>What the definition and the value disagree about, or null when they agree.</summary>
    private static string? Disagreement(ExifTagDefinition definition, ExifValue value)
    {
        if (!definition.Types.IsEmpty && !definition.Types.Contains(value.FieldType))
            return $"takes the field type {string.Join(" or ", definition.Types)}, not {value.FieldType}";
        if (!definition.Counts.IsEmpty && !definition.Counts.Contains(value.Count))
            return $"takes {string.Join(" or ", definition.Counts)} value(s), not {value.Count}";
        if (value.FieldType == TiffFieldType.Ascii)
        {
            ReadOnlySpan<byte> bytes = value.RawBytes;
            if (bytes.Length == 0 || bytes[bytes.Length - 1] != 0) return "is ASCII and has to end with NUL";
            foreach (byte character in bytes)
                if (character >= 0x80) return "is ASCII and holds seven-bit characters";
        }
        return null;
    }

    private void CheckKey(ExifTagKey key)
    {
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        if (!Enum.IsDefined(key.Directory)) throw new ArgumentException("The directory is not one of ExifDirectoryKind.", nameof(key));
        if (!IsEditable(key)) throw NotEditable(key);
    }

    /// <summary>The refusal of a tag <see cref="IsEditable"/> excludes; the writers throw it too.</summary>
    internal static MetadataException NotEditable(ExifTagKey key) => new(MetadataErrorCode.TagNotEditable,
        $"Tag 0x{key.TagId:X4} of the {key.Directory} directory is a pointer, an address or length of image data, or the MakerNote: it is not edited as a value.");

    internal EditSnapshot Snapshot() => new(removeAll, new Dictionary<ExifTagKey, ExifValue?>(changes)) { Thumbnail = thumbnail, ThumbnailRemoved = removeThumbnail };

    internal sealed record EditSnapshot(bool RemoveAll, IReadOnlyDictionary<ExifTagKey, ExifValue?> Changes)
    {
        /// <summary>The JPEG stream to set as the thumbnail, or null.</summary>
        internal byte[]? Thumbnail { get; init; }
        /// <summary>True when the thumbnail directory is to be removed.</summary>
        internal bool ThumbnailRemoved { get; init; }
        internal bool HasThumbnailChange => Thumbnail is not null || ThumbnailRemoved;
        internal bool IsEmpty => !RemoveAll && Changes.Count == 0 && !HasThumbnailChange;
        internal ExifMetadata Apply(TiffReadResult? source, Endianness order, MetadataLimits limits)
        {
            var entries = source?.Metadata.Entries.ToDictionary(e => e.Key, e => e.Value) ?? [];
            foreach (var change in Changes)
            {
                if (change.Value is null) entries.Remove(change.Key);
                else entries[change.Key] = change.Value;
            }
            if (entries.Count > limits.MaxTotalEntries)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited entry count exceeds its budget.");
            long bytes = source?.Metadata.Entries.Sum(e => (long)e.Value.ByteLength) ?? 0;
            foreach (ExifValue value in entries.Values)
            {
                bytes += value.ByteLength;
                if (value.ByteLength > limits.MaxValueBytes || bytes > limits.MaxRetainedMetadataBytes)
                    throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited values exceed the metadata budget.");
            }
            return new ExifMetadata(order, entries.Select(e => new ExifEntry(e.Key, ConvertOrder(e.Value, order))).ToArray());
        }
    }

    internal static ExifValue ConvertOrder(ExifValue value, Endianness order)
    {
        if (value.ByteOrder == order) return value;
        byte[] bytes = value.RawBytes.ToArray();
        int width = value.FieldType is TiffFieldType.Rational or TiffFieldType.SRational ? 4 : ExifValue.GetWidth(value.FieldType);
        if (width > 1) for (int i = 0; i < bytes.Length; i += width) bytes.AsSpan(i, width).Reverse();
        return ExifValue.FromRaw(value.FieldType, value.Count, order, bytes);
    }
}
