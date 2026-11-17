using System.Buffers.Binary;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Whether tag 0x8773 of the first directory of a TIFF-based file is the profile of the image the
/// file is for. In a plain TIFF it is. In DNG and camera RAW files the first directory is commonly
/// a reduced image, while the raw data is elsewhere. One narrow DNG layout is safe: an un-reduced
/// RGB primary with its own accounted strip and a separate CFA SubIFD. Such a file is recognised
/// by what it holds, never by its name:
/// <list type="bullet">
/// <item>a header number other than 42: ORF and RW2;</item>
/// <item>the letters <c>CR</c> after the header, outside every directory and value: CR2;</item>
/// <item>DNGVersion (0xC612) in the first directory: DNG, refused unless the full-colour-primary
/// and separate raw SubIFD layout above is proven;</item>
/// <item>bit 0 of NewSubfileType (0x00FE) in the first directory: a reduced image, as the first
/// directory of a NEF or an ARW is;</item>
/// <item>raw sensor data in any image directory: a colour filter array or linear raw data
/// (PhotometricInterpretation 32803 or 34892), or the compression of an ARW, a NEF or a PEF
/// (32767, 34713, 65535), as in the first directory of a PEF and in the sub-directories of a NEF
/// or an ARW.</item>
/// </list>
/// The name of the maker alone decides nothing: cameras write plain TIFF files too.
/// </summary>
internal static class TiffBasedIccTarget
{
    private const ushort ClassicMagic = 42, BigTiffMagic = 43;
    private const ushort NewSubfileType = 0x00FE, Width = 0x0100, Length = 0x0101, Compression = 0x0103,
        Photometric = 0x0106, SamplesPerPixel = 0x0115, StripOffsets = 0x0111, StripByteCounts = 0x0117,
        SubIfds = 0x014A, DngVersion = 0xC612, IccProfile = 0x8773;
    // PhotometricInterpretation of sensor data, and the Compression numbers of ARW, NEF and PEF.
    private const long ColourFilterArray = 32803, LinearRaw = 34892, SonyRaw = 32767, NikonRaw = 34713, PentaxRaw = 65535;

    /// <summary>
    /// Why the profile of the first directory is not written, or null when the file is a plain
    /// TIFF as far as it can be read. The header and the first directory are read here, within the
    /// file and without the map, so that a raw file the map refuses is still refused for what it is.
    /// </summary>
    internal static string? Refusal(byte[] file, TiffBasedFileMap? map)
    {
        if (file.Length < 8) return null;
        bool big = file[0] == 0x4D;
        ushort magic = U16(file, 2, big);
        // BigTIFF is refused by the map, with its own words.
        if (magic == BigTiffMagic) return null;
        if (magic != ClassicMagic)
            return Because("the file has the header of a raw format (ORF or RW2)");

        uint first = U32(file, 4, big);
        if (first >= 12 && file.Length >= 12 && file[8] == (byte)'C' && file[9] == (byte)'R' && !Addressed(map, 8, 4))
            return Because("the file has the signature of a CR2");

        bool dng = false;
        if (first <= file.Length - 2)
        {
            bool reduced = false;
            int count = U16(file, (int)first, big);
            for (int index = 0; index < count; index++)
            {
                long entry = first + 2 + 12L * index;
                if (entry > file.Length - 12) break;
                int at = (int)entry;
                ushort tag = U16(file, at, big), type = U16(file, at + 2, big);
                dng |= tag == DngVersion;
                if (tag != NewSubfileType || U32(file, at + 4, big) != 1) continue;
                uint flags = type switch { 3 => U16(file, at + 8, big), 4 => U32(file, at + 8, big), _ => 0u };
                reduced |= (flags & 1) != 0;
            }
            if (reduced)
                return dng
                    ? Because("the file is a DNG (DNGVersion) whose primary directory is marked reduced")
                    : Because("the first directory is that of a reduced image (NewSubfileType)");
        }

        if (map is null) return dng ? Because("the file is a DNG whose primary image layout is not accounted for") : null;
        if (dng)
            return HasProvenDngColourPrimary(file, map, big)
                ? null
                : Because("the DNG (DNGVersion) primary directory is not a proven full-colour image ahead of a raw SubIFD");

        foreach (TiffBasedDirectory directory in map.Directories)
        {
            if (directory.Role is not (TiffBasedRole.Primary or TiffBasedRole.Thumbnail or TiffBasedRole.Other)) continue;
            if (HasRawSensorData(file, directory, big))
                return Because("the file holds raw sensor data (a colour filter array, linear raw data or the compression of a raw format)");
        }
        return null;
    }

