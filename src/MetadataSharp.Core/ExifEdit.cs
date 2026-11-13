using System.Buffers.Binary;
using MetadataSharp.Core.MakerNotes;
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
    private readonly List<MakerNoteChange> makerNote = [];
    private MakerNoteCopyPayload? makerNoteCopy;
    private bool removeAll;
    /// <summary>Creates an edit that holds no change.</summary>
    public ExifEdit() { }
    /// <summary>True when the edit holds no change; a save then copies the source.</summary>
    public bool IsEmpty => !removeAll && changes.Count == 0 && thumbnail is null && !removeThumbnail && makerNote.Count == 0 && makerNoteCopy is null;

    /// <summary>
    /// Whether a tag can be set and removed through an edit. The tags that cannot are, in any
    /// directory: the directory pointers ExifIFD (0x8769), GPSInfo (0x8825), InteroperabilityIFD
    /// (0xA005) and SubIFDs (0x014A); the addresses and lengths of image and thumbnail data
    /// StripOffsets (0x0111), StripByteCounts (0x0117), TileOffsets (0x0144), TileByteCounts (0x0145),
    /// JPEGInterchangeFormat (0x0201) and JPEGInterchangeFormatLength (0x0202); MakerNote (0x927C);
    /// and DNGPrivateData (0xC634), where a DNG file keeps a MakerNote.
    /// </summary>
    /// <param name="key">The directory and tag ID to test.</param>
    /// <returns><see langword="true"/> if the tag can be changed by an edit; otherwise, <see langword="false"/>.</returns>
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
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="value">The raw value to store for the tag.</param>
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
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="value">The raw value to store without checking its tag definition.</param>
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
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="text">Seven-bit text without an embedded NUL; the stored ASCII value is terminated with NUL.</param>
    public void Set(ExifTagKey key, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsSevenBitText(text))
            throw new ArgumentException("An ASCII value holds seven-bit characters and no NUL before its end.", nameof(text));
        // The array is filled by one call, not by a loop of this method. With the loop here, a long
        // text set early in a process reached the validation as a value without bytes on .NET 8
        // (seen in CI on three systems, never on .NET 10); the test ALongTextIsSetAsItIs covers it.
        byte[] bytes = new byte[text.Length + 1];
        int written = System.Text.Encoding.ASCII.GetBytes(text, 0, text.Length, bytes, 0);
        if (written != text.Length) throw new InvalidOperationException("The ASCII value was not encoded whole.");
        Store(key, ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    private static bool IsSevenBitText(string text)
    {
        foreach (char character in text)
            if (character is '\0' or > '\u007F') return false;
        return true;
    }

    /// <summary>Sets a tag to one SHORT; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="value">The SHORT value to store.</param>
    public void Set(ExifTagKey key, ushort value) => Set(key, new ReadOnlySpan<ushort>(in value));

    /// <summary>Sets a tag to one LONG; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="value">The LONG value to store.</param>
    public void Set(ExifTagKey key, uint value) => Set(key, new ReadOnlySpan<uint>(in value));

    /// <summary>Sets a tag to one RATIONAL; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="value">The unsigned rational value to store.</param>
    public void Set(ExifTagKey key, UnsignedRational value) => Set(key, new ReadOnlySpan<UnsignedRational>(in value));

    /// <summary>Sets a tag to one SRATIONAL; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="value">The signed rational value to store.</param>
    public void Set(ExifTagKey key, SignedRational value) => Set(key, new ReadOnlySpan<SignedRational>(in value));

    /// <summary>Sets a tag to SHORT values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="values">The SHORT values to store in order.</param>
    public void Set(ExifTagKey key, ReadOnlySpan<ushort> values)
    {
        byte[] bytes = new byte[Length(values.Length, 2)];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), values[i]);
        Store(key, ExifValue.FromRaw(TiffFieldType.Short, (uint)values.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Sets a tag to LONG values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="values">The LONG values to store in order.</param>
    public void Set(ExifTagKey key, ReadOnlySpan<uint> values)
    {
        byte[] bytes = new byte[Length(values.Length, 4)];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * i), values[i]);
        Store(key, ExifValue.FromRaw(TiffFieldType.Long, (uint)values.Length, Endianness.LittleEndian, bytes), validate: true);
    }

    /// <summary>Sets a tag to RATIONAL values; validated as <see cref="Set(ExifTagKey, ExifValue)"/> validates.</summary>
    /// <inheritdoc cref="Set(ExifTagKey, ExifValue)" path="/exception"/>
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="values">The unsigned rational values to store in order.</param>
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
    /// <param name="key">The directory and tag ID to set.</param>
    /// <param name="values">The signed rational values to store in order.</param>
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
    /// <param name="key">The directory and tag ID to remove.</param>
    public void Remove(ExifTagKey key)
    {
        CheckKey(key);
        changes[key] = null;
    }

    /// <summary>Removes the whole EXIF block.</summary>
    /// <exception cref="ArgumentException">The edit already holds a tag change.</exception>
    public void RemoveAllExif()
    {
        if (changes.Count != 0 || thumbnail is not null || removeThumbnail || makerNote.Count != 0 || makerNoteCopy is not null) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        removeAll = true;
    }

    private byte[]? thumbnail;
    private bool removeThumbnail;

    /// <summary>
    /// Sets the thumbnail of the EXIF block: a JPEG stream in the directory after the primary one
    /// (IFD1), which is created when the block has none, with Compression 6, a resolution of 72 dpi
    /// and the address and length of the stream. A thumbnail the block has is replaced: the other
    /// tags of its directory stay, and the bytes of the old stream are cleared where nothing else
    /// addresses them. A file without an EXIF block gets one. This is <c>exiv2 -it</c>.
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
    /// <param name="jpeg">The JPEG-encoded thumbnail bytes to store.</param>
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

    // Check multiplication before allocating so the byte count cannot overflow or exceed array limits.
    private static int Length(int count, int width)
    {
        if (count > Array.MaxLength / width) throw new ArgumentException("The value is longer than an array can be.");
        return count * width;
    }

    // Centralize key, value and optional vocabulary checks before replacing the pending change for a tag.
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

    /// <summary>Refuses edits after whole-block removal and keys whose values are structural addresses.</summary>
    private void CheckKey(ExifTagKey key)
    {
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        if (!Enum.IsDefined(key.Directory)) throw new ArgumentException("The directory is not one of ExifDirectoryKind.", nameof(key));
        if (!IsEditable(key)) throw NotEditable(key);
    }

    /// <summary>The refusal of a tag <see cref="IsEditable"/> excludes; the writers throw it too.</summary>
    internal static MetadataException NotEditable(ExifTagKey key) => new(MetadataErrorCode.TagNotEditable,
        $"Tag 0x{key.TagId:X4} of the {key.Directory} directory is a pointer, an address or length of image data, or the MakerNote: it is not edited as a value.");

    internal EditSnapshot Snapshot() => new(removeAll, new Dictionary<ExifTagKey, ExifValue?>(changes)) { Thumbnail = thumbnail, ThumbnailRemoved = removeThumbnail, MakerNote = makerNote.ToArray(), MakerNoteCopy = makerNoteCopy };

    /// <summary>One change of a tag of the MakerNote: a value, or text to read in the type the tag has in the file, or neither to remove the tag.</summary>
    internal sealed record MakerNoteChange(string Key, ExifValue? Value, string? Text, bool Unchecked = false);

    // ---- Keys as text, and the tags of the MakerNote ------------------------------------------------------

    /// <summary>
    /// True for a key of a tag of a MakerNote this library decodes, as Exiv2 names it:
    /// <c>Exif.Canon.OwnerName</c>, <c>Exif.Nikon3.ISOSpeed</c>, and for a field of an array or a tag
    /// of a sub-directory the group of that array, <c>Exif.CanonCs.Quality</c>,
    /// <c>Exif.NikonLd3.FocalLength</c>, <c>Exif.OlympusCs.WhiteBalance</c>. The groups start with the
    /// name of the maker; whether the file has the tag is known when the edit is written.
    /// </summary>
    /// <param name="key">The dotted EXIF key to test.</param>
    /// <returns><see langword="true"/> if the key names a supported MakerNote tag; otherwise, <see langword="false"/>.</returns>
    public static bool IsMakerNoteKey(string? key) => MakerNotes.MakerNoteKeys.IsMakerNoteKey(key);

    /// <summary>
    /// Sets a tag by its key. A key of a standard directory (<c>Exif.Image.Artist</c>,
    /// <c>Exif.Photo.ISOSpeedRatings</c>) is <see cref="Set(ExifTagKey, ExifValue)"/>. A key of the
    /// MakerNote (<see cref="IsMakerNoteKey"/>) changes a tag, a field of an array or a tag of a
    /// sub-directory of the MakerNote the file has.
    /// </summary>
    /// <remarks>
    /// <para>The MakerNote is changed where it is and never moves. The key has to name something the
    /// note of the file holds: a tag that is not there is not added. The value has the field type the
    /// tag has in the file; a field of an array also has its count. A value no longer than the one
    /// stored is written in its place. A longer one is appended in the EXIF block of a JPEG, PNG or
    /// WebP file, where the layout of the note allows it: at the end of the block for a note whose
    /// offsets count from the TIFF header, at the end of the note for one whose offsets count from the
    /// note, and then only when the note is the last thing in the block; otherwise it is refused. An
    /// array that is stored enciphered (Nikon ShotInfo, ColorBalance and LensData; several Sony
    /// arrays) is deciphered, changed and enciphered again with the key it had.</para>
    /// <para>Not changed as values: a tag that addresses data or a directory and the length that goes
    /// with it (a preview, a sub-directory), and the serial number and shutter count of a Nikon note,
    /// which are the key of its cipher. A tag this library has no name for (its key ends in
    /// <c>0x</c> and four digits) of the type LONG, SLONG or UNDEFINED may be such an address without
    /// this library knowing: it is changed only with <paramref name="allowUnnamed"/>, as
    /// <see cref="SetUnchecked"/> writes a standard tag as given.</para>
    /// <para>The note is read again after the change and before anything is written: every tag and
    /// field that was not named has to read byte for byte as before, and the named ones as set.</para>
    /// <para>What the documents refuse the edit with:
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a file without a MakerNote this library
    /// decodes, a key the note does not hold, a value of another field type, a value that grows where
    /// it cannot be appended (always in a TIFF-based file), and a BMFF file;
    /// <see cref="MetadataErrorCode.TagNotEditable"/> for the tags above;
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the note does not read back as planned.</para>
    /// </remarks>
    /// <param name="key">The key: <c>Exif.Image.Artist</c>, <c>Exif.Canon.OwnerName</c>, <c>Exif.CanonCs.Quality</c>.</param>
    /// <param name="value">The value.</param>
    /// <param name="allowUnnamed">True to change a MakerNote tag without a name whose type may hold an address. Nothing for a standard key.</param>
    /// <exception cref="ArgumentException">A key that is neither, a value without elements, or what <see cref="Set(ExifTagKey, ExifValue)"/> refuses.</exception>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.TagNotEditable"/> for a standard tag <see cref="IsEditable"/> excludes.</exception>
    public void SetByKey(string key, ExifValue value, bool allowUnnamed = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (Operations.MetadataKeys.TryParseExif(key, out ExifTagKey standard)) { Set(standard, value); return; }
        CheckMakerNoteKey(key);
        if (value.Count == 0) throw new ArgumentException("A value holds at least one element.", nameof(value));
        StoreMakerNote(new(key, value, null, allowUnnamed));
    }

    /// <summary>
    /// Sets a tag by its key from text, as a modify command of Exiv2 gives a value: ASCII text as it
    /// is, numbers separated by blanks, rationals as <c>1/3</c>. A tag of a standard directory is read
    /// in the first field type its definition names (ASCII for a tag without one); a tag of the
    /// MakerNote in the field type it has in the file, when the edit is written. Otherwise as
    /// <see cref="SetByKey(string, ExifValue, bool)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A key that is neither, or text that is not a value of the type.</exception>
    /// <exception cref="FormatException">A number that does not read as one of the type.</exception>
    /// <exception cref="OverflowException">A number outside the range of the type.</exception>
    /// <param name="key">The standard EXIF or supported MakerNote key to set.</param>
    /// <param name="text">The value text to parse using the tag's field type.</param>
    /// <param name="allowUnnamed">Whether to allow a potentially address-bearing MakerNote tag without a library name.</param>
    public void SetTextByKey(string key, string text, bool allowUnnamed = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(text);
        if (Operations.MetadataKeys.TryParseExif(key, out ExifTagKey standard))
        {
            TiffFieldType type = ExifTags.Find(standard) is { Types.IsDefaultOrEmpty: false } definition ? definition.Types[0] : TiffFieldType.Ascii;
            Set(standard, Operations.ExifValueText.Parse(type, text));
            return;
        }
        CheckMakerNoteKey(key);
        StoreMakerNote(new(key, null, text, allowUnnamed));
    }

    /// <summary>
    /// Removes a tag by its key: <see cref="Remove(ExifTagKey)"/> for a key of a standard directory.
    /// For a key of the MakerNote, the tag is taken out of its directory in place: the entries after
    /// it move up, the count drops by one, the offset of the next directory keeps its value, and the
    /// twelve bytes that are freed and the bytes of the value are cleared. A field of an array is not
    /// removed, since the array has no place for fewer; nor is the last tag of a directory. The rest
    /// is as in <see cref="SetByKey(string, ExifValue, bool)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A key that is neither.</exception>
    /// <param name="key">The standard EXIF or supported MakerNote key to remove.</param>
    /// <param name="allowUnnamed">Whether to allow removal of a potentially address-bearing MakerNote tag without a library name.</param>
    public void RemoveByKey(string key, bool allowUnnamed = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Operations.MetadataKeys.TryParseExif(key, out ExifTagKey standard)) { Remove(standard); return; }
        CheckMakerNoteKey(key);
        StoreMakerNote(new(key, null, null, allowUnnamed));
    }

    // Keep textual MakerNote edits out of standard tags and reject every change after whole-block removal.
    private void CheckMakerNoteKey(string key)
    {
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        if (!IsMakerNoteKey(key)) throw new ArgumentException($"`{key}' is not the key of an EXIF tag or of a MakerNote tag.", nameof(key));
    }

    /// <summary>The last change of a key is the one that counts; the order of the others stays.</summary>
    private void StoreMakerNote(MakerNoteChange change)
    {
        makerNote.RemoveAll(earlier => earlier.Key == change.Key);
        makerNote.Add(change);
    }

    // Carry the source note and its raw entry together so copy operations can relocate them as one unit.
    internal void SetMakerNoteForCopy(ExifValue value, MakerNote note)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(note);
        if (removeAll) throw new ArgumentException("Complete EXIF removal cannot be combined with tag edits.");
        makerNoteCopy = new(value, note);
    }

    internal sealed record MakerNoteCopyPayload(ExifValue Value, MakerNote Note);

    /// <summary>A detached, ordered view of the pending edits used during one save.</summary>
    internal sealed record EditSnapshot(bool RemoveAll, IReadOnlyDictionary<ExifTagKey, ExifValue?> Changes)
    {
        /// <summary>The JPEG stream to set as the thumbnail, or null.</summary>
        internal byte[]? Thumbnail { get; init; }
        /// <summary>True when the thumbnail directory is to be removed.</summary>
        internal bool ThumbnailRemoved { get; init; }
        internal bool HasThumbnailChange => Thumbnail is not null || ThumbnailRemoved;
        /// <summary>The changes of the MakerNote, in the order they were made.</summary>
        internal IReadOnlyList<MakerNoteChange> MakerNote { get; init; } = [];
        internal MakerNoteCopyPayload? MakerNoteCopy { get; init; }
        internal bool IsEmpty => !RemoveAll && Changes.Count == 0 && !HasThumbnailChange && MakerNote.Count == 0 && MakerNoteCopy is null;
        internal ExifMetadata Apply(TiffReadResult? source, Endianness order, MetadataLimits limits)
        {
            var entries = source?.Metadata.Entries.ToDictionary(e => e.Key, e => e.Value) ?? [];
            foreach (var change in Changes)
            {
                if (change.Value is null) entries.Remove(change.Key);
                else entries[change.Key] = change.Value;
            }
            if (MakerNoteCopy is { } maker)
                entries[new ExifTagKey(ExifDirectoryKind.Exif, 0x927C)] = ConvertOrder(maker.Value, order);
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
