using System.Buffers.Binary;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>
/// Decodes the Canon MakerNote layout: an IFD with no header, in the byte order of the enclosing
/// TIFF, whose external value offsets count from the TIFF header. A trailing 8-byte footer
/// (a TIFF byte-order mark followed by the MakerNote's original offset) lets a MakerNote that
/// was moved by other software be read with its offsets corrected.
/// </summary>
/// <remarks>
/// Decoding is best effort and never fails the surrounding EXIF read: any structural problem
/// returns null, and the MakerNote stays available as its raw EXIF value.
/// </remarks>
internal static class CanonMakerNoteReader
{
    internal static MakerNote? TryRead(ReadOnlySpan<byte> tiff, int start, int length, Endianness order,
        MetadataLimits limits, long retainedBudget)
    {
        if (start < 0 || length < 2 || start > tiff.Length - length)
            return null;
        long delta = FooterDelta(tiff, start, length, order == Endianness.LittleEndian);
        return MakerNoteReader.Directory(MakerNoteFormat.Canon, tiff, start, start + length, 0, order, delta,
            limits, retainedBudget, Name, CanonMakerNoteArrays.Decompose);
    }

    /// <summary>The offset correction implied by the footer, or 0 when there is none.</summary>
    private static long FooterDelta(ReadOnlySpan<byte> tiff, int start, int length, bool little)
    {
        if (length < 8)
            return 0;
        ReadOnlySpan<byte> footer = tiff.Slice(start + length - 8, 8);
        bool marked = little
            ? footer[0] == 0x49 && footer[1] == 0x49 && footer[2] == 0x2A && footer[3] == 0x00
            : footer[0] == 0x4D && footer[1] == 0x4D && footer[2] == 0x00 && footer[3] == 0x2A;
        if (!marked)
            return 0;
        uint original = little
            ? BinaryPrimitives.ReadUInt32LittleEndian(footer[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(footer[4..]);
        return start - (long)original;
    }

    internal static string? Name(ushort tag) => MakerNoteReader.Lookup(Names, tag);

    /// <summary>Canon MakerNote tag names, ascending by ID.</summary>
    internal static readonly (ushort Id, string Name)[] Names =
    [
        (0x0001, "CameraSettings"), (0x0002, "FocalLength"), (0x0003, "CanonFlashInfo"),
        (0x0004, "ShotInfo"), (0x0005, "Panorama"), (0x0006, "ImageType"),
        (0x0007, "FirmwareVersion"), (0x0008, "FileNumber"), (0x0009, "OwnerName"),
        (0x000C, "SerialNumber"), (0x000D, "CameraInfo"), (0x000E, "FileLength"),
        (0x000F, "CustomFunctions2"), (0x0010, "ModelID"), (0x0011, "MovieInfo"),
        (0x0012, "PictureInfo"), (0x0013, "ThumbnailImageValidArea"), (0x0015, "SerialNumberFormat"),
        (0x001A, "SuperMacro"), (0x001C, "DateStampMode"), (0x001D, "MyColors"),
        (0x001E, "FirmwareRevision"), (0x0023, "Categories"), (0x0024, "FaceDetect1"),
        (0x0025, "FaceDetect2"), (0x0026, "AFInfo"), (0x0027, "ContrastInfo"),
        (0x0028, "ImageUniqueID"), (0x0029, "WBInfo"), (0x002F, "FaceDetect3"),
        (0x0035, "TimeInfo"), (0x0038, "BatteryType"), (0x003C, "AFInfo3"),
        (0x0081, "RawDataOffset"), (0x0083, "OriginalDecisionDataOffset"), (0x0090, "CustomFunctions1D"),
        (0x0091, "PersonalFunctions"), (0x0092, "PersonalFunctionValues"), (0x0093, "CanonFileInfo"),
        (0x0094, "AFPointsInFocus1D"), (0x0095, "LensModel"), (0x0096, "InternalSerialNumber"),
        (0x0097, "DustRemovalData"), (0x0099, "CustomFunctions"), (0x009A, "AspectInfo"),
        (0x00A0, "ProcessingInfo"), (0x00A1, "ToneCurveTable"), (0x00A2, "SharpnessTable"),
        (0x00A3, "SharpnessFreqTable"), (0x00A4, "WhiteBalanceTable"), (0x00A9, "ColorBalance"),
        (0x00AA, "MeasuredColor"), (0x00AE, "ColorTemperature"), (0x00B0, "CanonFlags"),
        (0x00B1, "ModifiedInfo"), (0x00B2, "ToneCurveMatching"), (0x00B3, "WhiteBalanceMatching"),
        (0x00B4, "ColorSpace"), (0x00B6, "PreviewImageInfo"), (0x00D0, "VRDOffset"),
        (0x00E0, "SensorInfo"), (0x4001, "ColorData"), (0x4002, "CRWParam"),
        (0x4003, "ColorInfo"), (0x4005, "Flavor"), (0x4008, "PictureStyleUserDef"),
        (0x4009, "PictureStylePC"), (0x4010, "CustomPictureStyleFileName"), (0x4013, "AFMicroAdj"),
        (0x4015, "VignettingCorr"), (0x4016, "VignettingCorr2"), (0x4018, "LightingOpt"),
        (0x4019, "LensInfo"), (0x4020, "AmbienceInfo"), (0x4021, "MultiExp"),
        (0x4024, "FilterInfo"), (0x4025, "HDRInfo"), (0x4028, "AFConfig"),
        (0x403F, "RawBurstModeRoll"),
    ];
}
