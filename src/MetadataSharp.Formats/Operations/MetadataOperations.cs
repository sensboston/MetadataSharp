using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Cr3;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Pgf;
using MetadataSharp.Formats.Eps;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;

namespace MetadataSharp.Formats.Operations;

/// <summary>
/// The file operations of the exiv2 command-line tool: delete (<c>rm</c>), extract (<c>ex</c>),
/// insert (<c>in</c>), metadata copy, <c>fixiso</c> and <c>fixcom</c>. The file is detected by its content.
/// </summary>
/// <remarks>
/// <para>Operations that write take the file whole into memory, run their passes there, and write
/// the destination once, after every pass succeeded; the source is never modified. JPEG, PNG, WebP,
/// TIFF-based files, BMFF files, EPS XMP and PGF files with an embedded PNG are written; other
/// containers are refused with <see cref="MetadataErrorCode.UnsupportedFeature"/>.</para>
/// <para>A kind of metadata the container has no place for (IPTC in a WebP file, a comment in a
/// TIFF) is passed over. One it holds and its document cannot write is refused by that document.</para>
/// </remarks>
public static class MetadataOperations
{
    private static readonly ExifTagKey IsoSpeedRatings = new(ExifDirectoryKind.Exif, 0x8827), UserComment = new(ExifDirectoryKind.Exif, 0x9286);

    // ---- delete ----

