using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Crw;

/// <summary>
/// A Canon CRW file opened for editing: the tags <see cref="CrwMetadata"/> gives are written back
/// into the records of the heap they were read from, and the comment into its record. These are
/// the tags the reference tool (Exiv2 0.28.9) writes to a CRW file, and no other tag has a record
/// to go to.
/// </summary>
/// <remarks>
/// <para>What is written, and where:</para>
/// <list type="bullet">
/// <item><c>Exif.Image.Make</c> and <c>Model</c>: one record holds both, so both are set or both
/// are removed.</item>
/// <item><c>Exif.Image.Orientation</c> (1, 3, 6 or 8, stored as a rotation in degrees),
/// <c>Exif.Photo.PixelXDimension</c> and <c>PixelYDimension</c>: fields of one record; they are
/// set, not removed.</item>
/// <item><c>Exif.Photo.DateTimeOriginal</c>, stored as seconds since 1970 without a time zone;
/// <c>Exif.Photo.ColorSpace</c>; <c>Exif.Photo.SubjectDistance</c> (stored as a LONG).</item>
/// <item>The tags of the Canon MakerNote the file has, by key
/// (<see cref="ExifEdit.SetByKey"/>, <see cref="ExifEdit.SetTextByKey"/>,
/// <see cref="ExifEdit.RemoveByKey"/>): <c>Exif.Canon.OwnerName</c>,
/// <c>Exif.CanonCs.Quality</c> and the others, under the rules of every Canon MakerNote.</item>
/// <item>The thumbnail (<see cref="ExifEdit.SetThumbnail"/>, <see cref="ExifEdit.RemoveThumbnail"/>).</item>
/// <item>The comment (<see cref="SaveComment"/>).</item>
/// </list>
/// <para><c>Exif.Photo.FNumber</c> and <c>ExposureTime</c> are computed from the Canon shot
/// information and follow it; they and every tag not listed are refused with
/// <see cref="MetadataErrorCode.TagNotEditable"/>. Removal of the whole EXIF is refused.</para>
/// <para>The heap is written again whole, in the layout the reference writes. Every record the
/// edit does not name keeps its bytes and its place in its directory; the bytes before the heap
/// are kept. The output is read again before it is returned: its records have to be the planned
/// ones, the tags that were set have to read as set, and every other tag and the comment as before
/// (<see cref="MetadataErrorCode.UnsafeRelocation"/> otherwise).</para>
/// </remarks>
public sealed class CrwMetadataDocument : IDisposable
{
    private static readonly ushort[] Root = [];
    private static readonly ushort[] Properties = [0x300A];
    private static readonly ushort[] Camera = [0x300A, 0x2807];
    private static readonly ushort[] Exposure = [0x300A, 0x3002];
    private static readonly ushort[] Image = [0x300A, 0x300B];

    private static readonly ExifTagKey Make = new(ExifDirectoryKind.Ifd0, 0x010F);
    private static readonly ExifTagKey Model = new(ExifDirectoryKind.Ifd0, 0x0110);
    private static readonly ExifTagKey Orientation = new(ExifDirectoryKind.Ifd0, 0x0112);
    private static readonly ExifTagKey Width = new(ExifDirectoryKind.Exif, 0xA002);
    private static readonly ExifTagKey Height = new(ExifDirectoryKind.Exif, 0xA003);
    private static readonly ExifTagKey Date = new(ExifDirectoryKind.Exif, 0x9003);
    private static readonly ExifTagKey ColorSpace = new(ExifDirectoryKind.Exif, 0xA001);
    private static readonly ExifTagKey Distance = new(ExifDirectoryKind.Exif, 0x9206);
    private static readonly ExifTagKey FNumber = new(ExifDirectoryKind.Exif, 0x829D);
    private static readonly ExifTagKey ExposureTime = new(ExifDirectoryKind.Exif, 0x829A);

