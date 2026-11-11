namespace MetadataSharp.Core;

// Names from publications other than Exif and TIFF. Each row is a tag number and the word its publication
// uses for the field, nothing else: no types, no counts, no value meanings and no descriptive text are
// taken, so a row can be checked against its source by reading two columns of one table.
public static partial class ExifTags
{
    private static IEnumerable<ExifTagDefinition> Ifd0FromOtherSources() =>
    [
        // ISO 12234-2 (TIFF/EP): the fields it defines for the main directory of a raw file that the Exif
        // 0th IFD table does not list. Exif keeps the camera fields among them in its own directory, where
        // the reviewed table names them; a file that follows TIFF/EP stores them here. The two names with
        // a slash in the standard, IPTC/NAA and TIFF/EPStandardID, are written without it.
        .. Named(ExifTagSource.TiffEp,
            (0x014A, "SubIFDs"), (0x015B, "JPEGTables"), (0x828D, "CFARepeatPatternDim"),
            (0x828E, "CFAPattern"), (0x828F, "BatteryLevel"), (0x829A, "ExposureTime"), (0x829D, "FNumber"),
            (0x83BB, "IPTCNAA"), (0x8773, "InterColorProfile"), (0x8822, "ExposureProgram"),
            (0x8824, "SpectralSensitivity"), (0x8827, "ISOSpeedRatings"), (0x8828, "OECF"),
            (0x8829, "Interlace"), (0x882A, "TimeZoneOffset"), (0x882B, "SelfTimerMode"),
            (0x9003, "DateTimeOriginal"), (0x9102, "CompressedBitsPerPixel"), (0x9201, "ShutterSpeedValue"),
            (0x9202, "ApertureValue"), (0x9203, "BrightnessValue"), (0x9204, "ExposureBiasValue"),
            (0x9205, "MaxApertureValue"), (0x9206, "SubjectDistance"), (0x9207, "MeteringMode"),
            (0x9208, "LightSource"), (0x9209, "Flash"), (0x920A, "FocalLength"), (0x920B, "FlashEnergy"),
            (0x920C, "SpatialFrequencyResponse"), (0x920D, "Noise"), (0x920E, "FocalPlaneXResolution"),
            (0x920F, "FocalPlaneYResolution"), (0x9210, "FocalPlaneResolutionUnit"), (0x9211, "ImageNumber"),
            (0x9212, "SecurityClassification"), (0x9213, "ImageHistory"), (0x9214, "SubjectLocation"),
            (0x9215, "ExposureIndex"), (0x9216, "TIFFEPStandardID"), (0x9217, "SensingMethod")),

        // Microsoft: the rating pair and the five UTF-16 text fields of the Windows property system.
        // Seiko Epson: the PRINT Image Matching block.
        .. Named(ExifTagSource.Vendor,
            (0x4746, "Rating"), (0x4749, "RatingPercent"), (0x9C9B, "XPTitle"), (0x9C9C, "XPComment"),
            (0x9C9D, "XPAuthor"), (0x9C9E, "XPKeywords"), (0x9C9F, "XPSubject"),
            (0xC4A5, "PrintImageMatching")),

        // Adobe Digital Negative 1.7, tag numbers 0xC612 and up. A DNG stores some of these in the directory
        // of the raw image, not in the first one; they are named wherever a 0th-IFD table applies.
        .. Named(ExifTagSource.Dng,
            (0xC612, "DNGVersion"), (0xC613, "DNGBackwardVersion"), (0xC614, "UniqueCameraModel"),
            (0xC615, "LocalizedCameraModel"), (0xC616, "CFAPlaneColor"), (0xC617, "CFALayout"),
            (0xC618, "LinearizationTable"), (0xC619, "BlackLevelRepeatDim"), (0xC61A, "BlackLevel"),
            (0xC61B, "BlackLevelDeltaH"), (0xC61C, "BlackLevelDeltaV"), (0xC61D, "WhiteLevel"),
            (0xC61E, "DefaultScale"), (0xC61F, "DefaultCropOrigin"), (0xC620, "DefaultCropSize"),
            (0xC621, "ColorMatrix1"), (0xC622, "ColorMatrix2"), (0xC623, "CameraCalibration1"),
            (0xC624, "CameraCalibration2"), (0xC625, "ReductionMatrix1"), (0xC626, "ReductionMatrix2"),
            (0xC627, "AnalogBalance"), (0xC628, "AsShotNeutral"), (0xC629, "AsShotWhiteXY"),
            (0xC62A, "BaselineExposure"), (0xC62B, "BaselineNoise"), (0xC62C, "BaselineSharpness"),
            (0xC62D, "BayerGreenSplit"), (0xC62E, "LinearResponseLimit"), (0xC62F, "CameraSerialNumber"),
            (0xC630, "LensInfo"), (0xC631, "ChromaBlurRadius"), (0xC632, "AntiAliasStrength"),
            (0xC633, "ShadowScale"), (0xC634, "DNGPrivateData"), (0xC635, "MakerNoteSafety"),
            (0xC65A, "CalibrationIlluminant1"), (0xC65B, "CalibrationIlluminant2"),
            (0xC65C, "BestQualityScale"), (0xC65D, "RawDataUniqueID"), (0xC68B, "OriginalRawFileName"),
            (0xC68C, "OriginalRawFileData"), (0xC68D, "ActiveArea"), (0xC68E, "MaskedAreas"),
            (0xC68F, "AsShotICCProfile"), (0xC690, "AsShotPreProfileMatrix"), (0xC691, "CurrentICCProfile"),
            (0xC692, "CurrentPreProfileMatrix"), (0xC6BF, "ColorimetricReference"),
            (0xC6F3, "CameraCalibrationSignature"), (0xC6F4, "ProfileCalibrationSignature"),
            (0xC6F5, "ExtraCameraProfiles"), (0xC6F6, "AsShotProfileName"), (0xC6F7, "NoiseReductionApplied"),
            (0xC6F8, "ProfileName"), (0xC6F9, "ProfileHueSatMapDims"), (0xC6FA, "ProfileHueSatMapData1"),
            (0xC6FB, "ProfileHueSatMapData2"), (0xC6FC, "ProfileToneCurve"), (0xC6FD, "ProfileEmbedPolicy"),
            (0xC6FE, "ProfileCopyright"), (0xC714, "ForwardMatrix1"), (0xC715, "ForwardMatrix2"),
            (0xC716, "PreviewApplicationName"), (0xC717, "PreviewApplicationVersion"),
            (0xC718, "PreviewSettingsName"), (0xC719, "PreviewSettingsDigest"), (0xC71A, "PreviewColorSpace"),
            (0xC71B, "PreviewDateTime"), (0xC71C, "RawImageDigest"), (0xC71D, "OriginalRawFileDigest"),
            (0xC71E, "SubTileBlockSize"), (0xC71F, "RowInterleaveFactor"), (0xC725, "ProfileLookTableDims"),
            (0xC726, "ProfileLookTableData"), (0xC740, "OpcodeList1"), (0xC741, "OpcodeList2"),
            (0xC74E, "OpcodeList3"), (0xC761, "NoiseProfile"), (0xC791, "OriginalDefaultFinalSize"),
            (0xC792, "OriginalBestQualityFinalSize"), (0xC793, "OriginalDefaultCropSize"),
            (0xC7A3, "ProfileHueSatMapEncoding"), (0xC7A4, "ProfileLookTableEncoding"),
            (0xC7A5, "BaselineExposureOffset"), (0xC7A6, "DefaultBlackRender"), (0xC7A7, "NewRawImageDigest"),
            (0xC7A8, "RawToPreviewGain"), (0xC7B5, "DefaultUserCrop"), (0xC7E9, "DepthFormat"),
            (0xC7EA, "DepthNear"), (0xC7EB, "DepthFar"), (0xC7EC, "DepthUnits"), (0xC7ED, "DepthMeasureType"),
            (0xC7EE, "EnhanceParams"), (0xCD2D, "ProfileGainTableMap"), (0xCD2E, "SemanticName"),
            (0xCD30, "SemanticInstanceID"), (0xCD31, "CalibrationIlluminant3"), (0xCD32, "CameraCalibration3"),
            (0xCD33, "ColorMatrix3"), (0xCD34, "ForwardMatrix3"), (0xCD35, "IlluminantData1"),
            (0xCD36, "IlluminantData2"), (0xCD37, "IlluminantData3"), (0xCD38, "MaskSubArea"),
            (0xCD39, "ProfileHueSatMapData3"), (0xCD3A, "ReductionMatrix3"), (0xCD3C, "BigTableDigests"),
            (0xCD3D, "BigTableOffsets"), (0xCD3E, "BigTableByteCounts"), (0xCD3F, "RGBTables"),
            (0xCD40, "ProfileGainTableMap2"), (0xCD43, "ColumnInterleaveFactor"),
            (0xCD44, "ImageSequenceInfo"), (0xCD46, "ImageStats"), (0xCD47, "ProfileDynamicRange"),
            (0xCD48, "ProfileGroupName"), (0xCD49, "JXLDistance"), (0xCD4A, "JXLEffort"),
            (0xCD4B, "JXLDecodeSpeed")),
    ];