    /// <summary>Writes <paramref name="sourcePath"/> to <paramref name="destinationPath"/> without the kinds of metadata named: <c>exiv2 rm -d</c>.</summary>
    /// <exception cref="MetadataException">As <see cref="Delete(ReadOnlySpan{byte}, MetadataFamilies)"/>.</exception>
    /// <param name="sourcePath">Image file whose selected metadata families are removed.</param>
    /// <param name="destinationPath">Path for publishing the image after the selected metadata families are removed.</param>
    /// <param name="families">Metadata families to remove from the source image.</param>
    public static void Delete(string sourcePath, string destinationPath, MetadataFamilies families = MetadataFamilies.All)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        Write(destinationPath, Delete(File.ReadAllBytes(sourcePath), families));
    }

    /// <summary>As <see cref="Delete(string, string, MetadataFamilies)"/>, from a stream read to its end, to a stream.</summary>
    /// <param name="source">Readable, seekable image file input stream positioned at the start; it remains open after the read.</param>
    /// <param name="destination">Writable destination stream for the rewritten image; the source stream remains open and separate.</param>
    /// <param name="families">Metadata families to remove from the source image.</param>
    /// <exception cref="ArgumentNullException">The source or destination stream is null.</exception>
    /// <exception cref="MetadataException">The file is malformed, unsupported for this operation, or exceeds a configured budget.</exception>
    public static void Delete(Stream source, Stream destination, MetadataFamilies families = MetadataFamilies.All)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(Delete(Drain(source), families));
    }

    /// <summary>
    /// The file without the kinds of metadata named. A kind the file does not have is no change;
    /// <see cref="MetadataFamilies.Thumbnail"/> removes only the thumbnail of the EXIF block, in a
    /// JPEG, PNG or WebP file (in a TIFF-based file the directory after the primary one is an image
    /// of the file, and stays).
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a container no document writes, or for a
    /// kind the document of the container does not remove; what the readers refuse the file with.
    /// </exception>
    /// <param name="file">Complete image bytes from which the selected metadata families are removed.</param>
    /// <param name="families">Metadata families to remove from the file.</param>
    /// <returns>The complete file bytes after removing the selected metadata families.</returns>
    public static byte[] Delete(ReadOnlySpan<byte> file, MetadataFamilies families = MetadataFamilies.All)
    {
        byte[] bytes = file.ToArray();
        ImageMetadata metadata = ImageMetadata.Read(bytes);
        MetadataFormat format = Writable(metadata.Format);
        var edits = new SyncEdits();
        // A kind its reader refuses is there all the same: it is removed whole, which needs no reading of it.
        bool Held(MetadataFamily family, bool read) => read || metadata.Errors.Any(error => error.Family == family);
        if (families.HasFlag(MetadataFamilies.Exif))
        {
            if (Held(MetadataFamily.Exif, metadata.Exif is not null)) edits.Exif.RemoveAllExif();
        }
        else if (families.HasFlag(MetadataFamilies.Thumbnail) && metadata.Exif is null &&
            metadata.Errors.FirstOrDefault(error => error.Family == MetadataFamily.Exif) is { } unread)
        {
            // The thumbnail of a block its reader refuses cannot be told from the rest of it.
            throw unread.Exception;
        }
        else if (metadata.Exif is not null)
        {
            if (families.HasFlag(MetadataFamilies.Thumbnail))
            {
                if (format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP) edits.Exif.RemoveThumbnail();
                else if (format != MetadataFormat.Tiff && metadata.Exif.Entries.Any(entry => entry.Key.Directory == ExifDirectoryKind.Thumbnail))
                    throw ExifBlockEditor.ThumbnailElsewhere();
            }
        }
        if (families.HasFlag(MetadataFamilies.Iptc) && Held(MetadataFamily.IptcIim, metadata.Iptc is not null) && HoldsIptc(format)) edits.Iptc.RemoveAllIptc();
        if (families.HasFlag(MetadataFamilies.Xmp) && Held(MetadataFamily.Xmp, metadata.Xmp is not null)) edits.Xmp.RemoveAllXmp();
        if (!edits.IsEmpty) bytes = Save(bytes, format, edits);
        if (families.HasFlag(MetadataFamilies.Comment) && metadata.Comments.Count != 0 && HoldsComment(format)) bytes = SaveComment(bytes, format, null);
        if (families.HasFlag(MetadataFamilies.IccProfile) && metadata.IccProfile.Length != 0) bytes = SaveIcc(bytes, format, null);
        return bytes;
    }

    // ---- copy ----

    /// <summary>
    /// Copies selected metadata families from one image to another. The source is read before the
    /// destination is touched; a refusal returns the original destination bytes and writes nothing.
    /// </summary>
    /// <param name="sourcePath">Image file supplying the metadata families to copy.</param>
    /// <param name="destinationPath">Path for publishing the destination image with the selected source metadata copied into it.</param>
    /// <param name="options">Metadata-copy settings; null selects the defaults.</param>
    /// <returns>The copy result, including destination bytes and any refusal reason or notes.</returns>
    /// <exception cref="ArgumentException">A required source or destination path is null or empty.</exception>
    /// <exception cref="ArgumentNullException">A required option value is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A metadata-family or copy-policy option contains an unsupported value.</exception>
    public static MetadataCopyResult CopyMetadata(string sourcePath, string destinationPath, MetadataCopyOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        byte[] source = File.ReadAllBytes(sourcePath);
        byte[] destination = File.ReadAllBytes(destinationPath);
        MetadataCopyResult result = CopyMetadata(source, destination, options);
        if (!result.WasRefused) Write(destinationPath, result.DestinationBytes);
        return result;
    }

    /// <summary>As the path overload, returning the complete new destination bytes instead of writing a file.</summary>
    /// <param name="source">Complete source image bytes from which to copy metadata.</param>
    /// <param name="destination">Complete destination image bytes to preserve when applying the selected metadata changes.</param>
    /// <param name="options">Metadata-copy settings; null selects the defaults.</param>
    /// <returns>The copy result, including destination bytes and any refusal reason or notes.</returns>
    /// <exception cref="ArgumentNullException">The options contain a null limits object.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A metadata-family or copy-policy option contains an unsupported value.</exception>
    public static MetadataCopyResult CopyMetadata(ReadOnlySpan<byte> source, ReadOnlySpan<byte> destination,
        MetadataCopyOptions? options = null)
    {
        options ??= new MetadataCopyOptions();
        ArgumentNullException.ThrowIfNull(options.Limits);
        options.Limits.Validate();
        if ((options.Families & ~MetadataCopyFamilies.All) != 0) throw new ArgumentOutOfRangeException(nameof(options.Families));
        if (!Enum.IsDefined(options.MakerNote)) throw new ArgumentOutOfRangeException(nameof(options.MakerNote));
        if (!Enum.IsDefined(options.Sync)) throw new ArgumentOutOfRangeException(nameof(options.Sync));
        byte[] unchanged = destination.ToArray();
        var notes = new List<MetadataCopyNote>();
        try
        {
            ImageMetadata from = ImageMetadata.Read(source, options.Limits);
            ImageMetadata into = ImageMetadata.Read(destination, options.Limits);
            MetadataFormat format = Writable(into.Format);
            MetadataCopyFamilies requestedFamilies = options.Families;
            MetadataReadError? sourceFailure = from.Errors.FirstOrDefault(error =>
                Requested(requestedFamilies, error.Family) && SupportsCopy(format, FamilyOf(error.Family)));
            if (sourceFailure is not null)
                return Refused(unchanged, notes, MetadataCopyReason.MalformedSourceFamily,
                    "The source has unread requested metadata; no destination bytes were changed: " + sourceFailure.Code + ".");
            MetadataReadError? destinationFailure = into.Errors.FirstOrDefault(error =>
                SupportsCopy(format, FamilyOf(error.Family)) && requestedFamilies.HasFlag(FamilyOf(error.Family)));
            if (destinationFailure is not null)
                return Refused(unchanged, notes, MetadataCopyReason.DestinationRefused,
                    "The destination has unread selected metadata; no destination bytes were changed: " + destinationFailure.Code + ".");
            MetadataCopyFamilies requested = options.Families;
            MetadataCopyFamilies supported = MetadataCopyFamilies.None;
            foreach (MetadataCopyFamilies family in new[] { MetadataCopyFamilies.Exif, MetadataCopyFamilies.Iptc,
                         MetadataCopyFamilies.Xmp, MetadataCopyFamilies.Icc, MetadataCopyFamilies.Comment })
            {
                if (!requested.HasFlag(family)) continue;
                if (SupportsCopy(format, family)) supported |= family;
                else notes.Add(new(MetadataCopyReason.DestinationDoesNotSupportFamily,
                    $"The destination format {format} has no writer for this metadata family.", family));
                if (supported.HasFlag(family) && !SourceHasFamily(from, family))
                    notes.Add(new(MetadataCopyReason.SourceFamilyAbsent,
                        "The source has no metadata in this family; destination metadata was left unchanged.", family));
            }

            // Match Exiv2's -ea / -ia operation: source entries overwrite matching destination
            // entries, while destination-only metadata is retained. A family absent at the source
            // is therefore a no-op, not a request to erase the destination's family.
            byte[] output = unchanged;

            bool copyExif = supported.HasFlag(MetadataCopyFamilies.Exif) && from.Exif is not null;
            bool copyIptc = supported.HasFlag(MetadataCopyFamilies.Iptc) && from.Iptc is not null;
            bool copyXmp = supported.HasFlag(MetadataCopyFamilies.Xmp) && from.Xmp is not null;
            bool copyComment = supported.HasFlag(MetadataCopyFamilies.Comment) && from.Comments.Count != 0;
            if (copyExif || copyIptc || copyXmp || copyComment)
            {
                byte[]? thumbnail = copyExif ? Thumbnail(source, from).ToArray() : null;
                byte[] BuildExv(byte[]? preview) => ExvFile.Create(copyExif ? from.Exif : null, copyIptc ? from.Iptc : null,
                    copyXmp ? from.Xmp : null, copyComment ? from.Comments[0] : null, preview ?? []);
                byte[] exv;
                try { exv = BuildExv(thumbnail); }
                catch (MetadataException thumbnailRefusal) when (thumbnail is { Length: > 0 })
                {
                    // An unreadable/unsupported source thumbnail must not prevent copying otherwise
                    // readable EXIF, including a MakerNote. The omission is explicit in the result.
                    notes.Add(new(MetadataCopyReason.ValueNotRepresentable,
                        "The EXIF thumbnail was skipped because it cannot be represented at the destination: " + thumbnailRefusal.Message,
                        MetadataCopyFamilies.Exif));
                    exv = BuildExv(null);
                }
                MetadataFamilies insertFamilies = MetadataFamilies.None;
                if (copyExif) insertFamilies |= MetadataFamilies.Exif;
                if (copyIptc) insertFamilies |= MetadataFamilies.Iptc;
                if (copyXmp) insertFamilies |= MetadataFamilies.Xmp;
                if (copyComment) insertFamilies |= MetadataFamilies.Comment;
                output = InsertExvCore(output, exv, insertFamilies, out IReadOnlyList<string> insertNotes, preserveExisting: true);
                foreach (string note in insertNotes)
                    notes.Add(new(MetadataCopyReason.ValueNotRepresentable, note));
            }

            MetadataCopyFamilies copied = MetadataCopyFamilies.None;
            if (copyExif) copied |= MetadataCopyFamilies.Exif;
            if (copyIptc) copied |= MetadataCopyFamilies.Iptc;
            if (copyXmp) copied |= MetadataCopyFamilies.Xmp;
            if (copyComment) copied |= MetadataCopyFamilies.Comment;
            if (supported.HasFlag(MetadataCopyFamilies.Icc) && from.IccProfile.Length != 0)
            {
                output = SaveIcc(output, format, from.IccProfile.ToArray());
                copied |= MetadataCopyFamilies.Icc;
            }

            if (copyExif && options.MakerNote == MetadataMakerNoteMode.Copy)
            {
                if (!TryMakerNoteForCopy(source, from, options.Limits, out MakerNote? note, out ExifValue? raw))
                {
                    if (from.Exif!.MakerNote is not null || HasRawMakerNote(from.Exif))
                        notes.Add(new(MetadataCopyReason.MakerNoteNotDecoded,
                            "The source MakerNote is not decoded well enough to prove safe relocation.", MetadataCopyFamilies.Exif));
                }
                else if (!MakerNoteRelocator.CanRelocate(note!, raw!, from.Exif!, out string refusal))
                {
                    MetadataCopyReason reason = note!.Format == MakerNoteFormat.Nikon3 &&
                        (refusal.Contains("cipher", StringComparison.OrdinalIgnoreCase) || refusal.Contains("shutter-count key", StringComparison.OrdinalIgnoreCase) || refusal.Contains("serial-number", StringComparison.OrdinalIgnoreCase))
                        ? MetadataCopyReason.MakerNoteCipherKeysUnavailable : MetadataCopyReason.UnsafeMakerNoteRelocation;
                    notes.Add(new(reason, refusal, MetadataCopyFamilies.Exif));
                }
                else
                {
                    var maker = new SyncEdits();
                    maker.Exif.SetMakerNoteForCopy(raw!, note!);
                    try { output = Save(output, format, maker); }
                    catch (MetadataException writeRefusal)
                    {
                        notes.Add(new(MetadataCopyReason.UnsafeMakerNoteRelocation,
                            "The destination writer could not relocate and verify the MakerNote; it was skipped: " + writeRefusal.Message,
                            MetadataCopyFamilies.Exif));
                    }
                }
            }
            else if (copyExif && options.MakerNote == MetadataMakerNoteMode.Skip &&
                     (from.Exif!.MakerNote is not null || HasRawMakerNote(from.Exif)))
            {
                notes.Add(new(MetadataCopyReason.MakerNoteCopyDisabled, "MakerNote copying was disabled by the options.", MetadataCopyFamilies.Exif));
            }

            if (options.Sync == MetadataCopySync.ExistingRules)
            {
                ImageMetadata copiedMetadata = ImageMetadata.Read(output, options.Limits);
                SyncEdits reconciliation = MetadataSync.Reconcile(copiedMetadata.Exif, copiedMetadata.Iptc,
                    copiedMetadata.Xmp, copiedMetadata.Iptc?.Digest);
                foreach (SyncNote note in reconciliation.Notes)
                    notes.Add(new(MetadataCopyReason.SyncValueNotRepresentable, note.Source + " → " + note.Target + ": " + note.Reason));
                if (!reconciliation.IsEmpty) output = Save(output, format, reconciliation);
            }
            return new(output, copied, notes.AsReadOnly(), refused: false);
        }
        catch (MetadataException refusal)
        {
            return Refused(unchanged, notes, MetadataCopyReason.CopyRefused,
                "The planned copy was refused and no destination bytes were written: " + refusal.Message);
        }
    }

    // Keep Refused focused on metadata and its format-specific validation contract.
    private static MetadataCopyResult Refused(byte[] unchanged, List<MetadataCopyNote> notes,
        MetadataCopyReason reason, string message)
    {
        notes.Add(new(reason, message));
        return new(unchanged, MetadataCopyFamilies.None, notes.AsReadOnly(), refused: true);
    }

    private static bool Requested(MetadataCopyFamilies requested, MetadataFamily family)
    {
        MetadataCopyFamilies mapped = FamilyOf(family);
        return mapped != MetadataCopyFamilies.None && requested.HasFlag(mapped);
    }

    // Keep FamilyOf focused on metadata and its format-specific validation contract.
    private static MetadataCopyFamilies FamilyOf(MetadataFamily family) => family switch
    {
        MetadataFamily.Exif => MetadataCopyFamilies.Exif,
        MetadataFamily.IptcIim => MetadataCopyFamilies.Iptc,
        MetadataFamily.Xmp => MetadataCopyFamilies.Xmp,
        MetadataFamily.Icc => MetadataCopyFamilies.Icc,
        MetadataFamily.Comment => MetadataCopyFamilies.Comment,
        _ => MetadataCopyFamilies.None
    };

    // Keep SourceHasFamily focused on metadata and its format-specific validation contract.
    private static bool SourceHasFamily(ImageMetadata metadata, MetadataCopyFamilies family) => family switch
    {
        MetadataCopyFamilies.Exif => metadata.Exif is not null,
        MetadataCopyFamilies.Iptc => metadata.Iptc is not null,
        MetadataCopyFamilies.Xmp => metadata.Xmp is not null,
        MetadataCopyFamilies.Icc => metadata.IccProfile.Length != 0,
        MetadataCopyFamilies.Comment => metadata.Comments.Count != 0,
        _ => false
    };

    // Keep SupportsCopy focused on metadata and its format-specific validation contract.
    private static bool SupportsCopy(MetadataFormat format, MetadataCopyFamilies family) => family switch
    {
        MetadataCopyFamilies.Exif => format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP or MetadataFormat.Tiff or MetadataFormat.Bmff,
        MetadataCopyFamilies.Iptc => HoldsIptc(format),
        MetadataCopyFamilies.Xmp => format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP or MetadataFormat.Tiff or MetadataFormat.Bmff or MetadataFormat.Pgf or MetadataFormat.Eps,
        MetadataCopyFamilies.Icc => format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP or MetadataFormat.Tiff or MetadataFormat.Bmff,
        MetadataCopyFamilies.Comment => HoldsComment(format),
        _ => false
    };

    private static bool TryMakerNoteForCopy(ReadOnlySpan<byte> source, ImageMetadata metadata, MetadataLimits limits,
        out MakerNote? note, out ExifValue? value)
    {
        note = metadata.Exif?.MakerNote;
        if (note is null) { value = null; return false; }
        if (TryMakerNoteValue(metadata.Exif!, out value)) return true;

        // In CR3 the MakerNote is CMT3, a standalone TIFF rather than an Exif.Photo.MakerNote value.
        // Its directory has already been decoded, so retain the CMT3 payload from the source and
        // normalize its TIFF header/offset origin before the ordinary relocation proof and writer.
        if (metadata.Format == MetadataFormat.Cr3 && note.Format == MakerNoteFormat.Canon)
        {
            Cr3Layout layout = Cr3Reader.Locate(source, limits);
            if (layout.MakerNote.Present)
            {
                ReadOnlySpan<byte> embedded = source.Slice(layout.MakerNote.Start, layout.MakerNote.Length);
                value = MakerNoteRelocator.NormalizeEmbeddedTiff(note, embedded, out MakerNote normalized);
                note = normalized;
                return true;
            }
        }
        value = null;
        return false;
    }

    // Keep TryMakerNoteValue focused on metadata and its format-specific validation contract.
    private static bool TryMakerNoteValue(ExifMetadata exif, out ExifValue? value)
    {
        ExifTagKey makerNote = new(ExifDirectoryKind.Exif, 0x927C);
        if (exif.TryGetEntry(makerNote, out ExifEntry? standard)) { value = standard.Value; return true; }
        ExifTagKey privateData = new(ExifDirectoryKind.Ifd0, 0xC634);
        if (exif.TryGetEntry(privateData, out ExifEntry? pentax))
        {
            byte[] bytes = pentax.Value.RawBytes.ToArray();
            value = ExifValue.FromRaw(TiffFieldType.Undefined, (uint)bytes.Length, pentax.Value.ByteOrder, bytes);
            return true;
        }
        value = null;
        return false;
    }

    private static bool HasRawMakerNote(ExifMetadata exif) =>
        exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Exif, 0x927C), out _) ||
        exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0xC634), out _);

    // ---- extract ----

    /// <summary>
    /// The metadata of a file as an EXV file: <c>exiv2 ex</c>, narrowed by the kinds named.
    /// </summary>
    /// <remarks>
    /// From a JPEG file the segments are copied as they are, so the EXIF block arrives byte for byte,
    /// with its MakerNote and its thumbnail, and so do the image resources that hold the IPTC stream
    /// and an ICC profile. From any other file the EXV file is built by <see cref="ExvFile.Create"/>,
    /// with the selected ICC profile carried as numbered APP2 segments.
    /// </remarks>
    /// <exception cref="MetadataException">What the readers refuse the file with, and what <see cref="ExvFile.Create"/> refuses.</exception>
    /// <param name="file">Complete image bytes from which the selected metadata families are extracted into EXV.</param>
    /// <param name="families">Metadata families selected for this operation.</param>
    /// <returns>A complete EXV file containing the selected metadata families.</returns>
    public static byte[] ExtractExv(ReadOnlySpan<byte> file, MetadataFamilies families = MetadataFamilies.All)
    {
        // A JPEG file and an EXV file hold their metadata in the same segments, which are copied as they are.
        MetadataFormat format = ImageMetadata.Detect(file[..Math.Min(file.Length, ImageMetadata.HeaderLength)]);
        if (format == MetadataFormat.Jpeg) return SegmentsToExv(file, 2, families);
        if (format == MetadataFormat.Exv) return SegmentsToExv(file, ExvFile.Signature.Length, families);
        ImageMetadata metadata = ImageMetadata.Read(file);
        bool exif = families.HasFlag(MetadataFamilies.Exif);
        return ExvFile.Create(
            exif ? metadata.Exif : null,
            families.HasFlag(MetadataFamilies.Iptc) ? metadata.Iptc : null,
            families.HasFlag(MetadataFamilies.Xmp) ? metadata.Xmp : null,
            families.HasFlag(MetadataFamilies.Comment) && metadata.Comments.Count != 0 ? metadata.Comments[0] : null,
            exif || families.HasFlag(MetadataFamilies.Thumbnail) ? Thumbnail(file, metadata) : default,
            families.HasFlag(MetadataFamilies.IccProfile) && !metadata.IccProfile.IsEmpty ? metadata.IccProfile.ToArray() : null);
    }

    /// <summary>
    /// The XMP packet of a file as the content of a sidecar file: <c>exiv2 ex -e X</c>. With
    /// <paramref name="convert"/> the EXIF tags and the IPTC datasets of the file are first converted
    /// into the packet, as <see cref="MetadataSync.ExifToXmp"/> and <see cref="MetadataSync.IptcToXmp"/>
    /// do, which is what exiv2 writes. Null for a file with nothing to write.
    /// </summary>
    /// <exception cref="MetadataException">What the readers refuse the file with; <see cref="MetadataErrorCode.UnsupportedFeature"/> for a packet that cannot be edited.</exception>
    /// <param name="file">Complete image bytes whose XMP packet is serialized as a sidecar.</param>
    /// <param name="convert">Whether to convert sidecar properties into corresponding EXIF and IPTC values after replacing XMP.</param>
    /// <returns>The serialized XMP sidecar bytes, or null when the file has no XMP packet.</returns>
    public static byte[]? ExtractXmpSidecar(ReadOnlySpan<byte> file, bool convert = true)
    {
        ImageMetadata metadata = ImageMetadata.Read(file);
        XmpPacket? packet = metadata.Xmp;
        if (convert)
        {
            if (metadata.Exif is not null)
            {
                SyncEdits edits = MetadataSync.ExifToXmp(metadata.Exif, packet);
                if (!edits.Xmp.IsEmpty) packet = (packet ?? XmpPacket.CreateEmpty()).Apply(edits.Xmp);
            }
            if (metadata.Iptc is not null)
            {
                SyncEdits edits = MetadataSync.IptcToXmp(metadata.Iptc, packet);
                if (!edits.Xmp.IsEmpty) packet = (packet ?? XmpPacket.CreateEmpty()).Apply(edits.Xmp);
            }
        }
        if (packet is null) return null;
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    /// <summary>The JPEG thumbnail of the EXIF block of a file (<c>exiv2 ex -e t</c>), or null when it has none.</summary>
    /// <exception cref="MetadataException">What the readers refuse the file with.</exception>
    /// <param name="file">Complete image bytes whose EXIF thumbnail is being extracted.</param>
    /// <returns>The JPEG thumbnail bytes, or null when the EXIF block has no JPEG thumbnail.</returns>
    public static byte[]? ExtractThumbnail(ReadOnlySpan<byte> file)
    {
        ReadOnlySpan<byte> found = Thumbnail(file, ImageMetadata.Read(file));
        return found.Length == 0 ? null : found.ToArray();
    }

    // Keep Thumbnail focused on metadata and its format-specific validation contract.
    private static ReadOnlySpan<byte> Thumbnail(ReadOnlySpan<byte> file, ImageMetadata metadata)
    {
        foreach (PreviewImage preview in metadata.Previews)
        {
            if (preview.Source == "Exif.Thumbnail.JPEGInterchangeFormat" && preview.Offset >= 0 && preview.Length > 0 &&
                preview.Offset <= file.Length - preview.Length)
                return file.Slice((int)preview.Offset, preview.Length);
        }
        return default;
    }

    // ---- insert ----

    /// <summary>Writes <paramref name="sourcePath"/> to <paramref name="destinationPath"/> with the metadata of an EXV file: see <see cref="InsertExv(ReadOnlySpan{byte}, ReadOnlySpan{byte}, MetadataFamilies, out IReadOnlyList{string})"/>.</summary>
    /// <returns>What was not carried.</returns>
    /// <param name="sourcePath">Image file to receive the selected EXV metadata.</param>
    /// <param name="destinationPath">Path for publishing the image after the selected EXV metadata is inserted.</param>
    /// <param name="exv">Complete EXV file bytes containing the metadata to insert.</param>
    /// <param name="families">Metadata families from the EXV file that may replace corresponding destination families.</param>
    /// <exception cref="ArgumentException">A source or destination path is null or empty.</exception>
    public static IReadOnlyList<string> InsertExv(string sourcePath, string destinationPath, ReadOnlySpan<byte> exv, MetadataFamilies families = MetadataFamilies.All)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        Write(destinationPath, InsertExv(File.ReadAllBytes(sourcePath), exv, families, out IReadOnlyList<string> notes));
        return notes;
    }

    /// <summary>
    /// The file with the metadata of an EXV file in place of its own: <c>exiv2 in</c>. Each kind
    /// that is named, that the EXV file holds and that the container has a place for replaces that
    /// kind of the file whole; the others stay.
    /// </summary>
    /// <remarks>
    /// The EXIF block is written from the tags an <see cref="ExifEdit"/> can set: the MakerNote and
    /// the tags of image data are not carried (exiv2 carries the MakerNote), and
    /// <paramref name="notes"/> says so, with every tag the edit refuses. The thumbnail of the EXV
    /// file is set in a JPEG, PNG or WebP file, with the tags of its directory. The IPTC stream
    /// written declares UTF-8 (1:90) and its own record version: a dataset whose bytes are not UTF-8,
    /// from a stream that declares no character set, is not carried, and noted. In a TIFF-based file the tags of the EXV file are set
    /// over the ones the file has, since the tags that describe its image cannot be removed.
    /// </remarks>
    /// <exception cref="MetadataException">What the readers and the documents refuse; <see cref="MetadataErrorCode.UnsupportedFeature"/> for a container no document writes.</exception>
    /// <param name="file">Complete image bytes to update with the selected EXV metadata.</param>
    /// <param name="exv">Complete EXV file bytes containing the metadata to insert.</param>
    /// <param name="families">Metadata families from the EXV file that may replace corresponding destination families.</param>
    /// <param name="notes">Receives non-fatal notes about metadata families that could not be inserted.</param>
    /// <returns>The complete output image bytes after inserting the selected EXV metadata.</returns>
    public static byte[] InsertExv(ReadOnlySpan<byte> file, ReadOnlySpan<byte> exv, MetadataFamilies families, out IReadOnlyList<string> notes) =>
        InsertExvCore(file, exv, families, out notes, preserveExisting: false);

    private static byte[] InsertExvCore(ReadOnlySpan<byte> file, ReadOnlySpan<byte> exv, MetadataFamilies families,
        out IReadOnlyList<string> notes, bool preserveExisting)
    {
        ImageMetadata from = ImageMetadata.Read(exv);
        if (from.Format != MetadataFormat.Exv)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The metadata to insert is not an EXV file.");
        foreach (MetadataReadError error in from.Errors) throw error.Exception;
        var said = new List<string>();
        notes = said;
        byte[] bytes = file.ToArray();
        ImageMetadata into = ImageMetadata.Read(bytes);
        MetadataFormat format = Writable(into.Format);
        bool blocks = format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP;

        var clear = new SyncEdits();
        var set = new SyncEdits();
        bool carryThumbnailTags = false;
        if (families.HasFlag(MetadataFamilies.Exif) && from.Exif is not null)
        {
            if (!preserveExisting && into.Exif is not null && format != MetadataFormat.Tiff) clear.Exif.RemoveAllExif();
            ExvFile.CopyTags(from.Exif, set.Exif, said);
            if (!preserveExisting && from.Exif.MakerNote is not null) said.Add("The MakerNote is not carried.");
            ReadOnlySpan<byte> thumbnail = Thumbnail(exv, from);
            if (thumbnail.Length != 0)
            {
                if (blocks)
                {
                    set.Exif.SetThumbnail(thumbnail);
                    carryThumbnailTags = true;
                }
                else said.Add("The thumbnail is not carried: it is written to JPEG, PNG and WebP files.");
            }
        }
        else if (families.HasFlag(MetadataFamilies.Thumbnail) && blocks && Thumbnail(exv, from) is { Length: > 0 } only)
        {
            set.Exif.SetThumbnail(only);
        }
        if (families.HasFlag(MetadataFamilies.Iptc) && from.Iptc is not null)
        {
            if (!HoldsIptc(format)) said.Add("The IPTC stream is not carried: the container has no place for it.");
            else
            {
                if (!preserveExisting && into.Iptc is not null) clear.Iptc.RemoveAllIptc();
                bool declared = from.Iptc.CharacterSet == IptcCharacterSet.Utf8;
                foreach (IGrouping<IptcDatasetKey, IptcDataset> group in from.Iptc.Datasets.GroupBy(dataset => dataset.Key))
                {
                    if (group.Key == new IptcDatasetKey(1, 90) || group.Key == new IptcDatasetKey(2, 0)) continue; // the character set and the record version are the writer's
                    string name = MetadataSharp.Core.Operations.MetadataKeys.Format(group.Key) ?? $"IPTC {group.Key.Record}:{group.Key.Dataset}";
                    if (group.Key.Record is not (1 or 2))
                    {
                        said.Add(name + " is not carried: only the datasets of records 1 and 2 are written.");
                        continue;
                    }
                    // The stream written declares UTF-8. Bytes that are not UTF-8, under no declaration, are text in a
                    // character set nobody states: they are not carried as if they were UTF-8.
                    if (!declared && group.Any(dataset => !IsUtf8(dataset.Value)))
                    {
                        said.Add(name + " is not carried: its text is not UTF-8 and the stream does not declare a character set.");
                        continue;
                    }
                    set.Iptc.ReplaceRaw(group.Key, group.Select(dataset => dataset.Value));
                }
            }
        }
        if (families.HasFlag(MetadataFamilies.Xmp) && from.Xmp is not null)
        {
            if (!preserveExisting && into.Xmp is not null) clear.Xmp.RemoveAllXmp();
            set.Xmp.ReplaceWith(from.Xmp);
        }
        if (!clear.IsEmpty) bytes = Save(bytes, format, clear);
        if (!set.IsEmpty) bytes = Save(bytes, format, set);
        if (carryThumbnailTags)
        {
            // The directory of the thumbnail is there now: the tags the EXV file has in its own are set in it.
            var tags = new SyncEdits();
            foreach (ExifEntry entry in from.Exif!.Entries)
            {
                if (entry.Key.Directory != ExifDirectoryKind.Thumbnail || entry.Key.TagId == 0x0103 || !ExifEdit.IsEditable(entry.Key)) continue;
                try { tags.Exif.SetUnchecked(entry.Key, entry.Value); }
                catch (ArgumentException refusal) { said.Add(MetadataSharp.Core.Operations.MetadataKeys.Format(entry.Key) + " is not carried: " + refusal.Message); }
            }
            try
            {
                if (!tags.IsEmpty) bytes = Save(bytes, format, tags);
            }
            catch (MetadataException refusal)
            {
                said.Add("The tags of the thumbnail directory are not carried: " + refusal.Message);
            }
        }
        if (families.HasFlag(MetadataFamilies.Comment) && from.Comments.Count != 0)
        {
            if (HoldsComment(format)) bytes = SaveComment(bytes, format, from.Comments[0]);
            else said.Add("The comment is not carried: the container has no place for it.");
        }
        return bytes;
    }

    /// <summary>Writes <paramref name="sourcePath"/> to <paramref name="destinationPath"/> with the packet of a sidecar file: see <see cref="InsertXmpSidecar(ReadOnlySpan{byte}, ReadOnlySpan{byte}, bool)"/>.</summary>
    /// <param name="sourcePath">Image file to receive the sidecar XMP packet.</param>
    /// <param name="destinationPath">Path for publishing the image after its XMP sidecar packet is applied.</param>
    /// <param name="sidecar">Complete XMP sidecar packet bytes to insert.</param>
    /// <param name="convert">Whether to convert sidecar properties into corresponding EXIF and IPTC values after replacing XMP.</param>
    /// <exception cref="ArgumentException">A source or destination path is null or empty.</exception>
    public static void InsertXmpSidecar(string sourcePath, string destinationPath, ReadOnlySpan<byte> sidecar, bool convert = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        Write(destinationPath, InsertXmpSidecar(File.ReadAllBytes(sourcePath), sidecar, convert));
    }

    /// <summary>
    /// The file with the packet of a sidecar file in place of its XMP packet: <c>exiv2 in -i X</c>.
    /// With <paramref name="convert"/> the properties of the new packet are then converted to the EXIF
    /// tags and IPTC datasets of the file, as <see cref="MetadataSync.XmpToExif"/> and
    /// <see cref="MetadataSync.XmpToIptc"/> do, which is what exiv2 does.
    /// </summary>
    /// <exception cref="MetadataException">What the readers and the documents refuse; <see cref="MetadataErrorCode.UnsupportedFeature"/> for a container no document writes or a packet that cannot be edited.</exception>
    /// <param name="file">Complete image bytes to update with the sidecar XMP packet.</param>
    /// <param name="sidecar">Complete XMP sidecar packet bytes to insert.</param>
    /// <param name="convert">Whether to convert sidecar properties into corresponding EXIF and IPTC values after replacing XMP.</param>
    /// <returns>The complete output image bytes after replacing XMP and applying any requested conversions.</returns>
    public static byte[] InsertXmpSidecar(ReadOnlySpan<byte> file, ReadOnlySpan<byte> sidecar, bool convert = true)
    {
        XmpPacket packet = XmpPacket.Parse(sidecar);
        byte[] bytes = file.ToArray();
        ImageMetadata into = ImageMetadata.Read(bytes);
        MetadataFormat format = Writable(into.Format);
        if (into.Xmp is not null)
        {
            var clear = new SyncEdits();
            clear.Xmp.RemoveAllXmp();
            bytes = Save(bytes, format, clear);
        }
        var set = new SyncEdits();
        set.Xmp.ReplaceWith(packet);
        if (!set.IsEmpty) bytes = Save(bytes, format, set);
        if (!convert) return bytes;

        SyncEdits exif = MetadataSync.XmpToExif(packet, into.Exif);
        if (!exif.IsEmpty) bytes = Save(bytes, format, exif);
        if (HoldsIptc(format))
        {
            SyncEdits iptc = MetadataSync.XmpToIptc(packet, into.Iptc);
            if (!iptc.IsEmpty) bytes = Save(bytes, format, iptc);
        }
        return bytes;
    }

    // Keep IsUtf8 focused on metadata and its format-specific validation contract.
    private static bool IsUtf8(IptcValue value)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    // ---- fixiso, fixcom ----

    /// <summary>
    /// <c>exiv2 fixiso</c>: an edit that sets ISOSpeedRatings (Exif, 0x8827) from the ISO setting of a
    /// Canon or Nikon MakerNote. Null when the tag is present, when there is no such MakerNote, or
    /// when it holds no speed that fits the tag.
    /// </summary>
    /// <param name="exif">EXIF metadata whose tags are inspected for a possible correction.</param>
    /// <returns>An EXIF edit that normalizes ISO speed tags, or null when no correction is needed.</returns>
    /// <exception cref="ArgumentNullException">The EXIF metadata is null.</exception>
    public static ExifEdit? FixIso(ExifMetadata exif)
    {
        ArgumentNullException.ThrowIfNull(exif);
        if (exif.TryGetEntry(IsoSpeedRatings, out _) || exif.MakerNote is not MakerNote note) return null;
        long? speed = null;
        if (note.Format == MakerNoteFormat.Canon)
        {
            // CameraSettings, field 16: a speed with the flag 0x4000, or one of the coded speeds.
            if (Field(note, 0x0001, 16) is long coded)
            {
                speed = (coded & 0x4000) != 0 ? coded & 0x3FFF : coded switch { 16 => 50L, 17 => 100L, 18 => 200L, 19 => 400L, 20 => 800L, _ => (long?)null };
            }
            // ShotInfo, field 2: the speed in thirty-seconds of a stop over ISO 100/32.
            if ((speed is null || speed == 0) && Field(note, 0x0004, 2) is long apex && apex > 0 && apex < 1024)
                speed = (long)Math.Round(Math.Exp(apex / 32.0 * Math.Log(2)) * 100.0 / 32.0);
        }
        else if (note.Format.ToString().StartsWith("Nikon", StringComparison.Ordinal))
        {
            // ISOSpeed: two numbers, the speed second.
            ushort tag = note.Format.ToString() == "Nikon2" ? (ushort)0x0006 : (ushort)0x0002;
            if (note.TryGetEntry(tag, out MakerNoteEntry? entry) && entry.Value.TryGetUInt16Array(out ushort[]? numbers) && numbers.Length != 0)
                speed = numbers[^1];
        }
        if (speed is not (> 0 and <= ushort.MaxValue)) return null;
        var edit = new ExifEdit();
        edit.Set(IsoSpeedRatings, (ushort)speed.Value);
        return edit;
    }

    // Find a MakerNote value by its tag and occurrence index.
    private static long? Field(MakerNote note, ushort tag, ushort index)
    {
        if (!note.TryGetEntry(tag, out MakerNoteEntry? entry)) return null;
        foreach (MakerNoteField field in entry.Fields)
            if (field.Index == index && field.Value.TryGetInt64(out long value)) return value;
        return null;
    }

    /// <summary>
    /// <c>exiv2 fixcom</c>: an edit that rewrites a UserComment (Exif, 0x9286) marked <c>UNICODE</c>
    /// whose text is really in <paramref name="current"/> (the <c>-n</c> option) as UCS-2 in the byte
    /// order of the block. Null without the tag, for a comment under another character code, and
    /// when the bytes would not change.
    /// </summary>
    /// <param name="exif">EXIF metadata whose tags are inspected for a possible correction.</param>
    /// <param name="current">Encoding used to decode the current EXIF UserComment bytes.</param>
    /// <returns>An EXIF edit that normalizes UserComment bytes, or null when no correction is needed.</returns>
    /// <exception cref="ArgumentNullException">The EXIF metadata or text encoding is null.</exception>
    public static ExifEdit? FixUserComment(ExifMetadata exif, Encoding current)
    {
        ArgumentNullException.ThrowIfNull(exif);
        ArgumentNullException.ThrowIfNull(current);
        if (!exif.TryGetEntry(UserComment, out ExifEntry? entry)) return null;
        byte[] raw = new byte[entry.Value.ByteLength];
        entry.Value.RawBytes.CopyTo(raw);
        if (raw.Length <= 8 || !raw.AsSpan(0, 8).SequenceEqual("UNICODE\0"u8)) return null;
        string text = current.GetString(raw, 8, raw.Length - 8);
        Encoding wide = exif.ByteOrder == Endianness.BigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] fixedBytes = [.. "UNICODE\0"u8, .. wide.GetBytes(text)];
        if (fixedBytes.AsSpan().SequenceEqual(raw)) return null;
        var edit = new ExifEdit();
        edit.SetUnchecked(UserComment, ExifValue.FromRaw(TiffFieldType.Undefined, (uint)fixedBytes.Length, exif.ByteOrder, fixedBytes));
        return edit;
    }

    // ---- the passes ----

    // Keep Writable focused on metadata and its format-specific validation contract.
    private static MetadataFormat Writable(MetadataFormat format) => format switch
    {
        MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP or MetadataFormat.Tiff or MetadataFormat.Bmff or MetadataFormat.Pgf or MetadataFormat.Eps => format,
        _ => throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Files of the format " + format + " are not written."),
    };

    private static bool HoldsIptc(MetadataFormat format) => format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.Tiff or MetadataFormat.Pgf;

    private static bool HoldsComment(MetadataFormat format) => format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.Pgf;

    // Write the prepared metadata edit only after validation, preserving unrelated payloads.
    private static byte[] Save(byte[] file, MetadataFormat format, SyncEdits edits)
    {
        using var input = new MemoryStream(file, false);
        var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: { using var document = JpegMetadataDocument.Open(input); document.SaveAll(output, edits); break; }
            case MetadataFormat.Png: { using var document = PngMetadataDocument.Open(input); document.SaveAll(output, edits); break; }
            case MetadataFormat.WebP: { using var document = WebPMetadataDocument.Open(input); document.SaveAll(output, edits); break; }
            case MetadataFormat.Tiff: { using var document = TiffBasedMetadataDocument.Open(input); document.SaveAll(output, edits); break; }
            case MetadataFormat.Pgf: return PgfMetadata.Write(file, edits);
            case MetadataFormat.Eps: return EpsMetadata.WriteXmp(file, edits.Xmp);
            default: { using var document = BmffMetadataDocument.Open(input); document.SaveAll(output, edits); break; }
        }
        return output.ToArray();
    }

    // Write the prepared metadata edit only after validation, preserving unrelated payloads.
    private static byte[] SaveComment(byte[] file, MetadataFormat format, string? comment)
    {
        if (format == MetadataFormat.Pgf) return PgfMetadata.Write(file, new SyncEdits(), comment, replaceComment: true);
        using var input = new MemoryStream(file, false);
        var output = new MemoryStream();
        if (format == MetadataFormat.Jpeg) { using var document = JpegMetadataDocument.Open(input); document.SaveComment(output, comment); }
        else { using var document = PngMetadataDocument.Open(input); document.SaveComment(output, comment); }
        return output.ToArray();
    }

    // Write the prepared metadata edit only after validation, preserving unrelated payloads.
    private static byte[] SaveIcc(byte[] file, MetadataFormat format, byte[]? profile)
    {
        using var input = new MemoryStream(file, false);
        var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: { using var document = JpegMetadataDocument.Open(input); document.SaveIccProfile(output, profile); break; }
            case MetadataFormat.Png: { using var document = PngMetadataDocument.Open(input); document.SaveIccProfile(output, profile); break; }
            case MetadataFormat.WebP: { using var document = WebPMetadataDocument.Open(input); document.SaveIccProfile(output, profile); break; }
            case MetadataFormat.Tiff: { using var document = TiffBasedMetadataDocument.Open(input); document.SaveIccProfile(output, profile); break; }
            default: { using var document = BmffMetadataDocument.Open(input); document.SaveIccProfile(output, profile); break; }
        }
        return output.ToArray();
    }

    // Keep Drain focused on metadata and its format-specific validation contract.
    private static byte[] Drain(Stream source)
    {
        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The file appears under its name only when it is complete.</summary>
    private static void Write(string path, byte[] bytes)
    {
        string partial = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(partial, bytes);
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    /// <summary>The segments of a JPEG file that hold the kinds named, under the signature of an EXV file.</summary>
    private static byte[] SegmentsToExv(ReadOnlySpan<byte> file, int start, MetadataFamilies families)
    {
        var output = new MemoryStream();
        output.Write(ExvFile.Signature);
        int position = start;
        while (position <= file.Length - 4)
        {
            if (file[position] != 0xFF)
                throw new MetadataException(MetadataErrorCode.InvalidJpeg, "Expected a marker.", position);
            byte marker = file[position + 1];
            if (marker == 0xFF) { position++; continue; } // a fill byte
            if (marker is 0xDA or 0xD9) break; // image data, or the end
            if (marker is >= 0xD0 and <= 0xD7 or 0x01) { position += 2; continue; }
            int length = BinaryPrimitives.ReadUInt16BigEndian(file[(position + 2)..]);
            if (length < 2 || length > file.Length - position - 2)
                throw new MetadataException(MetadataErrorCode.InvalidJpeg, "A segment runs past the end of the file.", position);
            ReadOnlySpan<byte> payload = file.Slice(position + 4, length - 2);
            bool take = marker switch
            {
                0xE1 when payload.StartsWith(ExvFile.ExifHeader) => families.HasFlag(MetadataFamilies.Exif),
                0xE1 when payload.StartsWith(ExvFile.XmpHeader) || payload.StartsWith("http://ns.adobe.com/xmp/extension/\0"u8) => families.HasFlag(MetadataFamilies.Xmp),
                0xED when payload.StartsWith(ExvFile.PhotoshopHeader) => families.HasFlag(MetadataFamilies.Iptc),
                0xE2 when payload.StartsWith("ICC_PROFILE\0"u8) => families.HasFlag(MetadataFamilies.IccProfile),
                0xFE => families.HasFlag(MetadataFamilies.Comment),
                _ => false,
            };
            if (take) output.Write(file.Slice(position, length + 2));
            position += length + 2;
        }
        output.Write([0xFF, 0xD9]);
        return output.ToArray();
    }
}
