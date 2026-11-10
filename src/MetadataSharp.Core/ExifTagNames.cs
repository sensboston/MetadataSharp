using System.Diagnostics.CodeAnalysis;

namespace MetadataSharp.Core;

/// <summary>The tag identifiers of the supported EXIF directories, in the names the standards give them.</summary>
/// <remarks>
/// Each pair below is a tag number and the field name recorded for it in the final publications this
/// project reviewed for its read policy (see
/// docs/design/TIFF_EXIF_NORMATIVE_SOURCES_AND_READ_POLICY.md): the Exif 3.0 attribute tables for the
/// 0th, Exif, GPS and Interoperability directories, plus the TIFF 6.0 tag registry for the fields a 0th
/// directory may carry. The two agree on every shared identifier except 0x0202, where the 1992 text's
/// sorted appendix misprints the name as "JPEGInterchangeFormatLngth"; where that publication
/// describes the field, and where Exif lists it, it is spelled out, and that spelling is used here.
/// Names that only a vendor extension defines - a MakerNote's own subtags, PrintImageMatching, Rating,
/// IPTCNAA, the DNG tags - are deliberately absent: an unnamed tag keeps its numeric ID, which is a
/// stated gap rather than a guess at somebody else's dictionary. Where the reviewed text writes a Field
/// Name as a phrase, it is stored here as an identifier: ExifIFDPointer, GPSInfoIFDPointer,
/// InteroperabilityIFDPointer.
/// </remarks>
public static class ExifTagNames
{
    // 0th IFD and thumbnail: the Exif 3.0 table of TIFF fields, extended by the TIFF 6.0 registry.
    private static readonly (ushort Id, string Name)[] Image =
        [
            (0x00FE, "NewSubfileType"),
            (0x00FF, "SubfileType"),
            (0x0100, "ImageWidth"),
            (0x0101, "ImageLength"),
            (0x0102, "BitsPerSample"),
            (0x0103, "Compression"),
            (0x0106, "PhotometricInterpretation"),
            (0x0107, "Threshholding"),
            (0x0108, "CellWidth"),
            (0x0109, "CellLength"),
            (0x010A, "FillOrder"),
            (0x010D, "DocumentName"),
            (0x010E, "ImageDescription"),
            (0x010F, "Make"),
            (0x0110, "Model"),
            (0x0111, "StripOffsets"),
            (0x0112, "Orientation"),
            (0x0115, "SamplesPerPixel"),
            (0x0116, "RowsPerStrip"),
            (0x0117, "StripByteCounts"),
            (0x0118, "MinSampleValue"),
            (0x0119, "MaxSampleValue"),
            (0x011A, "XResolution"),
            (0x011B, "YResolution"),
            (0x011C, "PlanarConfiguration"),
            (0x011D, "PageName"),
            (0x011E, "XPosition"),
            (0x011F, "YPosition"),
            (0x0120, "FreeOffsets"),
            (0x0121, "FreeByteCounts"),
            (0x0122, "GrayResponseUnit"),
            (0x0123, "GrayResponseCurve"),
            (0x0124, "T4Options"),
            (0x0125, "T6Options"),
            (0x0128, "ResolutionUnit"),
            (0x0129, "PageNumber"),
            (0x012D, "TransferFunction"),
            (0x0131, "Software"),
            (0x0132, "DateTime"),
            (0x013B, "Artist"),
            (0x013C, "HostComputer"),
            (0x013D, "Predictor"),
            (0x013E, "WhitePoint"),
            (0x013F, "PrimaryChromaticities"),
            (0x0140, "ColorMap"),
            (0x0141, "HalftoneHints"),
            (0x0142, "TileWidth"),
            (0x0143, "TileLength"),
            (0x0144, "TileOffsets"),
            (0x0145, "TileByteCounts"),
            (0x014C, "InkSet"),
            (0x014D, "InkNames"),
            (0x014E, "NumberOfInks"),
            (0x0150, "DotRange"),
            (0x0151, "TargetPrinter"),
            (0x0152, "ExtraSamples"),
            (0x0153, "SampleFormat"),
            (0x0154, "SMinSampleValue"),
            (0x0155, "SMaxSampleValue"),
            (0x0156, "TransferRange"),
            (0x0200, "JPEGProc"),
            (0x0201, "JPEGInterchangeFormat"),
            (0x0202, "JPEGInterchangeFormatLength"),
            (0x0203, "JPEGRestartInterval"),
            (0x0205, "JPEGLosslessPredictors"),
            (0x0206, "JPEGPointTransforms"),
            (0x0207, "JPEGQTables"),
            (0x0208, "JPEGDCTables"),
            (0x0209, "JPEGACTables"),
            (0x0211, "YCbCrCoefficients"),
            (0x0212, "YCbCrSubSampling"),
            (0x0213, "YCbCrPositioning"),
            (0x0214, "ReferenceBlackWhite"),
            (0x8298, "Copyright"),
            (0x8769, "ExifIFDPointer"),
            (0x8825, "GPSInfoIFDPointer"),
        ];
    // Exif sub-IFD, exactly as the Exif 3.0 attribute tables list it.
    private static readonly (ushort Id, string Name)[] Photo =
        [
            (0x829A, "ExposureTime"),
            (0x829D, "FNumber"),
            (0x8822, "ExposureProgram"),
            (0x8824, "SpectralSensitivity"),
            (0x8827, "PhotographicSensitivity"),
            (0x8828, "OECF"),
            (0x8830, "SensitivityType"),
            (0x8831, "StandardOutputSensitivity"),
            (0x8832, "RecommendedExposureIndex"),
            (0x8833, "ISOSpeed"),
            (0x8834, "ISOSpeedLatitudeyyy"),
            (0x8835, "ISOSpeedLatitudezzz"),
            (0x9000, "ExifVersion"),
            (0x9003, "DateTimeOriginal"),
            (0x9004, "DateTimeDigitized"),
            (0x9010, "OffsetTime"),
            (0x9011, "OffsetTimeOriginal"),
            (0x9012, "OffsetTimeDigitized"),
            (0x9101, "ComponentsConfiguration"),
            (0x9102, "CompressedBitsPerPixel"),
            (0x9201, "ShutterSpeedValue"),
            (0x9202, "ApertureValue"),
            (0x9203, "BrightnessValue"),
            (0x9204, "ExposureBiasValue"),
            (0x9205, "MaxApertureValue"),
            (0x9206, "SubjectDistance"),
            (0x9207, "MeteringMode"),
            (0x9208, "LightSource"),
            (0x9209, "Flash"),
            (0x920A, "FocalLength"),
            (0x9214, "SubjectArea"),
            (0x927C, "MakerNote"),
            (0x9286, "UserComment"),
            (0x9290, "SubSecTime"),
            (0x9291, "SubSecTimeOriginal"),
            (0x9292, "SubSecTimeDigitized"),
            (0x9400, "Temperature"),
            (0x9401, "Humidity"),
            (0x9402, "Pressure"),
            (0x9403, "WaterDepth"),
            (0x9404, "Acceleration"),
            (0x9405, "CameraElevationAngle"),
            (0xA000, "FlashpixVersion"),
            (0xA001, "ColorSpace"),
            (0xA002, "PixelXDimension"),
            (0xA003, "PixelYDimension"),
            (0xA004, "RelatedSoundFile"),
            (0xA005, "InteroperabilityIFDPointer"),
            (0xA20B, "FlashEnergy"),
            (0xA20C, "SpatialFrequencyResponse"),
            (0xA20E, "FocalPlaneXResolution"),
            (0xA20F, "FocalPlaneYResolution"),
            (0xA210, "FocalPlaneResolutionUnit"),
            (0xA214, "SubjectLocation"),
            (0xA215, "ExposureIndex"),
            (0xA217, "SensingMethod"),
            (0xA300, "FileSource"),
            (0xA301, "SceneType"),
            (0xA302, "CFAPattern"),
            (0xA401, "CustomRendered"),
            (0xA402, "ExposureMode"),
            (0xA403, "WhiteBalance"),
            (0xA404, "DigitalZoomRatio"),
            (0xA405, "FocalLengthIn35mmFilm"),
            (0xA406, "SceneCaptureType"),
            (0xA407, "GainControl"),
            (0xA408, "Contrast"),
            (0xA409, "Saturation"),
            (0xA40A, "Sharpness"),
            (0xA40B, "DeviceSettingDescription"),
            (0xA40C, "SubjectDistanceRange"),
            (0xA420, "ImageUniqueID"),
            (0xA430, "CameraOwnerName"),
            (0xA431, "BodySerialNumber"),
            (0xA432, "LensSpecification"),
            (0xA433, "LensMake"),
            (0xA434, "LensModel"),
            (0xA435, "LensSerialNumber"),
            (0xA436, "ImageTitle"),
            (0xA437, "Photographer"),
            (0xA438, "ImageEditor"),
            (0xA439, "CameraFirmware"),
            (0xA43A, "RAWDevelopingSoftware"),
            (0xA43B, "ImageEditingSoftware"),
            (0xA43C, "MetadataEditingSoftware"),
            (0xA460, "CompositeImage"),
            (0xA461, "SourceImageNumberOfCompositeImage"),
            (0xA462, "SourceExposureTimesOfCompositeImage"),
            (0xA500, "Gamma"),
        ];
    // GPS Info sub-IFD, exactly as the Exif 3.0 GPS attribute table lists it.
    private static readonly (ushort Id, string Name)[] Gps =
        [
            (0x0000, "GPSVersionID"),
            (0x0001, "GPSLatitudeRef"),
            (0x0002, "GPSLatitude"),
            (0x0003, "GPSLongitudeRef"),
            (0x0004, "GPSLongitude"),
            (0x0005, "GPSAltitudeRef"),
            (0x0006, "GPSAltitude"),
            (0x0007, "GPSTimeStamp"),
            (0x0008, "GPSSatellites"),
            (0x0009, "GPSStatus"),
            (0x000A, "GPSMeasureMode"),
            (0x000B, "GPSDOP"),
            (0x000C, "GPSSpeedRef"),
            (0x000D, "GPSSpeed"),
            (0x000E, "GPSTrackRef"),
            (0x000F, "GPSTrack"),
            (0x0010, "GPSImgDirectionRef"),
            (0x0011, "GPSImgDirection"),
            (0x0012, "GPSMapDatum"),
            (0x0013, "GPSDestLatitudeRef"),
            (0x0014, "GPSDestLatitude"),
            (0x0015, "GPSDestLongitudeRef"),
            (0x0016, "GPSDestLongitude"),
            (0x0017, "GPSDestBearingRef"),
            (0x0018, "GPSDestBearing"),
            (0x0019, "GPSDestDistanceRef"),
            (0x001A, "GPSDestDistance"),
            (0x001B, "GPSProcessingMethod"),
            (0x001C, "GPSAreaInformation"),
            (0x001D, "GPSDateStamp"),
            (0x001E, "GPSDifferential"),
            (0x001F, "GPSHPositioningError"),
        ];
    // Interoperability sub-IFD: the reviewed table records one field.
    private static readonly (ushort Id, string Name)[] Iop =
        [
            (0x0001, "InteroperabilityIndex"),
        ];