    private static IEnumerable<ExifTagDefinition> InteropFromOtherSources() =>
        // DCF: the version of the interoperability rules, and the three fields that describe the main image
        // a thumbnail file belongs to.
        Named(ExifTagSource.Dcf,
            (0x0002, "InteroperabilityVersion"), (0x1000, "RelatedImageFileFormat"),
            (0x1001, "RelatedImageWidth"), (0x1002, "RelatedImageLength"));

    private static IEnumerable<ExifTagDefinition> Named(ExifTagSource source, params (ushort Id, string Name)[] rows)
    {
        foreach ((ushort id, string name) in rows)
            yield return new() { Id = id, Name = name, Source = source };
    }

    /// <summary>
    /// One directory's rows in ascending tag order. A number the reviewed table already names keeps that
    /// row: Exif is the fuller statement, and the same number never gets two names in one directory.
    /// </summary>
    private static ExifTagDefinition[] Join(ExifTagDefinition[] reviewed, IEnumerable<ExifTagDefinition> others)
    {
        var byId = new SortedDictionary<ushort, ExifTagDefinition>();
        foreach (ExifTagDefinition definition in others) byId[definition.Id] = definition;
        foreach (ExifTagDefinition definition in reviewed) byId[definition.Id] = definition;
        return [.. byId.Values];
    }
}
