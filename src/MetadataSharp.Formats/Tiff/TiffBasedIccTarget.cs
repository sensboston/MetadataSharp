using System.Buffers.Binary;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// Whether tag 0x8773 of the first directory of a TIFF-based file is the profile of the image the
/// file is for. In a plain TIFF it is. In a DNG and in the raw formats of the camera makers the
/// first directory is commonly that of a reduced image, and the raw data has its colour described
/// elsewhere: a profile written there would describe the thumbnail. Such a file is recognised by
/// what it holds, never by its name:
/// <list type="bullet">
/// <item>a header number other than 42: ORF and RW2;</item>
/// <item>the letters <c>CR</c> after the header, outside every directory and value: CR2;</item>
/// <item>DNGVersion (0xC612) in the first directory: DNG;</item>
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
    private const ushort NewSubfileType = 0x00FE, Compression = 0x0103, Photometric = 0x0106, DngVersion = 0xC612, IccProfile = 0x8773;
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
                if (tag == DngVersion) return Because("the file is a DNG (DNGVersion in the first directory)");
                if (tag != NewSubfileType || U32(file, at + 4, big) != 1) continue;
                uint flags = type switch { 3 => U16(file, at + 8, big), 4 => U32(file, at + 8, big), _ => 0u };
                reduced |= (flags & 1) != 0;
            }
            if (reduced) return Because("the first directory is that of a reduced image (NewSubfileType)");
        }

        if (map is null) return null;
        foreach (TiffBasedDirectory directory in map.Directories)
        {
            if (directory.Role is not (TiffBasedRole.Primary or TiffBasedRole.Thumbnail or TiffBasedRole.Other)) continue;
            if (Number(file, directory.Find(Photometric), big) is ColourFilterArray or LinearRaw
                || Number(file, directory.Find(Compression), big) is SonyRaw or NikonRaw or PentaxRaw)
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

    private static string Because(string reason) =>
        "The ICC profile of the first directory is not written or removed: " + reason
        + ", and the profile there would be that of a reduced image, not of the image the file is for.";

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