    private readonly byte[] file;
    private readonly MetadataLimits limits;
    private readonly string? sourcePath;
    private readonly CrwMetadata metadata;
    private bool disposed;

    private CrwMetadataDocument(byte[] file, MetadataLimits limits, string? sourcePath)
    {
        this.file = file;
        this.limits = limits;
        this.sourcePath = sourcePath;
        metadata = CrwReader.Read(file, limits);
        // The writer reads the heap as a tree; a file it cannot hold so is refused when it is opened.
        _ = CiffHeap.Parse(file, limits, out _, out _);
    }

    /// <summary>The tags of the file, as <see cref="CrwMetadata.Exif"/> gives them.</summary>
    public ExifMetadata? Exif => metadata.Exif;

    /// <summary>The comment of the file, or null when it has none.</summary>
    public string? Comment => metadata.Comment;

    /// <summary>Opens a CRW file from a stream, which is read whole from where it stands and may be closed afterwards.</summary>
    /// <exception cref="MetadataException">
    /// As <see cref="CrwMetadata.Read"/>; <see cref="MetadataErrorCode.UnsupportedFeature"/> for a
    /// directory that lies in its own entry.
    /// </exception>
    public static CrwMetadataDocument Open(Stream source, MetadataLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new MetadataLimits();
        limits.Validate();
        return new CrwMetadataDocument(ReadAll(source, limits), limits, null);
    }

    /// <summary>Opens a CRW file by its path; the file is read whole and not kept open.</summary>
    /// <exception cref="MetadataException">As <see cref="Open(Stream, MetadataLimits)"/>.</exception>
    public static CrwMetadataDocument Open(string path, MetadataLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        limits ??= new MetadataLimits();
        limits.Validate();
        string full = Path.GetFullPath(path);
        using var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new CrwMetadataDocument(ReadAll(source, limits), limits, full);
    }

    // Read all from bounded CIFF data, validating its declared extent before consuming fields.
    private static byte[] ReadAll(Stream source, MetadataLimits limits)
    {
        var buffer = new MemoryStream();
        byte[] chunk = new byte[limits.CopyBufferBytes];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limits.MaxInputBytes)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The CRW file exceeds the input budget.", 0);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Writes the file with the tags of <paramref name="edit"/> changed to an empty, seekable stream.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.TagNotEditable"/> for a tag that has no record in a CRW file;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a value of a type or a form its record
    /// does not hold, for the removal of a tag that is a field of a record, for a key of the Canon
    /// MakerNote the file does not have, and for the removal of the whole EXIF;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded;
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the output does not read back as planned.
    /// </exception>
    public void SaveExif(Stream destination, ExifEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(Stream.Null, destination);
        byte[] output = Apply(edit.Snapshot(), false, null);
        ContainerIo.Write(destination, output, false, default).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>As <see cref="SaveExif"/>, to a new file.</summary>
    public void SaveExifAs(string destinationPath, ExifEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        Publish(target, Apply(edit.Snapshot(), false, null));
    }

    /// <summary>
    /// Writes the file with its comment set to an empty, seekable stream. The comment is stored as
    /// UTF-8 in the record the file has for it, which keeps its length when the comment is
    /// shorter, as the reference keeps it; null or an empty text clears the record and leaves it in
    /// place, which reads as no comment.
    /// </summary>
    /// <exception cref="ArgumentException">The comment holds a NUL character.</exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.CommentTooLarge"/> for a comment longer than
    /// <see cref="MetadataLimits.MaxValueBytes"/>; <see cref="MetadataErrorCode.UnsafeRelocation"/>
    /// when the output does not read back as planned.
    /// </exception>
    public void SaveComment(Stream destination, string? comment)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(Stream.Null, destination);
        byte[] output = Apply(null, true, comment);
        ContainerIo.Write(destination, output, false, default).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>As <see cref="SaveComment"/>, to a new file.</summary>
    public void SaveCommentAs(string destinationPath, string? comment)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        Publish(target, Apply(null, true, comment));
    }