    /// <summary>
    /// An existing, readable tag 0x8773 in the accounted primary directory is an explicit ICC
    /// target even in a camera RAW layout that would be unsafe for a new profile. Replacing or
    /// removing that tag keeps the profile attached to the same directory the source already names.
    /// </summary>
    internal static bool HasExistingPrimaryProfile(byte[] file, TiffBasedFileMap? map) =>
        map?.Find(TiffBasedRole.Primary)?.Find(IccProfile) is not null && IccProfiles.Read(file) is not null;

    // A narrow DNG insertion case: IFD0 is explicitly an un-reduced, uncompressed RGB image with
    // its own accounted strip, and a separate SubIFD carries the CFA raw samples. Inserting into
    // IFD0 therefore edits the rendered colour image, not a reduced thumbnail or the sensor data.
    private static bool HasProvenDngColourPrimary(byte[] file, TiffBasedFileMap map, bool big)
    {
        TiffBasedDirectory? primary = map.Find(TiffBasedRole.Primary);
        TiffBasedField? version = primary?.Find(DngVersion);
        if (primary is null || version is null || version.Type != 1 || version.Count != 4 || version.ByteLength != 4
            || version.ValueOffset < 0 || version.ValueOffset > file.Length - 4 || file[(int)version.ValueOffset] == 0
            || Number(file, primary.Find(NewSubfileType), big) != 0
            || Number(file, primary.Find(Compression), big) != 1
            || Number(file, primary.Find(Photometric), big) != 2
            || Number(file, primary.Find(SamplesPerPixel), big) != 3
            || Number(file, primary.Find(Width), big) <= 0 || Number(file, primary.Find(Length), big) <= 0
            || !HasSingleStrip(file, map, primary, big))
            return false;

        TiffBasedField? subIfds = primary.Find(SubIfds);
        if (subIfds is null || subIfds.Count != 1 || subIfds.Type is not (4 or 13)
            || subIfds.ValueOffset < 0 || subIfds.ValueOffset > file.Length - 4)
            return false;
        TiffBasedDirectory? raw = map.FindAt(U32(file, (int)subIfds.ValueOffset, big));
        return raw is not null && raw.Role == TiffBasedRole.Other && HasRawSensorData(file, raw, big)
            && HasSingleStrip(file, map, raw, big);
    }

    private static bool HasRawSensorData(byte[] file, TiffBasedDirectory directory, bool big) =>
        Number(file, directory.Find(Photometric), big) is ColourFilterArray or LinearRaw
        || Number(file, directory.Find(Compression), big) is SonyRaw or NikonRaw or PentaxRaw;

    // Only accept a single, bounded strip whose byte range the TIFF map accounts for exactly.
    private static bool HasSingleStrip(byte[] file, TiffBasedFileMap map, TiffBasedDirectory directory, bool big)
    {
        TiffBasedField? offsets = directory.Find(StripOffsets), counts = directory.Find(StripByteCounts);
        long offset = Number(file, offsets, big), length = Number(file, counts, big);
        return offsets?.Count == 1 && counts?.Count == 1 && offset >= 8 && length > 0
            && offset <= file.Length - length
            && map.Ranges.Any(range => range.Kind == TiffBasedRangeKind.ImageData
                && range.Start == offset && range.Length == length);
    }

    private static string Because(string reason) =>
        "The ICC profile of the first directory is not written or removed: " + reason
        + ", and the primary image target is not proven.";

    /// <summary>Whether a directory or a value of the file lies over the bytes.</summary>
    private static bool Addressed(TiffBasedFileMap? map, long start, long length)
    {
        if (map is null) return false;
        foreach (TiffBasedRange range in map.Ranges)
            if (start < range.End && range.Start < start + length) return true;
        return false;
    }

    /// <summary>The one SHORT or LONG of a field that is stored in its entry; -1 for anything else.</summary>
    private static long Number(byte[] file, TiffBasedField? field, bool big)
    {
        if (field is null || field.Count != 1 || !field.IsInline || field.ValueOffset < 0 || field.ValueOffset > file.Length - 4) return -1;
        return field.Type switch
        {
            3 => U16(file, (int)field.ValueOffset, big),
            4 => U32(file, (int)field.ValueOffset, big),
            _ => -1
        };
    }

    private static ushort U16(byte[] file, int at, bool big) => big
        ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at, 2))
        : BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at, 2));

    private static uint U32(byte[] file, int at, bool big) => big
        ? BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4))
        : BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
}
