using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;

namespace MetadataSharp.Formats.Crw;

/// <summary>
/// Walks the heap of a Canon CRW file (CIFF) and gives the records the reference maps to EXIF tags
/// as those tags. A CIFF file has no EXIF block: a directory lies at the end of its block, each of
/// its entries of ten bytes names a record by a tag whose upper bits say what the record holds and
/// whether its eight bytes of data lie in the entry itself, and a record may be a directory again.
/// Offsets count from the start of the block of the directory that holds the entry.
/// </summary>
/// <remarks>
/// The rules of the walk are those of the reference (Exiv2 0.28.9, crwimage_int.cpp), so that a
/// file it reads is read here and gives the same tags: which records are mapped
/// (<c>CrwMap::crwMapping_</c>), how long a text is (to its first zero byte), which records are
/// arrays of the Canon MakerNote, and the two tags it computes from the shot information. Where
/// the reference lists a key twice, the first stands here.
/// </remarks>
internal ref struct CrwReader
{
    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private readonly bool little;
    private readonly Endianness order;
    private readonly List<ExifEntry> entries = [];
    private readonly HashSet<ExifTagKey> keys = [];
    private readonly SortedDictionary<ushort, (TiffFieldType Type, byte[] Bytes)> canon = [];
    private int records;
    private long retained;
    private string? comment;
    private long previewOffset = -1;
    private long previewLength;
    private long thumbnailOffset = -1;
    private long thumbnailLength;

    private CrwReader(ReadOnlySpan<byte> data, MetadataLimits limits, bool little)
    {
        this.data = data;
        this.limits = limits;
        this.little = little;
        order = little ? Endianness.LittleEndian : Endianness.BigEndian;
    }

    /// <summary>The fourteen bytes every CRW file starts with: the byte order twice, the length of the header, <c>HEAPCCDR</c>.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 14 && header[0] == header[1] && (header[0] == 0x49 || header[0] == 0x4D) && header.Slice(6, 8).SequenceEqual("HEAPCCDR"u8);

    internal static CrwMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits)
    {
        limits ??= new MetadataLimits();
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The CRW file exceeds the input budget.", 0);
        if (!HasSignature(data)) throw Fail("The file does not start with the header of a CRW file.", 0);
        var reader = new CrwReader(data, limits, data[0] == 0x49);
        uint heap = reader.U32(2);
        if (heap < 14 || heap > (uint)data.Length) throw Fail("The header of the CRW file leads outside the file.", 2);
        reader.Directory((int)heap, data.Length - (int)heap, 0, 0);
        return reader.Result();
    }

    private static MetadataException Fail(string message, long offset) => new(MetadataErrorCode.InvalidCrw, message, offset);

    private readonly ushort U16(int at) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));

    private readonly uint U32(int at) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    /// <summary>One directory: the block at <paramref name="start"/> of <paramref name="size"/> bytes, whose last four bytes say where its entries are.</summary>
    private void Directory(int start, int size, ushort tag, int depth)
    {
        if (depth > limits.MaxDirectoryDepth)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The directories of the CRW file are nested deeper than the budget allows.", start);
        if (size < 4) throw Fail("A directory of the CRW file is too short.", start);
        uint at = U32(start + size - 4);
        if (at > (uint)(size - 2)) throw Fail("The entries of a directory of the CRW file lie outside it.", start + size - 4);
        int count = U16(start + (int)at);
        long first = at + 2;
        if (count * 10L > size - first) throw Fail("A directory of the CRW file has more entries than it has room for.", start + at);
        for (int i = 0; i < count; i++)
        {
            if (++records > limits.MaxTotalEntries)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The CRW file exceeds the entry budget.", start + first);
            // Where the entry lies in the block, which is what its offsets are held against.
            long entry = first + (i * 10L);
            ushort recordTag = U16(start + (int)entry);
            long offset, length;
            switch (recordTag & 0xC000)
            {
                case 0x0000:
                    length = U32(start + (int)entry + 2);
                    offset = U32(start + (int)entry + 6);
                    // The record lies before the entry or after it, and never over it: a directory
                    // that held itself would be walked without end.
                    bool fits = offset < entry
                        ? length <= entry - offset
                        : offset >= entry + 10 && offset <= size && length <= size - offset;
                    if (!fits) throw Fail("A record of the CRW file lies outside its directory or over its own entry.", start + entry);
                    break;
                case 0x4000:
                    length = 8;
                    offset = entry + 2;
                    break;
                default:
                    throw Fail("An entry of the CRW file says neither that its data lies in the entry nor that it lies in the heap.", start + entry);
            }
            int where = start + (int)offset;
            if ((recordTag & 0x3800) is 0x2800 or 0x3000) Directory(where, (int)length, recordTag, depth + 1);
            else Record(tag, (ushort)(recordTag & 0x3FFF), where, (int)length);
        }
    }

    private static TiffFieldType TypeOf(ushort record) => (record & 0x3800) switch
    {
        0x0000 => TiffFieldType.Byte,
        0x0800 => TiffFieldType.Ascii,
        0x1000 => TiffFieldType.Short,
        0x1800 => TiffFieldType.Long,
        _ => TiffFieldType.Undefined
    };

    /// <summary>One record that is no directory, by the directory that holds it and its tag without the two bits of its storage.</summary>
    private void Record(ushort directory, ushort record, int at, int length)
    {
        switch ((directory, record))
        {
            case (0x300A, 0x0805):
                if (length > limits.MaxValueBytes)
                    throw new MetadataException(MetadataErrorCode.LimitExceeded, "The comment of the CRW file exceeds the value budget.", at);
                Retain(length, at);
                // A record of nothing but zero bytes is no comment.
                ReadOnlySpan<byte> written = data.Slice(at, length).TrimEnd((byte)0);
                if (comment is null && written.Length != 0) comment = Comments.Decode(written);
                break;
            case (0x2807, 0x080A):
            {
                // The make, a zero byte, the model.
                int make = TextLength(at, length);
                Add(ExifDirectoryKind.Ifd0, 0x010F, TiffFieldType.Ascii, Text(at, make));
                int rest = TextLength(at + make, length - make);
                Add(ExifDirectoryKind.Ifd0, 0x0110, TiffFieldType.Ascii, Text(at + make, rest));
                break;
            }
            case (0x3004, 0x080B): Canon(0x0007, record, at, length); break;
            case (0x2807, 0x0810): Canon(0x0009, record, at, length); break;
            case (0x2804, 0x0815): Canon(0x0006, record, at, length); break;
            case (0x300B, 0x1029): Canon(0x0002, record, at, length); break;
            case (0x300B, 0x102A): CanonArray(0x0004, record, at, length); break;
            case (0x300B, 0x102D): CanonArray(0x0001, record, at, length); break;
            case (0x300B, 0x1033): CanonArray(0x000F, record, at, length); break;
            case (0x300B, 0x1038): CanonArray(0x0012, record, at, length); break;
            case (0x300B, 0x10A9): Canon(0x00A9, record, at, length); break;
            case (0x300B, 0x10B4): Add(ExifDirectoryKind.Exif, 0xA001, TypeOf(record), Basic(record, at, length, 0)); break;
            case (0x300B, 0x10B5): Canon(0x00B5, record, at, length); break;
            case (0x300B, 0x10C0): Canon(0x00C0, record, at, length); break;
            case (0x300B, 0x10C1): Canon(0x00C1, record, at, length); break;
            case (0x3002, 0x1807): Add(ExifDirectoryKind.Exif, 0x9206, TypeOf(record), Basic(record, at, length, 0)); break;
            case (0x3004, 0x180B): Canon(0x000C, record, at, length); break;
            case (0x300A, 0x180E):
                if (length < 8) { Add(ExifDirectoryKind.Exif, 0x9003, TypeOf(record), Basic(record, at, length, 0)); break; }
                // Seconds since 1970, written as the time of the camera's clock.
                string time = DateTime.UnixEpoch.AddSeconds(U32(at)).ToString("yyyy':'MM':'dd HH':'mm':'ss", CultureInfo.InvariantCulture);
                Add(ExifDirectoryKind.Exif, 0x9003, TiffFieldType.Ascii, [.. Encoding.ASCII.GetBytes(time), 0]);
                break;
            case (0x300A, 0x1810):
                if (length < 28) { Add(ExifDirectoryKind.Exif, 0xA002, TypeOf(record), Basic(record, at, length, 0)); break; }
                Add(ExifDirectoryKind.Exif, 0xA002, TiffFieldType.Long, data.Slice(at, 4).ToArray());
                Add(ExifDirectoryKind.Exif, 0xA003, TiffFieldType.Long, data.Slice(at + 4, 4).ToArray());
                // The rotation in degrees, as the orientation it stands for; any other number is upright.
                ushort orientation = (int)U32(at + 12) switch { 180 or -180 => (ushort)3, 90 or -270 => (ushort)6, 270 or -90 => (ushort)8, _ => (ushort)1 };
                Add(ExifDirectoryKind.Ifd0, 0x0112, TiffFieldType.Short, Short(orientation));
                break;
            case (0x300A, 0x1817): Canon(0x0008, record, at, length, 4); break;
            case (0x300B, 0x183B): Canon(0x0015, record, at, length); break;
            case (0x0000, 0x2008):
                if (thumbnailOffset < 0) (thumbnailOffset, thumbnailLength) = (at, length);
                break;
            case (0x0000, 0x2007):
                if (previewOffset < 0) (previewOffset, previewLength) = (at, length);
                break;
        }
    }

    private void Retain(long bytes, long at)
    {
        retained += bytes;
        if (retained > limits.MaxRetainedMetadataBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The records of the CRW file exceed the retained metadata budget.", at);
    }

    /// <summary>The bytes up to and with the first zero byte, or all of them.</summary>
    private readonly int TextLength(int at, int length)
    {
        int zero = data.Slice(at, length).IndexOf((byte)0);
        return zero < 0 ? length : zero + 1;
    }

    /// <summary>A text as an ASCII value: with a zero byte at its end unless it is empty.</summary>
    private readonly byte[] Text(int at, int length)
    {
        ReadOnlySpan<byte> text = data.Slice(at, length);
        return text.Length == 0 || text[^1] == 0 ? text.ToArray() : [.. text, 0];
    }

    /// <summary>The value of a record as the reference reads it: whole elements of the type the tag names.</summary>
    private readonly byte[] Basic(ushort record, int at, int length, int fixedLength)
    {
        TiffFieldType type = TypeOf(record);
        if (fixedLength != 0)
        {
            if (fixedLength > length) throw Fail("A record of the CRW file is shorter than the value it is defined to hold.", at);
            length = fixedLength;
        }
        else if (type == TiffFieldType.Ascii) return Text(at, TextLength(at, length));
        int width = type is TiffFieldType.Short ? 2 : type is TiffFieldType.Long ? 4 : 1;
        return data.Slice(at, length - (length % width)).ToArray();
    }

    private readonly byte[] Short(ushort value)
    {
        byte[] bytes = new byte[2];
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); else BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private readonly byte[] Long(uint value)
    {
        byte[] bytes = new byte[4];
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); else BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private void Add(ExifDirectoryKind directory, ushort tag, TiffFieldType type, byte[] bytes)
    {
        var key = new ExifTagKey(directory, tag);
        if (bytes.Length > limits.MaxValueBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "A record of the CRW file exceeds the value budget.", 0);
        if (!keys.Add(key)) return;
        Retain(bytes.Length, 0);
        int width = type is TiffFieldType.Short ? 2 : type is TiffFieldType.Long ? 4 : type is TiffFieldType.Rational ? 8 : 1;
        entries.Add(new ExifEntry(key, ExifValue.FromRaw(type, (uint)(bytes.Length / width), order, bytes)));
    }

    /// <summary>A record that is a tag of the Canon MakerNote.</summary>
    private void Canon(ushort tag, ushort record, int at, int length, int fixedLength = 0)
    {
        byte[] bytes = Basic(record, at, length, fixedLength);
        if (bytes.Length > limits.MaxValueBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "A record of the CRW file exceeds the value budget.", at);
        if (canon.ContainsKey(tag)) return;
        Retain(bytes.Length, at);
        canon.Add(tag, (TypeOf(record), bytes));
    }

    /// <summary>
    /// A record that is one of the arrays of the Canon MakerNote (camera settings, shot information,
    /// custom functions, picture information). The shot information also gives the aperture and the
    /// exposure time as the two EXIF tags, computed as the reference computes them.
    /// </summary>
    private void CanonArray(ushort tag, ushort record, int at, int length)
    {
        if ((length & 1) != 0) throw Fail("An array of the CRW file has half an element.", at);
        bool first = !canon.ContainsKey(tag);
        Canon(tag, record, at, length);
        if (tag != 0x0004 || !first) return;
        int count = length / 2;
        long aperture = count > 21 ? U16(at + 42) : 0, shutter = count > 22 ? U16(at + 44) : 0;

        float number = MathF.Exp(MathF.Log(2.0F) * Ev(aperture) / 2.0F);
        if (Math.Abs(number - 3.5) < 0.1) number = 3.5F;
        (int numerator, int denominator) = Rational(number);
        Add(ExifDirectoryKind.Exif, 0x829D, TiffFieldType.Rational, [.. Long((uint)numerator), .. Long((uint)denominator)]);

        uint top = 1, bottom = 1;
        double time = Math.Exp(Math.Log(2.0) * Ev(shutter));
        if (time > 1)
        {
            double rounded = Math.Round(time, MidpointRounding.AwayFromZero);
            if (rounded <= uint.MaxValue) bottom = (uint)rounded;
        }
        else
        {
            double rounded = Math.Round(1 / time, MidpointRounding.AwayFromZero);
            if (rounded >= 0 && rounded <= uint.MaxValue) top = (uint)rounded;
        }
        Add(ExifDirectoryKind.Exif, 0x829A, TiffFieldType.Rational, [.. Long(top), .. Long(bottom)]);
    }

    /// <summary>Canon's exposure value: thirty-seconds of a stop, with the codes for a third and two thirds.</summary>
    private static float Ev(long value)
    {
        long remainder = value & 0x1F;
        value -= remainder;
        float fraction = remainder;
        if (remainder == 0x0C) fraction = 32.0F / 3;
        else if (remainder == 0x14) fraction = 64.0F / 3;
        else if (value == 160 && remainder == 0x08) fraction = 30.0F / 3;
        return (value + fraction) / 32.0F;
    }

    /// <summary>A number as a fraction over a power of ten, reduced, as the reference turns it.</summary>
    private static (int Numerator, int Denominator) Rational(float value)
    {
        double number = value;
        int denominator;
        if (Math.Abs(number) <= int.MaxValue / 1000000) denominator = 1000000;
        else if (Math.Abs(number) <= int.MaxValue / 10000) denominator = 10000;
        else if (Math.Abs(number) <= int.MaxValue / 100) denominator = 100;
        else if (Math.Abs(number) <= int.MaxValue) denominator = 1;
        else return (number > 0 ? 1 : -1, 0);
        int numerator = (int)Math.Round(number * denominator, MidpointRounding.AwayFromZero);
        int a = Math.Abs(numerator), b = denominator;
        while (b != 0) (a, b) = (b, a % b);
        return a == 0 ? (numerator, denominator) : (numerator / a, denominator / a);
    }

    private CrwMetadata Result()
    {
        if (thumbnailOffset >= 0)
        {
            // As the reference lists a thumbnail it holds apart from the tags: a JPEG stream at no offset.
            Add(ExifDirectoryKind.Thumbnail, 0x0103, TiffFieldType.Short, Short(6));
            Add(ExifDirectoryKind.Thumbnail, 0x0201, TiffFieldType.Long, Long(0));
            Add(ExifDirectoryKind.Thumbnail, 0x0202, TiffFieldType.Long, Long((uint)thumbnailLength));
        }
        MakerNote? note = Note();
        ExifMetadata? exif = entries.Count == 0 && note is null ? null : new ExifMetadata(order, entries, note, editable: false);
        return new CrwMetadata(exif, comment, previewOffset < 0 ? null : previewOffset, previewOffset < 0 ? 0 : previewLength,
            thumbnailOffset < 0 ? null : thumbnailOffset, thumbnailOffset < 0 ? 0 : thumbnailLength) { Canon = canon, Little = little };
    }

    /// <summary>
    /// The records that are tags of the Canon MakerNote, laid out as such a note
    /// (<see cref="CrwCanonNote"/>) and read by the reader of that note, which names the tags and
    /// splits the arrays as it does in every other file.
    /// </summary>
    private readonly MakerNote? Note()
    {
        if (canon.Count == 0) return null;
        byte[] block = CrwCanonNote.Block(little, canon);
        MakerNote? note = CanonMakerNoteReader.TryRead(block, CrwCanonNote.Start, block.Length - CrwCanonNote.Start, order, limits,
            limits.MaxRetainedMetadataBytes - retained);
        // The directory was built here: it lies in no file, so the note has no place to report.
        if (note is not null) note.Offset = 0;
        return note;
    }
}
