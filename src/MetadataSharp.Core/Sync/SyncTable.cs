using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Sync;

// The correspondence of EXIF tags and IIM datasets with XMP properties. The pairs are the ones the
// XMP specification (part 2, the tiff: and exif: namespaces), the IPTC Photo Metadata standard (its
// IIM mapping) and the MWG guidelines publish, and that Exiv2 converts. The four EXIF tags whose XMP
// form is a structure of binary measurements (OECF, SpatialFrequencyResponse, CFAPattern,
// DeviceSettingDescription) are left out.
internal static class SyncTable
{
    private const XmpValueKind T = XmpValueKind.Text, Seq = XmpValueKind.Sequence, Bag = XmpValueKind.Bag, Lang = XmpValueKind.LanguageAlternative;
    private const ExifDirectoryKind I = ExifDirectoryKind.Ifd0, E = ExifDirectoryKind.Exif, G = ExifDirectoryKind.Gps;
    private const TiffFieldType Short = TiffFieldType.Short, Long = TiffFieldType.Long, Ascii = TiffFieldType.Ascii,
        R = TiffFieldType.Rational, SR = TiffFieldType.SRational, Undefined = TiffFieldType.Undefined, Byte = TiffFieldType.Byte;

    private static ExifXmpMapping Text(ExifDirectoryKind d, ushort tag, string prefix, string name) => new(d, tag, prefix, name, SyncConversion.Text, T, Ascii);
    private static ExifXmpMapping Int(ExifDirectoryKind d, ushort tag, string prefix, string name, TiffFieldType type = Short, XmpValueKind form = T) =>
        new(d, tag, prefix, name, SyncConversion.Integer, form, type);
    private static ExifXmpMapping Rat(ExifDirectoryKind d, ushort tag, string prefix, string name, TiffFieldType type = R, XmpValueKind form = T) =>
        new(d, tag, prefix, name, SyncConversion.Rational, form, type);
    private static ExifXmpMapping Date(ExifDirectoryKind d, ushort tag, string prefix, string name) => new(d, tag, prefix, name, SyncConversion.Date, T, Ascii);
    private static ExifXmpMapping Coordinate(ushort tag, string name) => new(G, tag, "exif", name, SyncConversion.GpsCoordinate, T, R);
    private static ExifXmpMapping Comment(ExifDirectoryKind d, ushort tag, string name, XmpValueKind form) => new(d, tag, "exif", name, SyncConversion.Comment, form, Undefined);