    private static readonly IReadOnlyDictionary<ExifDirectoryKind, (ushort Id, string Name)[]> Tables =
        new Dictionary<ExifDirectoryKind, (ushort Id, string Name)[]>
        {
            [ExifDirectoryKind.Ifd0] = Image,
            [ExifDirectoryKind.Exif] = Photo,
            [ExifDirectoryKind.Gps] = Gps,
            [ExifDirectoryKind.Interop] = Iop,
            [ExifDirectoryKind.Thumbnail] = Image,
        };
    private static readonly IReadOnlyDictionary<ExifDirectoryKind, IReadOnlyList<ExifTagKey>> Named =
        new Dictionary<ExifDirectoryKind, IReadOnlyList<ExifTagKey>>
        {
            [ExifDirectoryKind.Ifd0] = Build(ExifDirectoryKind.Ifd0, Image),
            [ExifDirectoryKind.Exif] = Build(ExifDirectoryKind.Exif, Photo),
            [ExifDirectoryKind.Gps] = Build(ExifDirectoryKind.Gps, Gps),
            [ExifDirectoryKind.Interop] = Build(ExifDirectoryKind.Interop, Iop),
            [ExifDirectoryKind.Thumbnail] = Build(ExifDirectoryKind.Thumbnail, Image),
        };