    /// <summary>The document holds no handle; disposing it only ends its use.</summary>
    public void Dispose() => disposed = true;

    private void Publish(string target, byte[] output) =>
        ContainerIo.Publish(target, limits.CopyBufferBytes, false, default,
            stream => ContainerIo.Write(stream, output, false, default).AsTask()).GetAwaiter().GetResult();

    private static MetadataException Unsupported(string message) => new(MetadataErrorCode.UnsupportedFeature, message);

    private static MetadataException Unsafe(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The CRW file was not written: " + what + ".");

    /// <summary>What the output has to read as: the tags that were named, and what follows from the edit.</summary>
    private sealed class Plan
    {
        /// <summary>The tags that were named: the value each has to read as, null for a removed one.</summary>
        internal Dictionary<ExifTagKey, ExifValue?> Named { get; } = [];
        /// <summary>The Canon tags the note has to read as, by tag; null when the note was not changed.</summary>
        internal Dictionary<ushort, byte[]>? Canon { get; set; }
        internal bool Thumbnail { get; set; }
        internal int ThumbnailLength { get; set; } = -1;
        internal bool CommentChanged { get; set; }
        internal string? Comment { get; set; }
    }

    // Apply the requested changes to a staged copy and preserve unrelated CIFF bytes.
    private byte[] Apply(ExifEdit.EditSnapshot? edit, bool setComment, string? comment)
    {
        CiffRecord root = CiffHeap.Parse(file, limits, out bool little, out int heap);
        Endianness order = little ? Endianness.LittleEndian : Endianness.BigEndian;
        var plan = new Plan();
        if (setComment) SetComment(root, comment, plan);
        if (edit is not null)
        {
            if (edit.RemoveAll)
                throw Unsupported("The tags of a CRW file are records of its heap: they are removed one by one, not as a whole.");
            Tags(root, edit, order, plan);
            if (edit.Thumbnail is byte[] jpeg)
            {
                CiffHeap.Ensure(root, Root, 0x2008).Set((byte[])jpeg.Clone(), toHeap: true);
                (plan.Thumbnail, plan.ThumbnailLength) = (true, jpeg.Length);
            }
            else if (edit.ThumbnailRemoved)
            {
                CiffHeap.Remove(root, Root, 0x2008);
                plan.Thumbnail = true;
            }
            if (edit.MakerNote.Count != 0) Canon(root, edit.MakerNote, plan);
        }
        byte[] output = CiffHeap.Write(file.AsSpan(0, heap), root, little);
        if (output.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The edited CRW file exceeds the input budget.");
        Verify(output, root, heap, plan);
        return output;
    }

    private void SetComment(CiffRecord root, string? comment, Plan plan)
    {
        plan.CommentChanged = true;
        CiffRecord? record = CiffHeap.Find(root, Properties, 0x0805);
        if (string.IsNullOrEmpty(comment))
        {
            // As the reference does: the record stays and holds nothing.
            record?.Set(new byte[record.Data.Length]);
            return;
        }
        if (comment.Contains('\0')) throw new ArgumentException("A comment holds no NUL character.", nameof(comment));
        byte[] text = Encoding.UTF8.GetBytes(comment);
        if (text.Length > limits.MaxValueBytes)
            throw new MetadataException(MetadataErrorCode.CommentTooLarge, "The comment exceeds the value budget.");
        byte[] value = new byte[Math.Max(text.Length, record is null || record.InEntry ? 0 : record.Data.Length)];
        text.CopyTo(value, 0);
        CiffHeap.Ensure(root, Properties, 0x0805).Set(value, toHeap: true);
        // What the reader makes of the bytes: UTF-8 when well formed, which they are.
        plan.Comment = comment;
    }

    private void Tags(CiffRecord root, ExifEdit.EditSnapshot edit, Endianness order, Plan plan)
    {
        bool little = order == Endianness.LittleEndian;
        foreach ((ExifTagKey key, ExifValue? given) in edit.Changes)
        {
            ExifValue? value = given is null ? null : ExifEdit.ConvertOrder(given, order);
            if (value is not null && value.ByteLength > limits.MaxValueBytes)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "A value of the edit exceeds the value budget.");
            plan.Named[key] = value;
            if (key == Make || key == Model) continue;
            if (key == Orientation || key == Width || key == Height)
            {
                if (value is null)
                    throw Unsupported($"Tag 0x{key.TagId:X4} is a field of the image record of a CRW file: it is set, not removed.");
                CiffRecord record = CiffHeap.Ensure(root, Properties, 0x1810);
                byte[] data = record.Data.Length == 0 ? new byte[28] : (byte[])record.Data.Clone();
                if (data.Length < 28 || record.InEntry)
                    throw Unsupported("The image record of the CRW file is shorter than its fields: it is not written.");
                if (key == Orientation)
                {
                    if (value.FieldType != TiffFieldType.Short || value.Count != 1)
                        throw Unsupported("The orientation of a CRW file is one SHORT.");
                    ushort orientation = little ? BinaryPrimitives.ReadUInt16LittleEndian(value.RawBytes) : BinaryPrimitives.ReadUInt16BigEndian(value.RawBytes);
                    int degrees = orientation switch
                    {
                        1 => 0, 3 => 180, 6 => 90, 8 => 270,
                        _ => throw Unsupported("A CRW file stores the orientation as a rotation: 1, 3, 6 and 8 are the orientations it holds.")
                    };
                    if (little) BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), degrees); else BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), degrees);
                }
                else
                {
                    if (value.FieldType != TiffFieldType.Long || value.Count != 1)
                        throw Unsupported("A pixel dimension of a CRW file is one LONG.");
                    value.RawBytes.CopyTo(data.AsSpan(key == Width ? 0 : 4));
                }
                record.Set(data);
            }
            else if (key == Date)
            {
                if (value is null) { CiffHeap.Remove(root, Properties, 0x180E); continue; }
                string text = value.FieldType == TiffFieldType.Ascii ? Encoding.ASCII.GetString(value.RawBytes).TrimEnd('\0') : "";
                if (!DateTime.TryParseExact(text, "yyyy':'MM':'dd HH':'mm':'ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
                    throw Unsupported("The date of a CRW file is an ASCII value of the form YYYY:MM:DD HH:MM:SS.");
                long seconds = (long)(DateTime.SpecifyKind(time, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;
                if (seconds <= 0 || seconds > uint.MaxValue)
                    throw Unsupported("The date of a CRW file is stored as seconds since 1970 in 32 bits: this date is outside them.");
                CiffRecord record = CiffHeap.Ensure(root, Properties, 0x180E);
                // The record of a camera has twelve bytes; the eight after the seconds are kept.
                byte[] data = record.Data.Length >= 8 ? (byte[])record.Data.Clone() : new byte[12];
                if (little) BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)seconds); else BinaryPrimitives.WriteUInt32BigEndian(data, (uint)seconds);
                record.Set(data);
            }
            else if (key == ColorSpace) Basic(root, Image, 0x10B4, TiffFieldType.Short, value);
            else if (key == Distance) Basic(root, Exposure, 0x1807, TiffFieldType.Long, value);
            else if (key == FNumber || key == ExposureTime)
                throw new MetadataException(MetadataErrorCode.TagNotEditable,
                    $"Tag 0x{key.TagId:X4} of a CRW file is computed from the Canon shot information (Exif.CanonSi.ApertureValue, ShutterSpeedValue): it is not edited as a value.");
            else
                throw new MetadataException(MetadataErrorCode.TagNotEditable,
                    $"Tag 0x{key.TagId:X4} of the {key.Directory} directory has no record in a CRW file: it is not written.");
        }
        if (plan.Named.ContainsKey(Make) || plan.Named.ContainsKey(Model)) MakeAndModel(root, plan);
    }

    /// <summary>A tag whose value is the whole of a record.</summary>
    private static void Basic(CiffRecord root, ushort[] path, ushort id, TiffFieldType type, ExifValue? value)
    {
        if (value is null) { CiffHeap.Remove(root, path, id); return; }
        if (value.FieldType != type || value.ByteLength == 0)
            throw Unsupported($"Record 0x{id:X4} of a CRW file holds a value of the type {type}, and not an empty one.");
        // A value that lies in its entry is read as the eight bytes of the entry: another length goes to the heap.
        CiffHeap.Ensure(root, path, id).Set(value.RawBytes.ToArray(), toHeap: value.ByteLength != 8);
    }

    /// <summary>The make and the model are one record: the make, a zero byte, the model, a zero byte.</summary>
    private void MakeAndModel(CiffRecord root, Plan plan)
    {
        byte[]? Part(ExifTagKey key)
        {
            if (plan.Named.TryGetValue(key, out ExifValue? named))
            {
                if (named is null) return null;
                ReadOnlySpan<byte> bytes = named.RawBytes;
                if (named.FieldType != TiffFieldType.Ascii || bytes.Length == 0 || bytes.IndexOf((byte)0) != bytes.Length - 1)
                    throw Unsupported("The make and the model of a CRW file are ASCII values that end with their only zero byte.");
                return bytes.ToArray();
            }
            return metadata.Exif is not null && metadata.Exif.TryGetEntry(key, out ExifEntry? entry) ? entry.Value.RawBytes.ToArray() : null;
        }

        byte[]? make = Part(Make), model = Part(Model);
        if (make is null && model is null) { CiffHeap.Remove(root, Camera, 0x080A); return; }
        if (make is null || model is null || make.Length == 0 || make[^1] != 0)
            throw Unsupported("The make and the model of a CRW file are one record: both are set or both are removed.");
        CiffHeap.Ensure(root, Camera, 0x080A).Set([.. make, .. model], toHeap: true);
    }

    /// <summary>
    /// The tags of the Canon MakerNote: changed in the note the records are laid out as, by the
    /// writer of every MakerNote, and each record whose tag reads differently afterwards is given
    /// the bytes of its tag.
    /// </summary>
    private void Canon(CiffRecord root, IReadOnlyList<ExifEdit.MakerNoteChange> changes, Plan plan)
    {
        if (metadata.Canon.Count == 0)
            throw Unsupported("The CRW file has no record of the Canon MakerNote: its tags cannot be changed.");
        byte[] block = CrwCanonNote.Block(metadata.Little, metadata.Canon);
        byte[] edited = MakerNoteWriter.Apply(block, changes, limits);
        MakerNote note = ExifTiffReader.Read(edited, limits).Metadata.MakerNote
            ?? throw Unsafe("the Canon tags do not read after the change");
        plan.Canon = [];
        foreach (MakerNoteEntry entry in note.Entries) plan.Canon[entry.TagId] = entry.Value.RawBytes.ToArray();
        foreach ((ushort tag, ushort id, ushort[] path, int fixedLength) in CrwCanonNote.Records)
        {
            bool had = metadata.Canon.TryGetValue(tag, out (TiffFieldType Type, byte[] Bytes) before);
            bool has = plan.Canon.TryGetValue(tag, out byte[]? after);
            if (!had && !has) continue;
            if (had && !has) { CiffHeap.Remove(root, path, id); continue; }
            if (!had || before.Bytes.AsSpan().SequenceEqual(after!)) continue;
            CiffRecord record = CiffHeap.Find(root, path, id)
                ?? throw Unsupported($"Record 0x{id:X4} of the CRW file is not in the directory the reference writes it to: it is not changed.");
            if (fixedLength != 0)
            {
                // The tag is the first bytes of the record; what follows them is kept.
                if (after!.Length != fixedLength || record.Data.Length < fixedLength)
                    throw Unsupported($"Record 0x{id:X4} of a CRW file holds a value of {fixedLength} bytes.");
                byte[] data = (byte[])record.Data.Clone();
                after.CopyTo(data, 0);
                record.Set(data);
            }
            else record.Set(after!, toHeap: after!.Length != 8);
        }
    }

    private void Verify(byte[] output, CiffRecord planned, int heap, Plan plan)
    {
        if (!output.AsSpan(0, heap).SequenceEqual(file.AsSpan(0, heap))) throw Unsafe("the bytes before the heap changed");
        CrwMetadata after;
        CiffRecord written;
        try
        {
            written = CiffHeap.Parse(output, limits, out _, out _);
            after = CrwReader.Read(output, limits);
        }
        catch (MetadataException failure)
        {
            throw Unsafe("the output is refused by the reader (" + failure.Code + ")");
        }
        if (!CiffHeap.Same(planned, written)) throw Unsafe("the records of the output are not the planned ones");

        string? comment = plan.CommentChanged ? plan.Comment : metadata.Comment;
        if (!string.Equals(comment, after.Comment, StringComparison.Ordinal)) throw Unsafe("the comment does not read as planned");

        var before = (metadata.Exif?.Entries ?? []).ToDictionary(entry => entry.Key, entry => entry.Value);
        var now = (after.Exif?.Entries ?? []).ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach ((ExifTagKey key, ExifValue? value) in plan.Named)
        {
            now.TryGetValue(key, out ExifValue? read);
            if (value is null ? read is not null : read is null || !Equal(value, read))
                throw Unsafe($"tag 0x{key.TagId:X4} of the {key.Directory} directory does not read as set");
        }
        foreach (ExifTagKey key in before.Keys.Union(now.Keys))
        {
            if (plan.Named.ContainsKey(key)) continue;
            // The aperture and the exposure time follow the shot information, and the tags of the thumbnail its record.
            if (plan.Canon is not null && (key == FNumber || key == ExposureTime)) continue;
            if (plan.Thumbnail && key.Directory == ExifDirectoryKind.Thumbnail) continue;
            if (!before.TryGetValue(key, out ExifValue? old) || !now.TryGetValue(key, out ExifValue? read) || !Equal(old, read))
                throw Unsafe($"tag 0x{key.TagId:X4} of the {key.Directory} directory, which the edit does not name, reads differently");
        }
        if (plan.Thumbnail && (plan.ThumbnailLength < 0 ? after.ThumbnailOffset is not null : after.ThumbnailLength != plan.ThumbnailLength))
            throw Unsafe("the thumbnail does not read as set");

        var canon = plan.Canon ?? metadata.Canon.ToDictionary(pair => pair.Key, pair => pair.Value.Bytes);
        if (canon.Count != after.Canon.Count) throw Unsafe("the Canon tags of the output are not the planned ones");
        foreach ((ushort tag, byte[] bytes) in canon)
            if (!after.Canon.TryGetValue(tag, out (TiffFieldType Type, byte[] Bytes) read) || !bytes.AsSpan().SequenceEqual(read.Bytes))
                throw Unsafe($"tag 0x{tag:X4} of the Canon MakerNote does not read as planned");
        if ((metadata.PreviewOffset is null) != (after.PreviewOffset is null) || metadata.PreviewLength != after.PreviewLength ||
            (metadata.PreviewOffset is long source && after.PreviewOffset is long target &&
             !file.AsSpan((int)source, (int)metadata.PreviewLength).SequenceEqual(output.AsSpan((int)target, (int)after.PreviewLength))))
            throw Unsafe("the preview changed");
    }

    private static bool Equal(ExifValue left, ExifValue right) =>
        left.FieldType == right.FieldType && left.Count == right.Count && left.RawBytes.SequenceEqual(right.RawBytes);
}
