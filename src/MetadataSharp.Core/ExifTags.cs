// The tables name field types bare, `Types = [Short]`, because 198 rows of `TiffFieldType.` would push
// every long one onto a second line. `Byte` is the one name spelled like a type the implicit `using System`
// exports, and CS0229 says a simple name cannot be contested between them, so that member stays qualified.
using System.Collections.Immutable;
using static MetadataSharp.Core.TiffFieldType;

namespace MetadataSharp.Core;

/// <summary>What a publication prescribes for one tag: its name, field types, counts, and value meanings.</summary>
/// <remarks>
/// A definition is descriptive knowledge about a format, for naming and printing. It is not a validation
/// rule for a file: the reader reports what a file contains, and a value that disagrees with its
/// definition is still reported as found. On the write side <see cref="ExifEdit.Set(ExifTagKey, ExifValue)" />
/// holds a listed tag to the types and counts stated here, and <see cref="ExifEdit.SetUnchecked" />
/// writes any value as given.
/// </remarks>
public sealed record ExifTagDefinition
{
    /// <summary>The tag number within its directory.</summary>
    public required ushort Id { get; init; }

    /// <summary>The field name as the publication spells it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The field types the tag may carry, in the order the publication lists them. Two entries mean either
    /// is written. For a tag of Exif or TIFF, none is only for the tags whose publication says to use
    /// whichever type matches the samples; for a tag of any other <see cref="Source" />, none means the
    /// shape was not recorded. A type of 129, which Exif allows in place of ASCII, is not part of the raw value model and
    /// is recorded by <see cref="ExifTagDefinition.Utf8TextAlternative" /> instead. These arrays come out
    /// of the one table, so they are immutable: a resolved definition cannot be turned against it.
    /// </summary>
    public ImmutableArray<TiffFieldType> Types { get; init; } = ImmutableArray<TiffFieldType>.Empty;

    /// <summary>
    /// The number of values, when the publication fixes one for the tag, otherwise 0; this is <see
    /// cref="Counts" /> read back, never a second statement of it.
    /// </summary>
    public uint FixedCount => this.Counts.Length == 1 ? this.Counts[0] : 0U;

    /// <summary>
    /// Every value count the publication allows, ascending; empty when the count varies with the image, and
    /// for every tag whose <see cref="Source" /> is not Exif or TIFF, whose shape is not recorded.
    /// This is the complete statement; a tag with one allowed count reads it back as <see
    /// cref="FixedCount" />.
    /// </summary>
    public ImmutableArray<uint> Counts { get; init; } = ImmutableArray<uint>.Empty;

    /// <summary>
    /// The publication the name was taken from. Only a tag of <see cref="ExifTagSource.ExifAndTiff" />
    /// carries more than its number and its name.
    /// </summary>
    public ExifTagSource Source { get; init; }

    /// <summary>True where the publication allows UTF-8 in place of ASCII for this tag.</summary>
    public bool Utf8TextAlternative { get; init; }

    /// <summary>What the publication gives as the default value, in its own notation; null for None.</summary>
    public string? Default { get; init; }

    /// <summary>
    /// What the numeric values mean, in the publication's own words, for the tags that enumerate them, and
    /// empty for the rest. Bit fields such as Flash are left out: their meaning is not a number-to-label
    /// table.
    /// </summary>
    public ImmutableArray<ExifTagValueMeaning> ValueMeanings { get; init; }
        = ImmutableArray<ExifTagValueMeaning>.Empty;
}

/// <summary>The publication that names a tag.</summary>
public enum ExifTagSource
{
    /// <summary>Exif 3.0 (CIPA DC-008) and the TIFF 6.0 fields it builds on: name, types, counts, meanings.</summary>
    ExifAndTiff,

    /// <summary>ISO 12234-2 (TIFF/EP), for the fields it places in the main directory of a raw file.</summary>
    TiffEp,

    /// <summary>The Adobe Digital Negative specification, version 1.7.</summary>
    Dng,

    /// <summary>The Design rule for Camera File system, for the fields of the interoperability directory.</summary>
    Dcf,

    /// <summary>
    /// A field one vendor defined and documented: the Microsoft rating and XP text fields, and the PRINT
    /// Image Matching block.
    /// </summary>
    Vendor,
}