    internal static readonly ExifXmpMapping[] Exif =
    [
        // Image structure and colour (IFD0).
        Int(I, 0x0100, "tiff", "ImageWidth", Long), Int(I, 0x0101, "tiff", "ImageLength", Long),
        Int(I, 0x0102, "tiff", "BitsPerSample", Short, Seq), Int(I, 0x0103, "tiff", "Compression"),
        Int(I, 0x0106, "tiff", "PhotometricInterpretation"), Int(I, 0x0112, "tiff", "Orientation"),
        Int(I, 0x0115, "tiff", "SamplesPerPixel"), Int(I, 0x011C, "tiff", "PlanarConfiguration"),
        Int(I, 0x0212, "tiff", "YCbCrSubSampling", Short, Seq), Int(I, 0x0213, "tiff", "YCbCrPositioning"),
        Rat(I, 0x011A, "tiff", "XResolution"), Rat(I, 0x011B, "tiff", "YResolution"), Int(I, 0x0128, "tiff", "ResolutionUnit"),
        Int(I, 0x012D, "tiff", "TransferFunction", Short, Seq), Rat(I, 0x013E, "tiff", "WhitePoint", R, Seq),
        Rat(I, 0x013F, "tiff", "PrimaryChromaticities", R, Seq), Rat(I, 0x0211, "tiff", "YCbCrCoefficients", R, Seq),
        Rat(I, 0x0214, "tiff", "ReferenceBlackWhite", R, Seq),
        // Description (IFD0).
        Date(I, 0x0132, "xmp", "ModifyDate"),
        new(I, 0x010E, "dc", "description", SyncConversion.Text, Lang, Ascii),
        Text(I, 0x010F, "tiff", "Make"), Text(I, 0x0110, "tiff", "Model"), Text(I, 0x0131, "tiff", "Software"),
        new(I, 0x013B, "dc", "creator", SyncConversion.TextList, Seq, Ascii),
        Int(I, 0x4746, "xmp", "Rating"),
        new(I, 0x8298, "dc", "rights", SyncConversion.Text, Lang, Ascii),
        // The Exif directory.
        new(E, 0x9000, "exif", "ExifVersion", SyncConversion.Version, T, Undefined),
        new(E, 0xA000, "exif", "FlashpixVersion", SyncConversion.Version, T, Undefined),
        Int(E, 0xA001, "exif", "ColorSpace"), Int(E, 0x9101, "exif", "ComponentsConfiguration", Undefined, Seq),
        Rat(E, 0x9102, "exif", "CompressedBitsPerPixel"),
        Int(E, 0xA002, "exif", "PixelXDimension", Long), Int(E, 0xA003, "exif", "PixelYDimension", Long),
        Comment(E, 0x9286, "UserComment", Lang), Text(E, 0xA004, "exif", "RelatedSoundFile"),
        Date(E, 0x9003, "photoshop", "DateCreated"), Date(E, 0x9004, "xmp", "CreateDate"),
        Rat(E, 0x829A, "exif", "ExposureTime"), Rat(E, 0x829D, "exif", "FNumber"), Int(E, 0x8822, "exif", "ExposureProgram"),
        Text(E, 0x8824, "exif", "SpectralSensitivity"), Int(E, 0x8827, "exif", "ISOSpeedRatings", Short, Seq),
        Rat(E, 0x9201, "exif", "ShutterSpeedValue", SR), Rat(E, 0x9202, "exif", "ApertureValue"),
        Rat(E, 0x9203, "exif", "BrightnessValue", SR), Rat(E, 0x9204, "exif", "ExposureBiasValue", SR),
        Rat(E, 0x9205, "exif", "MaxApertureValue"), Rat(E, 0x9206, "exif", "SubjectDistance"),
        Int(E, 0x9207, "exif", "MeteringMode"), Int(E, 0x9208, "exif", "LightSource"),
        new(E, 0x9209, "exif", "Flash", SyncConversion.Flash, T, Short),
        Rat(E, 0x920A, "exif", "FocalLength"), Int(E, 0x9214, "exif", "SubjectArea", Short, Seq),
        Rat(E, 0xA20B, "exif", "FlashEnergy"), Rat(E, 0xA20E, "exif", "FocalPlaneXResolution"),
        Rat(E, 0xA20F, "exif", "FocalPlaneYResolution"), Int(E, 0xA210, "exif", "FocalPlaneResolutionUnit"),
        Int(E, 0xA214, "exif", "SubjectLocation", Short, Seq), Rat(E, 0xA215, "exif", "ExposureIndex"),
        Int(E, 0xA217, "exif", "SensingMethod"), Int(E, 0xA300, "exif", "FileSource", Undefined),
        Int(E, 0xA301, "exif", "SceneType", Undefined), Int(E, 0xA401, "exif", "CustomRendered"),
        Int(E, 0xA402, "exif", "ExposureMode"), Int(E, 0xA403, "exif", "WhiteBalance"),
        Rat(E, 0xA404, "exif", "DigitalZoomRatio"), Int(E, 0xA405, "exif", "FocalLengthIn35mmFilm"),
        Int(E, 0xA406, "exif", "SceneCaptureType"), Int(E, 0xA407, "exif", "GainControl"),
        Int(E, 0xA408, "exif", "Contrast"), Int(E, 0xA409, "exif", "Saturation"), Int(E, 0xA40A, "exif", "Sharpness"),
        Int(E, 0xA40C, "exif", "SubjectDistanceRange"), Text(E, 0xA420, "exif", "ImageUniqueID"),
        // The GPS directory.
        new(G, 0x0000, "exif", "GPSVersionID", SyncConversion.GpsVersion, T, Byte),
        Coordinate(0x0002, "GPSLatitude"), Coordinate(0x0004, "GPSLongitude"),
        Int(G, 0x0005, "exif", "GPSAltitudeRef", Byte), Rat(G, 0x0006, "exif", "GPSAltitude"),
        new(G, 0x0007, "exif", "GPSTimeStamp", SyncConversion.GpsTime, T, R),
        Text(G, 0x0008, "exif", "GPSSatellites"), Text(G, 0x0009, "exif", "GPSStatus"), Text(G, 0x000A, "exif", "GPSMeasureMode"),
        Rat(G, 0x000B, "exif", "GPSDOP"), Text(G, 0x000C, "exif", "GPSSpeedRef"), Rat(G, 0x000D, "exif", "GPSSpeed"),
        Text(G, 0x000E, "exif", "GPSTrackRef"), Rat(G, 0x000F, "exif", "GPSTrack"),
        Text(G, 0x0010, "exif", "GPSImgDirectionRef"), Rat(G, 0x0011, "exif", "GPSImgDirection"),
        Text(G, 0x0012, "exif", "GPSMapDatum"),
        Coordinate(0x0014, "GPSDestLatitude"), Coordinate(0x0016, "GPSDestLongitude"),
        Text(G, 0x0017, "exif", "GPSDestBearingRef"), Rat(G, 0x0018, "exif", "GPSDestBearing"),
        Text(G, 0x0019, "exif", "GPSDestDistanceRef"), Rat(G, 0x001A, "exif", "GPSDestDistance"),
        Comment(G, 0x001B, "GPSProcessingMethod", T), Comment(G, 0x001C, "GPSAreaInformation", T),
        Int(G, 0x001E, "exif", "GPSDifferential"),
    ];

    internal static readonly IptcXmpMapping[] Iptc =
    [
        new(5, "dc", "title", Lang), new(10, "photoshop", "Urgency", T), new(15, "photoshop", "Category", T),
        new(20, "photoshop", "SupplementalCategories", Bag), new(25, "dc", "subject", Bag),
        new(40, "photoshop", "Instructions", T),
        new(55, "photoshop", "DateCreated", T, 60), new(62, "xmp", "CreateDate", T, 63),
        new(80, "dc", "creator", Seq), new(85, "photoshop", "AuthorsPosition", T),
        new(90, "photoshop", "City", T), new(92, "iptc", "Location", T), new(95, "photoshop", "State", T),
        new(100, "iptc", "CountryCode", T), new(101, "photoshop", "Country", T),
        new(103, "photoshop", "TransmissionReference", T), new(105, "photoshop", "Headline", T),
        new(110, "photoshop", "Credit", T), new(115, "photoshop", "Source", T),
        new(116, "dc", "rights", Lang), new(120, "dc", "description", Lang), new(122, "photoshop", "CaptionWriter", T),
    ];

    /// <summary>The EXIF tags the MWG guidelines reconcile with IPTC and XMP; every other EXIF tag is EXIF's alone.</summary>
    internal static bool IsShared(ExifXmpMapping mapping) =>
        mapping.Tag.TagId is 0x010E or 0x013B or 0x8298 or 0x0132 or 0x9003 or 0x9004 && mapping.Tag.Directory != G;
}
