namespace MetadataSharp.Core.MakerNotes;

/// <summary>Nikon MakerNote tag names per layout, ascending by ID.</summary>
internal static class NikonMakerNoteNames
{
    internal static string? Nikon1(ushort tag) => MakerNoteReader.Lookup(Nikon1Names, tag);

    // Map Nikon1 tag IDs to names in ascending order for binary lookup.
    internal static readonly (ushort Id, string Name)[] Nikon1Names =
    [
        (0x0001, "Version"), (0x0002, "ISOSpeed"), (0x0003, "ColorMode"),
        (0x0004, "Quality"), (0x0005, "WhiteBalance"), (0x0006, "Sharpening"),
        (0x0007, "Focus"), (0x0008, "FlashSetting"), (0x000F, "ISOSelection"),
        (0x0010, "DataDump"), (0x0080, "ImageAdjustment"), (0x0082, "AuxiliaryLens"),
        (0x0085, "FocusDistance"), (0x0086, "DigitalZoom"), (0x0088, "AFFocusPos"),
    ];

    internal static string? Nikon2(ushort tag) => MakerNoteReader.Lookup(Nikon2Names, tag);

    // Map Nikon2 tag IDs to names in ascending order for binary lookup.
    internal static readonly (ushort Id, string Name)[] Nikon2Names =
    [
        (0x0003, "Quality"), (0x0004, "ColorMode"), (0x0005, "ImageAdjustment"),
        (0x0006, "ISOSpeed"), (0x0007, "WhiteBalance"), (0x0008, "Focus"),
        (0x000A, "DigitalZoom"), (0x000B, "AuxiliaryLens"),
    ];

    internal static string? Nikon3(ushort tag) => MakerNoteReader.Lookup(Nikon3Names, tag);

    // Map Nikon3 tag IDs to names in ascending order for binary lookup.
    internal static readonly (ushort Id, string Name)[] Nikon3Names =
    [
        (0x0001, "Version"), (0x0002, "ISOSpeed"), (0x0003, "ColorMode"),
        (0x0004, "Quality"), (0x0005, "WhiteBalance"), (0x0006, "Sharpening"),
        (0x0007, "Focus"), (0x0008, "FlashSetting"), (0x0009, "FlashDevice"),
        (0x000B, "WhiteBalanceBias"), (0x000C, "WB_RBLevels"), (0x000D, "ProgramShift"),
        (0x000E, "ExposureDiff"), (0x000F, "ISOSelection"), (0x0010, "DataDump"),
        (0x0011, "Preview"), (0x0012, "FlashComp"), (0x0013, "ISOSettings"),
        (0x0016, "ImageBoundary"), (0x0017, "FlashExposureComp"), (0x0018, "FlashBracketComp"),
        (0x0019, "ExposureBracketComp"), (0x001A, "ImageProcessing"), (0x001B, "CropHiSpeed"),
        (0x001C, "ExposureTuning"), (0x001D, "SerialNumber"), (0x001E, "ColorSpace"),
        (0x001F, "VRInfo"), (0x0020, "ImageAuthentication"), (0x0022, "ActiveDLighting"),
        (0x0023, "PictureControl"), (0x0024, "WorldTime"), (0x0025, "ISOInfo"),
        (0x002A, "VignetteControl"), (0x0034, "ShutterMode"), (0x0037, "MechanicalShutterCount"),
        (0x003F, "WhiteBalanceBias2"), (0x0080, "ImageAdjustment"), (0x0081, "ToneComp"),
        (0x0082, "AuxiliaryLens"), (0x0083, "LensType"), (0x0084, "Lens"),
        (0x0085, "FocusDistance"), (0x0086, "DigitalZoom"), (0x0087, "FlashMode"),
        (0x0088, "AFInfo"), (0x0089, "ShootingMode"), (0x008A, "AutoBracketRelease"),
        (0x008B, "LensFStops"), (0x008C, "ContrastCurve"), (0x008D, "ColorHue"),
        (0x008F, "SceneMode"), (0x0090, "LightSource"), (0x0091, "ShotInfo"),
        (0x0092, "HueAdjustment"), (0x0093, "NEFCompression"), (0x0094, "Saturation"),
        (0x0095, "NoiseReduction"), (0x0096, "LinearizationTable"), (0x0097, "ColorBalance"),
        (0x0098, "LensData"), (0x0099, "RawImageCenter"), (0x009A, "SensorPixelSize"),
        (0x009C, "SceneAssist"), (0x009E, "RetouchHistory"), (0x00A0, "SerialNO"),
        (0x00A2, "ImageDataSize"), (0x00A5, "ImageCount"), (0x00A6, "DeletedImageCount"),
        (0x00A7, "ShutterCount"), (0x00A8, "FlashInfo"), (0x00A9, "ImageOptimization"),
        (0x00AA, "Saturation2"), (0x00AB, "VariProgram"), (0x00AC, "ImageStabilization"),
        (0x00AD, "AFResponse"), (0x00B0, "MultiExposure"), (0x00B1, "HighISONoiseReduction"),
        (0x00B3, "ToningEffect"), (0x00B7, "AFInfo2"), (0x00B8, "FileInfo"),
        (0x00B9, "AFTune"), (0x00C3, "BarometerInfo"), (0x0E00, "PrintIM"),
        (0x0E01, "CaptureData"), (0x0E09, "CaptureVersion"), (0x0E0E, "CaptureOffsets"),
        (0x0E10, "ScanIFD"), (0x0E1D, "ICCProfile"), (0x0E1E, "CaptureOutput"),
    ];
}