/// <summary>One numeric value of an enumerated tag.</summary>
/// <param name="Number">The field value.</param>
/// <param name="Meaning">The wording the publication uses for it.</param>
public readonly record struct ExifTagValueMeaning(ushort Number, string Meaning);

/// <summary>The tag vocabulary of the supported EXIF directories: one definition per tag, one lookup.</summary>
/// <remarks>
/// The rows are generated from the publications this project reviewed for its read policy (see
/// docs/archive/design/TIFF_EXIF_NORMATIVE_SOURCES_AND_READ_POLICY.md). Each field of a tag is stated in a row of
/// an attribute table and repeated, and sometimes qualified, in the section that describes the tag; where
/// the two disagree the section is the fuller statement and wins. That happens once in the reviewed text:
/// Exif 3.0 Table 9 gives GainControl as RATIONAL while its section 4.6.6.7.41 gives SHORT and enumerates
/// 0 to 4, and files carry SHORT, so SHORT with its value list is what is recorded here. One row of the
/// 1992 appendix contradicts its own body the same way, ExtraSamples as BYTE against SHORT, and is
/// resolved the same way. Two counts are conditional in the texts themselves and are recorded as the
/// Exif section gives them: TransferFunction is 3 * 256 for Exif, while TIFF's body ties the length of the
/// table to BitsPerSample, and DotRange is 2 or 2 * SamplesPerPixel, so it carries no fixed count at all.
/// <para>
/// Tags of four other sources are listed by number and name only, so that a file is not reported in
/// hexadecimal where a public specification has a word for the field: TIFF/EP, DNG, DCF and the fields
/// of two vendors, each marked by <see cref="ExifTagDefinition.Source" />. Nothing but the number and the name is
/// taken from them; types, counts and value meanings stay with the reviewed Exif and TIFF tables. Where
/// Exif and one of them name the same number in the same directory, the Exif row is the one kept. A tag
/// none of them names keeps its numeric ID, and a MakerNote subtag is never renamed by a name that belongs
/// to a different directory.
/// </para>
/// </remarks>
public static partial class ExifTags
{