    /// <summary>The tags this release can name in one directory, in ascending tag ID order.</summary>
    public static IReadOnlyList<ExifTagKey> NamedTags(ExifDirectoryKind directory)
    {
        if (!Enum.IsDefined(directory)) throw new ArgumentOutOfRangeException(nameof(directory));
        return Named[directory];
    }

    /// <summary>The standard's field name for a tag, or false when this release does not name that tag.</summary>
    public static bool TryGetName(ExifTagKey key, [NotNullWhen(true)] out string? name)
    {
        if (Tables.TryGetValue(key.Directory, out var table))
        {
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].Id == key.TagId)
                {
                    name = table[i].Name;
                    return true;
                }
            }
        }
        name = null;
        return false;
    }

    /// <summary>The tag a field name refers to in one directory. Comparison is ordinal and case sensitive.</summary>
    public static bool TryGetKey(ExifDirectoryKind directory, string name, out ExifTagKey key)
    {
        ArgumentNullException.ThrowIfNull(name);
        key = default;
        if (!Enum.IsDefined(directory) || !Tables.TryGetValue(directory, out var table)) return false;
        for (int i = 0; i < table.Length; i++)
        {
            if (string.Equals(table[i].Name, name, StringComparison.Ordinal))
            {
                key = new ExifTagKey(directory, table[i].Id);
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<ExifTagKey> Build(ExifDirectoryKind directory, (ushort Id, string Name)[] table)
    {
        var keys = new ExifTagKey[table.Length];
        for (int i = 0; i < table.Length; i++) keys[i] = new ExifTagKey(directory, table[i].Id);
        return keys;
    }
}