    // Fields of the Ifd0 and Thumbnail directories.
    // Shapes come from Exif 3.0 Table 6 with the TIFF 6.0 tag registry, and the section describing each field.
    private static readonly ExifTagDefinition[] Ifd0 =
        [
            new() { Id = 0x00FE, Name = "NewSubfileType", Types = [Long], Counts = [1U] },
            new() { Id = 0x00FF, Name = "SubfileType", Types = [Short], Counts = [1U] },
            new() { Id = 0x0100, Name = "ImageWidth", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0x0101, Name = "ImageLength", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0x0102, Name = "BitsPerSample", Types = [Short], Counts = [3U], Default = "8 8 8" },
            new()
            {
                Id = 0x0103, Name = "Compression", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(1, "uncompressed"),
                    new(6, "JPEG compression (thumbnails only)"),
                ],
            },
            new()
            {
                Id = 0x0106, Name = "PhotometricInterpretation", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(2, "RGB"),
                    new(6, "YCbCr"),
                ],
            },
            new() { Id = 0x0107, Name = "Threshholding", Types = [Short], Counts = [1U] },
            new() { Id = 0x0108, Name = "CellWidth", Types = [Short], Counts = [1U] },
            new() { Id = 0x0109, Name = "CellLength", Types = [Short], Counts = [1U] },
            new() { Id = 0x010A, Name = "FillOrder", Types = [Short], Counts = [1U] },
            new() { Id = 0x010D, Name = "DocumentName", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0x010E,
                Name = "ImageDescription",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0x010F, Name = "Make", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0x0110, Name = "Model", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new() { Id = 0x0111, Name = "StripOffsets", Types = [Short, Long], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0x0112, Name = "Orientation", Types = [Short], Counts = [1U], Default = "1",
                ValueMeanings =
                [
                    new(1, "The 0th row is at the visual top of the image, and the 0th column is the visual left-hand side"),
                    new(2, "The 0th row is at the visual top of the image, and the 0th column is the visual right-hand side"),
                    new(3, "The 0th row is at the visual bottom of the image, and the 0th column is the visual right-hand side"),
                    new(4, "The 0th row is at the visual bottom of the image, and the 0th column is the visual left-hand side"),
                    new(5, "The 0th row is the visual left-hand side of the image, and the 0th column is the visual top"),
                    new(6, "The 0th row is the visual right-hand side of the image, and the 0th column is the visual top"),
                    new(7, "The 0th row is the visual right-hand side of the image, and the 0th column is the visual bottom"),
                    new(8, "The 0th row is the visual left-hand side of the image, and the 0th column is the visual bottom"),
                ],
            },
            new() { Id = 0x0115, Name = "SamplesPerPixel", Types = [Short], Counts = [1U], Default = "3" },
            new() { Id = 0x0116, Name = "RowsPerStrip", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0x0117, Name = "StripByteCounts", Types = [Short, Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0118, Name = "MinSampleValue", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0119, Name = "MaxSampleValue", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x011A, Name = "XResolution", Types = [Rational], Counts = [1U], Default = "72" },
            new() { Id = 0x011B, Name = "YResolution", Types = [Rational], Counts = [1U], Default = "72" },
            new()
            {
                Id = 0x011C, Name = "PlanarConfiguration", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(1, "chunky format"),
                    new(2, "planar format"),
                ],
            },
            new() { Id = 0x011D, Name = "PageName", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x011E, Name = "XPosition", Types = [Rational], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x011F, Name = "YPosition", Types = [Rational], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0120, Name = "FreeOffsets", Types = [Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0121, Name = "FreeByteCounts", Types = [Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0122, Name = "GrayResponseUnit", Types = [Short], Counts = [1U] },
            new() { Id = 0x0123, Name = "GrayResponseCurve", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0124, Name = "T4Options", Types = [Long], Counts = [1U] },
            new() { Id = 0x0125, Name = "T6Options", Types = [Long], Counts = [1U] },
            new()
            {
                Id = 0x0128, Name = "ResolutionUnit", Types = [Short], Counts = [1U], Default = "2",
                ValueMeanings =
                [
                    new(2, "inches"),
                    new(3, "centimeters"),
                ],
            },
            new() { Id = 0x0129, Name = "PageNumber", Types = [Short], Counts = [2U] },
            new() { Id = 0x012D, Name = "TransferFunction", Types = [Short], Counts = [768U] },
            new()
            {
                Id = 0x0131, Name = "Software", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new() { Id = 0x0132, Name = "DateTime", Types = [Ascii], Counts = [20U] },
            new()
            {
                Id = 0x013B, Name = "Artist", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new() { Id = 0x013C, Name = "HostComputer", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x013D, Name = "Predictor", Types = [Short], Counts = [1U] },
            new() { Id = 0x013E, Name = "WhitePoint", Types = [Rational], Counts = [2U] },
            new() { Id = 0x013F, Name = "PrimaryChromaticities", Types = [Rational], Counts = [6U] },
            new() { Id = 0x0140, Name = "ColorMap", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0141, Name = "HalftoneHints", Types = [Short], Counts = [2U] },
            new() { Id = 0x0142, Name = "TileWidth", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0x0143, Name = "TileLength", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0x0144, Name = "TileOffsets", Types = [Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0145, Name = "TileByteCounts", Types = [Short, Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x014C, Name = "InkSet", Types = [Short], Counts = [1U] },
            new() { Id = 0x014D, Name = "InkNames", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x014E, Name = "NumberOfInks", Types = [Short], Counts = [1U] },
            new() { Id = 0x0150, Name = "DotRange", Types = [TiffFieldType.Byte, Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0151, Name = "TargetPrinter", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0152, Name = "ExtraSamples", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0153, Name = "SampleFormat", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0x0154,
                Name = "SMinSampleValue",
                Types = ImmutableArray<TiffFieldType>.Empty,
                Counts = ImmutableArray<uint>.Empty,
            },
            new()
            {
                Id = 0x0155,
                Name = "SMaxSampleValue",
                Types = ImmutableArray<TiffFieldType>.Empty,
                Counts = ImmutableArray<uint>.Empty,
            },
            new() { Id = 0x0156, Name = "TransferRange", Types = [Short], Counts = [6U] },
            new() { Id = 0x0200, Name = "JPEGProc", Types = [Short], Counts = [1U] },
            new() { Id = 0x0201, Name = "JPEGInterchangeFormat", Types = [Long], Counts = [1U] },
            new() { Id = 0x0202, Name = "JPEGInterchangeFormatLength", Types = [Long], Counts = [1U] },
            new() { Id = 0x0203, Name = "JPEGRestartInterval", Types = [Short], Counts = [1U] },
            new() { Id = 0x0205, Name = "JPEGLosslessPredictors", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0206, Name = "JPEGPointTransforms", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0207, Name = "JPEGQTables", Types = [Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0208, Name = "JPEGDCTables", Types = [Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0209, Name = "JPEGACTables", Types = [Long], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0211, Name = "YCbCrCoefficients", Types = [Rational], Counts = [3U], Default = "See Annex D" },
            new() { Id = 0x0212, Name = "YCbCrSubSampling", Types = [Short], Counts = [2U] },
            new()
            {
                Id = 0x0213, Name = "YCbCrPositioning", Types = [Short], Counts = [1U], Default = "1",
                ValueMeanings =
                [
                    new(1, "centered"),
                    new(2, "co-sited"),
                ],
            },
            new()
            {
                Id = 0x0214,
                Name = "ReferenceBlackWhite",
                Types = [Rational],
                Counts = [6U],
                Default = "[0, 255, 0, 255, 0, 255] (when PhotometricInterpretation is RGB)",
            },
            new()
            {
                Id = 0x8298, Name = "Copyright", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new() { Id = 0x8769, Name = "ExifIFDPointer", Types = [Long], Counts = [1U] },
            new() { Id = 0x8825, Name = "GPSInfoIFDPointer", Types = [Long], Counts = [1U] },
        ];
    // Fields of the Exif directory.
    // Shapes come from Exif 3.0 Tables 8 and 9, and the section describing each tag.
    private static readonly ExifTagDefinition[] Exif =
        [
            new() { Id = 0x829A, Name = "ExposureTime", Types = [Rational], Counts = [1U] },
            new() { Id = 0x829D, Name = "FNumber", Types = [Rational], Counts = [1U] },
            new()
            {
                Id = 0x8822, Name = "ExposureProgram", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Not defined"),
                    new(1, "Manual"),
                    new(2, "Normal program"),
                    new(3, "Aperture priority"),
                    new(4, "Shutter priority"),
                    new(5, "Creative program (biased toward depth of field)"),
                    new(6, "Action program (biased toward fast shutter speed)"),
                    new(7, "Portrait mode (for closeup photos with the background out of focus)"),
                    new(8, "Landscape mode (for landscape photos with the background in focus)"),
                ],
            },
            new() { Id = 0x8824, Name = "SpectralSensitivity", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x8827, Name = "PhotographicSensitivity", Types = [Short], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x8828, Name = "OECF", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0x8830, Name = "SensitivityType", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(0, "Unknown"),
                    new(1, "Standard output sensitivity (SOS)"),
                    new(2, "Recommended exposure index (REI)"),
                    new(3, "ISO speed"),
                    new(4, "Standard output sensitivity (SOS) and recommended exposure index (REI)"),
                    new(5, "Standard output sensitivity (SOS) and ISO speed"),
                    new(6, "Recommended exposure index (REI) and ISO speed"),
                    new(7, "Standard output sensitivity (SOS) and recommended exposure index (REI) and ISO speed"),
                ],
            },
            new() { Id = 0x8831, Name = "StandardOutputSensitivity", Types = [Long], Counts = [1U] },
            new() { Id = 0x8832, Name = "RecommendedExposureIndex", Types = [Long], Counts = [1U] },
            new() { Id = 0x8833, Name = "ISOSpeed", Types = [Long], Counts = [1U] },
            new() { Id = 0x8834, Name = "ISOSpeedLatitudeyyy", Types = [Long], Counts = [1U] },
            new() { Id = 0x8835, Name = "ISOSpeedLatitudezzz", Types = [Long], Counts = [1U] },
            new() { Id = 0x9000, Name = "ExifVersion", Types = [Undefined], Counts = [4U], Default = "\"0300”" },
            new() { Id = 0x9003, Name = "DateTimeOriginal", Types = [Ascii], Counts = [20U] },
            new() { Id = 0x9004, Name = "DateTimeDigitized", Types = [Ascii], Counts = [20U] },
            new() { Id = 0x9010, Name = "OffsetTime", Types = [Ascii], Counts = [7U] },
            new() { Id = 0x9011, Name = "OffsetTimeOriginal", Types = [Ascii], Counts = [7U] },
            new() { Id = 0x9012, Name = "OffsetTimeDigitized", Types = [Ascii], Counts = [7U] },
            new()
            {
                Id = 0x9101,
                Name = "ComponentsConfiguration",
                Types = [Undefined],
                Counts = [4U],
                Default = "4 5 6 0 (if RGB uncompressed) 1 2 3 0 (other cases)",
                ValueMeanings =
                [
                    new(0, "does not exist"),
                    new(1, "Y"),
                    new(2, "Cb"),
                    new(3, "Cr"),
                    new(4, "R"),
                    new(5, "G"),
                    new(6, "B"),
                ],
            },
            new() { Id = 0x9102, Name = "CompressedBitsPerPixel", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9201, Name = "ShutterSpeedValue", Types = [SRational], Counts = [1U] },
            new() { Id = 0x9202, Name = "ApertureValue", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9203, Name = "BrightnessValue", Types = [SRational], Counts = [1U] },
            new() { Id = 0x9204, Name = "ExposureBiasValue", Types = [SRational], Counts = [1U] },
            new() { Id = 0x9205, Name = "MaxApertureValue", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9206, Name = "SubjectDistance", Types = [Rational], Counts = [1U] },
            new()
            {
                Id = 0x9207, Name = "MeteringMode", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "unknown"),
                    new(1, "Average"),
                    new(2, "CenterWeightedAverage"),
                    new(3, "Spot"),
                    new(4, "Multi-spot"),
                    new(5, "Pattern"),
                    new(6, "Partial"),
                    new(255, "other"),
                ],
            },
            new()
            {
                Id = 0x9208, Name = "LightSource", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "unknown"),
                    new(1, "Daylight"),
                    new(2, "Fluorescent"),
                    new(3, "Tungsten (incandescent light)"),
                    new(4, "Flash"),
                    new(9, "Fine weather"),
                    new(10, "Cloudy weather"),
                    new(11, "Shade"),
                    new(12, "Daylight fluorescent (D 5700 - 7100K)"),
                    new(13, "Day white fluorescent (N 4600 - 5500K)"),
                    new(14, "Cool white fluorescent (W 3800 - 4500K)"),
                    new(15, "White fluorescent (WW 3250 - 3800K)"),
                    new(16, "Warm white fluorescent (L 2600 - 3250K)"),
                    new(17, "Standard light A"),
                    new(18, "Standard light B"),
                    new(19, "Standard light C"),
                    new(20, "D55"),
                    new(21, "D65"),
                    new(22, "D75"),
                    new(23, "D50"),
                    new(24, "ISO studio tungsten"),
                    new(255, "other light source"),
                ],
            },
            new() { Id = 0x9209, Name = "Flash", Types = [Short], Counts = [1U] },
            new() { Id = 0x920A, Name = "FocalLength", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9214, Name = "SubjectArea", Types = [Short], Counts = [2U, 3U, 4U] },
            new() { Id = 0x927C, Name = "MakerNote", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x9286, Name = "UserComment", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x9290, Name = "SubSecTime", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x9291, Name = "SubSecTimeOriginal", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x9292, Name = "SubSecTimeDigitized", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x9400, Name = "Temperature", Types = [SRational], Counts = [1U] },
            new() { Id = 0x9401, Name = "Humidity", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9402, Name = "Pressure", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9403, Name = "WaterDepth", Types = [SRational], Counts = [1U] },
            new() { Id = 0x9404, Name = "Acceleration", Types = [Rational], Counts = [1U] },
            new() { Id = 0x9405, Name = "CameraElevationAngle", Types = [SRational], Counts = [1U] },
            new() { Id = 0xA000, Name = "FlashpixVersion", Types = [Undefined], Counts = [4U], Default = "\"0100\"" },
            new()
            {
                Id = 0xA001, Name = "ColorSpace", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(1, "sRGB"),
                ],
            },
            new() { Id = 0xA002, Name = "PixelXDimension", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0xA003, Name = "PixelYDimension", Types = [Short, Long], Counts = [1U] },
            new() { Id = 0xA004, Name = "RelatedSoundFile", Types = [Ascii], Counts = [13U] },
            new() { Id = 0xA005, Name = "InteroperabilityIFDPointer", Types = [Long], Counts = [1U] },
            new() { Id = 0xA20B, Name = "FlashEnergy", Types = [Rational], Counts = [1U] },
            new() { Id = 0xA20C, Name = "SpatialFrequencyResponse", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0xA20E, Name = "FocalPlaneXResolution", Types = [Rational], Counts = [1U] },
            new() { Id = 0xA20F, Name = "FocalPlaneYResolution", Types = [Rational], Counts = [1U] },
            new() { Id = 0xA210, Name = "FocalPlaneResolutionUnit", Types = [Short], Counts = [1U], Default = "2 (inch)" },
            new() { Id = 0xA214, Name = "SubjectLocation", Types = [Short], Counts = [2U] },
            new() { Id = 0xA215, Name = "ExposureIndex", Types = [Rational], Counts = [1U] },
            new()
            {
                Id = 0xA217, Name = "SensingMethod", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(1, "Not defined"),
                    new(2, "One-chip color area sensor"),
                    new(3, "Two-chip color area sensor"),
                    new(4, "Three-chip color area sensor"),
                    new(5, "Color sequential area sensor"),
                    new(7, "Trilinear sensor"),
                    new(8, "Color sequential linear sensor"),
                ],
            },
            new()
            {
                Id = 0xA300, Name = "FileSource", Types = [Undefined], Counts = [1U], Default = "3",
                ValueMeanings =
                [
                    new(0, "others"),
                    new(1, "scanner of transparent type"),
                    new(2, "scanner of reflex type"),
                    new(3, "DSC"),
                ],
            },
            new()
            {
                Id = 0xA301, Name = "SceneType", Types = [Undefined], Counts = [1U], Default = "1",
                ValueMeanings =
                [
                    new(1, "A directly photographed image"),
                ],
            },
            new() { Id = 0xA302, Name = "CFAPattern", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0xA401, Name = "CustomRendered", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Normal process"),
                    new(1, "Custom process"),
                ],
            },
            new()
            {
                Id = 0xA402, Name = "ExposureMode", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(0, "Auto exposure"),
                    new(1, "Manual exposure"),
                    new(2, "Auto bracket"),
                ],
            },
            new()
            {
                Id = 0xA403, Name = "WhiteBalance", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(0, "Auto white balance"),
                    new(1, "Manual white balance"),
                ],
            },
            new() { Id = 0xA404, Name = "DigitalZoomRatio", Types = [Rational], Counts = [1U] },
            new() { Id = 0xA405, Name = "FocalLengthIn35mmFilm", Types = [Short], Counts = [1U] },
            new()
            {
                Id = 0xA406, Name = "SceneCaptureType", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Standard"),
                    new(1, "Landscape"),
                    new(2, "Portrait"),
                    new(3, "Night scene"),
                ],
            },
            new()
            {
                Id = 0xA407, Name = "GainControl", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(0, "None"),
                    new(1, "Low gain up"),
                    new(2, "High gain up"),
                    new(3, "Low gain down"),
                    new(4, "High gain down"),
                ],
            },
            new()
            {
                Id = 0xA408, Name = "Contrast", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Normal"),
                    new(1, "Soft"),
                    new(2, "Hard"),
                ],
            },
            new()
            {
                Id = 0xA409, Name = "Saturation", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Normal"),
                    new(1, "Low saturation"),
                    new(2, "High saturation"),
                ],
            },
            new()
            {
                Id = 0xA40A, Name = "Sharpness", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Normal"),
                    new(1, "Soft"),
                    new(2, "Hard"),
                ],
            },
            new() { Id = 0xA40B, Name = "DeviceSettingDescription", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0xA40C, Name = "SubjectDistanceRange", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(0, "unknown"),
                    new(1, "Macro"),
                    new(2, "Close view"),
                    new(3, "Distant view"),
                ],
            },
            new() { Id = 0xA420, Name = "ImageUniqueID", Types = [Ascii], Counts = [33U] },
            new()
            {
                Id = 0xA430,
                Name = "CameraOwnerName",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new() { Id = 0xA431, Name = "BodySerialNumber", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0xA432, Name = "LensSpecification", Types = [Rational], Counts = [4U] },
            new()
            {
                Id = 0xA433, Name = "LensMake", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA434, Name = "LensModel", Types = [Ascii], Counts = ImmutableArray<uint>.Empty, Utf8TextAlternative = true,
            },
            new() { Id = 0xA435, Name = "LensSerialNumber", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new()
            {
                Id = 0xA436,
                Name = "ImageTitle",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA437,
                Name = "Photographer",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA438,
                Name = "ImageEditor",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA439,
                Name = "CameraFirmware",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA43A,
                Name = "RAWDevelopingSoftware",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA43B,
                Name = "ImageEditingSoftware",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA43C,
                Name = "MetadataEditingSoftware",
                Types = [Ascii],
                Counts = ImmutableArray<uint>.Empty,
                Utf8TextAlternative = true,
            },
            new()
            {
                Id = 0xA460, Name = "CompositeImage", Types = [Short], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Unknown"),
                    new(1, "NonComposite"),
                    new(2, "GeneralComposite"),
                    new(3, "CompositeCapturedWhenShooting"),
                ],
            },
            new() { Id = 0xA461, Name = "SourceImageNumberOfCompositeImage", Types = [Short], Counts = [2U] },
            new()
            {
                Id = 0xA462,
                Name = "SourceExposureTimesOfCompositeImage",
                Types = [Undefined],
                Counts = ImmutableArray<uint>.Empty,
            },
            new() { Id = 0xA500, Name = "Gamma", Types = [Rational], Counts = [1U] },
        ];
    // Fields of the Gps directory.
    // Shapes come from Exif 3.0 Table 14, and the section describing each tag.
    private static readonly ExifTagDefinition[] Gps =
        [
            new() { Id = 0x0000, Name = "GPSVersionID", Types = [TiffFieldType.Byte], Counts = [4U], Default = "2.4.0.0" },
            new() { Id = 0x0001, Name = "GPSLatitudeRef", Types = [Ascii], Counts = [2U] },
            new() { Id = 0x0002, Name = "GPSLatitude", Types = [Rational], Counts = [3U] },
            new() { Id = 0x0003, Name = "GPSLongitudeRef", Types = [Ascii], Counts = [2U] },
            new() { Id = 0x0004, Name = "GPSLongitude", Types = [Rational], Counts = [3U] },
            new()
            {
                Id = 0x0005, Name = "GPSAltitudeRef", Types = [TiffFieldType.Byte], Counts = [1U], Default = "0",
                ValueMeanings =
                [
                    new(0, "Positive ellipsoidal height (at or above ellipsoidal surface)"),
                    new(1, "Negative ellipsoidal height (below ellipsoidal surface)"),
                    new(2, "Positive sea level value (at or above sea level reference)"),
                    new(3, "Negative sea level value (below sea level reference)"),
                ],
            },
            new() { Id = 0x0006, Name = "GPSAltitude", Types = [Rational], Counts = [1U] },
            new() { Id = 0x0007, Name = "GPSTimeStamp", Types = [Rational], Counts = [3U] },
            new() { Id = 0x0008, Name = "GPSSatellites", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0009, Name = "GPSStatus", Types = [Ascii], Counts = [2U] },
            new() { Id = 0x000A, Name = "GPSMeasureMode", Types = [Ascii], Counts = [2U] },
            new() { Id = 0x000B, Name = "GPSDOP", Types = [Rational], Counts = [1U] },
            new() { Id = 0x000C, Name = "GPSSpeedRef", Types = [Ascii], Counts = [2U], Default = "'K'" },
            new() { Id = 0x000D, Name = "GPSSpeed", Types = [Rational], Counts = [1U] },
            new() { Id = 0x000E, Name = "GPSTrackRef", Types = [Ascii], Counts = [2U], Default = "'T'" },
            new() { Id = 0x000F, Name = "GPSTrack", Types = [Rational], Counts = [1U] },
            new() { Id = 0x0010, Name = "GPSImgDirectionRef", Types = [Ascii], Counts = [2U], Default = "'T'" },
            new() { Id = 0x0011, Name = "GPSImgDirection", Types = [Rational], Counts = [1U] },
            new() { Id = 0x0012, Name = "GPSMapDatum", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x0013, Name = "GPSDestLatitudeRef", Types = [Ascii], Counts = [2U] },
            new() { Id = 0x0014, Name = "GPSDestLatitude", Types = [Rational], Counts = [3U] },
            new() { Id = 0x0015, Name = "GPSDestLongitudeRef", Types = [Ascii], Counts = [2U] },
            new() { Id = 0x0016, Name = "GPSDestLongitude", Types = [Rational], Counts = [3U] },
            new() { Id = 0x0017, Name = "GPSDestBearingRef", Types = [Ascii], Counts = [2U], Default = "'T'" },
            new() { Id = 0x0018, Name = "GPSDestBearing", Types = [Rational], Counts = [1U] },
            new() { Id = 0x0019, Name = "GPSDestDistanceRef", Types = [Ascii], Counts = [2U], Default = "'K'" },
            new() { Id = 0x001A, Name = "GPSDestDistance", Types = [Rational], Counts = [1U] },
            new() { Id = 0x001B, Name = "GPSProcessingMethod", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x001C, Name = "GPSAreaInformation", Types = [Undefined], Counts = ImmutableArray<uint>.Empty },
            new() { Id = 0x001D, Name = "GPSDateStamp", Types = [Ascii], Counts = [11U] },
            new()
            {
                Id = 0x001E, Name = "GPSDifferential", Types = [Short], Counts = [1U],
                ValueMeanings =
                [
                    new(0, "Measurement without differential correction"),
                    new(1, "Differential correction applied"),
                ],
            },
            new() { Id = 0x001F, Name = "GPSHPositioningError", Types = [Rational], Counts = [1U] },
        ];
    // Fields of the Interop directory.
    // Shapes come from Exif 3.0 Table 16, and the section describing it.
    private static readonly ExifTagDefinition[] Interop =
        [
            new() { Id = 0x0001, Name = "InteroperabilityIndex", Types = [Ascii], Counts = ImmutableArray<uint>.Empty },
        ];

    // The reviewed tables above, joined with the rows of ExifTags.OtherSources.cs. These two must stay
    // below the tables and above ByKey: static fields are initialised in the order they are written.
    private static readonly ExifTagDefinition[] Ifd0Complete = Join(Ifd0, Ifd0FromOtherSources());

    private static readonly ExifTagDefinition[] InteropComplete = Join(Interop, InteropFromOtherSources());

    private static readonly Dictionary<ExifTagKey, ExifTagDefinition> ByKey = Build();

    /// <summary>What its publication says of one tag, or null for a tag the vocabulary does not list.</summary>
    /// <param name="key">The directory and tag ID to look up.</param>
    /// <returns>The tag definition, or <see langword="null"/> when the vocabulary has no entry for the key.</returns>
    public static ExifTagDefinition? Find(ExifTagKey key) => ByKey.GetValueOrDefault(key);

    /// <summary>
    /// The definitions of one directory, in ascending tag order. A thumbnail directory carries 0th IFD
    /// fields, so it shares those definitions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value that is not one of the supported directories.</exception>
    /// <param name="directory">The directory role whose definitions to retrieve.</param>
    /// <returns>The definitions in ascending tag ID order.</returns>
    public static IReadOnlyList<ExifTagDefinition> Definitions(ExifDirectoryKind directory) => directory switch
    {
        ExifDirectoryKind.Ifd0 => Ifd0Complete,
        ExifDirectoryKind.Thumbnail => Ifd0Complete,
        ExifDirectoryKind.Exif => Exif,
        ExifDirectoryKind.Gps => Gps,
        ExifDirectoryKind.Interop => InteropComplete,
        _ => throw new ArgumentOutOfRangeException(nameof(directory)),
    };

    // Index the complete, sorted vocabulary once; duplicate keys are an invalid table, not a last-write-wins override.
    private static Dictionary<ExifTagKey, ExifTagDefinition> Build()
    {
        var byKey = new Dictionary<ExifTagKey, ExifTagDefinition>();
        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
            foreach (ExifTagDefinition definition in Definitions(directory))
                byKey[new ExifTagKey(directory, definition.Id)] = definition;
        return byKey;
    }
}
