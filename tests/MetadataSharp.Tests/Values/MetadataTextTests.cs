using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// The character-set matrix and the three carriers whose text has a character set of its own: an EXIF
/// UserComment, the Windows XP tags and an IPTC packet's undecorated text.
/// </summary>
public sealed class MetadataTextTests
{
    /// <summary>Texts a character set may or may not be able to write; every charset writes one of them.</summary>
    private static readonly string[] Samples =
    [
        "Plain seven-bit text", "Grüße aus Köln", "Привет, мир", "Ελληνικά", "עברית", "日本語のテキスト",
        "中文文本", "한국어 텍스트", "€ 100 © 2024", "Ça va très bien",
    ];

    private static ExifValue Raw(string prefix, params byte[] body)
    {
        byte[] code = Encoding.ASCII.GetBytes(prefix.PadRight(8, '\0'));
        return ExifValue.FromRaw(TiffFieldType.Undefined, (uint)(code.Length + body.Length), Endianness.LittleEndian,
            [.. code, .. body]);
    }

    // ---- The matrix ------------------------------------------------------------------------------------

    [Fact]
    public void TheMatrixIsResolvedByThePlatformsOwnCodePages()
    {
        // The code-page provider is part of the .NET runtime: the source projects declare no package
        // reference, and ProjectInfrastructureTests fails the build if one appears. This is the other
        // half of that promise - a code page outside Latin-1 really is available.
        MetadataCharset cyrillic = MetadataCharsets.Get("windows-1251");
        Assert.Equal(1251, cyrillic.Encoding.CodePage);
        Assert.Equal(1251, cyrillic.CodePage);
        Assert.Equal(1251, Encoding.GetEncoding(1251).CodePage);
        Assert.Equal("Привет", cyrillic.Encoding.GetString(cyrillic.Encoding.GetBytes("Привет")));
    }

    [Theory]
    [InlineData("latin1", "ISO-8859-1")]
    [InlineData("Latin-1", "ISO-8859-1")]
    [InlineData("windows_1251", "windows-1251")]
    [InlineData("CP1251", "windows-1251")]
    [InlineData("UCS-2LE", "UTF-16LE")]
    [InlineData("utf-8", "UTF-8")]
    [InlineData("ASCII", "US-ASCII")]
    [InlineData("SJIS", "Shift_JIS")]
    [InlineData("BIG5", "Big5")]
    public void ANameIsMatchedWithoutRegardToCaseHyphensOrUnderscores(string name, string expected)
    {
        Assert.True(MetadataCharsets.TryGet(name, out MetadataCharset? charset));
        Assert.Equal(expected, charset.Name);
    }

    [Fact]
    public void ANameOutsideTheMatrixIsRefusedAndTheMessageListsTheNames()
    {
        Assert.False(MetadataCharsets.TryGet("klingon", out _));
        Assert.False(MetadataCharsets.TryGet(null, out _));
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => MetadataCharsets.Get("klingon"));
        Assert.Contains("windows-1251", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMatrixNamesEveryEntryOnceAndKnowsItsEncodings()
    {
        Assert.Equal(MetadataCharsets.All.Count, MetadataCharsets.Names.Count);
        Assert.Equal(MetadataCharsets.All.Count, MetadataCharsets.Names.Distinct(StringComparer.Ordinal).Count());
        foreach (MetadataCharset charset in MetadataCharsets.All)
        {
            Assert.Equal(charset.CodePage, charset.Encoding.CodePage);
            Assert.True(MetadataCharsets.TryGetName(charset.Encoding, out string? name));
            Assert.Equal(MetadataCharsets.Get(name!).CodePage, charset.CodePage);
        }
    }

    [Fact]
    public void EveryCharsetReadsBackWhatItWrites()
    {
        foreach (MetadataCharset charset in MetadataCharsets.All)
        {
            string? sample = Samples.FirstOrDefault(text => CanWrite(charset, text));
            Assert.True(sample is not null, $"{charset.Name} writes none of the samples.");
            byte[] bytes = charset.Encoding.GetBytes(sample!);
            Assert.Equal(sample, charset.Encoding.GetString(bytes));
        }
    }

    [Fact]
    public void ACharsetRefusesAByteItCannotRepresentRatherThanReplacingIt()
    {
        // 0x98 is undefined in windows-1252 and a byte of Windows-1251's own alphabet.
        Assert.Throws<DecoderFallbackException>(() => MetadataCharsets.Get("windows-1251").Encoding.GetString([0x98]));
        Assert.Throws<EncoderFallbackException>(() => MetadataCharsets.Get("ISO-8859-1").Encoding.GetBytes("Я"));
    }

    private static bool CanWrite(MetadataCharset charset, string text)
    {
        try
        {
            charset.Encoding.GetBytes(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    // ---- UserComment -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void AUnicodeCommentIsWrittenInTheByteOrderOfTheFile(Endianness order)
    {
        ExifValue value = MetadataText.UserComment("Grüße", UserCommentCharset.Unicode, order);
        Assert.Equal(order, value.ByteOrder);
        Assert.Equal("UNICODE\0", Encoding.ASCII.GetString(value.RawBytes[..8]));
        Assert.Equal("Grüße", MetadataText.UserComment(value));
        // The body follows the byte order: "G" then NUL, or NUL then "G".
        Assert.Equal(0x47, order == Endianness.LittleEndian ? value.RawBytes[8] : value.RawBytes[9]);
    }

    [Fact]
    public void AnAsciiCommentCarriesItsOwnPrefix()
    {
        ExifValue value = MetadataText.UserComment("Hello", UserCommentCharset.Ascii);
        Assert.Equal("ASCII\0\0\0", Encoding.ASCII.GetString(value.RawBytes[..8]));
        Assert.Equal("Hello", MetadataText.UserComment(value));
        Assert.Throws<EncoderFallbackException>(() => MetadataText.UserComment("Grüße", UserCommentCharset.Ascii));
    }

    [Fact]
    public void ACommentWithoutACharsetPrefixIsReadAsUtf8()
    {
        ExifValue value = Raw("", Encoding.UTF8.GetBytes("Grüße"));
        Assert.Equal("Grüße", MetadataText.UserComment(value));
    }

    [Fact]
    public void ACharsetNameReinterpretsTheBodyTheWayExiv2sMinusNDoes()
    {
        // -n says what the bytes of a UNICODE comment are: the eight-bit code page of the writer.
        byte[] body = MetadataCharsets.Get("windows-1251").Encoding.GetBytes("Привет");
        ExifValue value = Raw("UNICODE", body);
        Assert.Equal("Привет", MetadataText.UserComment(value, "windows-1251"));
        // Read as the UCS-2 it claims to be, the same bytes are not that text.
        Assert.NotEqual("Привет", MetadataText.UserComment(value));
    }

    [Fact]
    public void ACommentWithAnUnknownPrefixIsLeftToTheRawValue()
    {
        ExifValue value = Raw("JIS", [1, 2, 3]);
        Assert.Null(MetadataText.UserComment(value));
        Assert.Null(MetadataText.UserComment(ExifValue.FromRaw(TiffFieldType.Undefined, 4, Endianness.LittleEndian,
            [0x41, 0x53, 0x43, 0x49])));
    }

    [Fact]
    public void ThePaddingOfACommentIsNotPartOfItsText()
    {
        byte[] body = [.. Encoding.ASCII.GetBytes("Note"), 0, 0, 0x20, 0x20];
        Assert.Equal("Note", MetadataText.UserComment(Raw("ASCII", body)));
    }

    // ---- The Windows XP tags ---------------------------------------------------------------------------

    [Fact]
    public void AnXpTagIsUcs2LittleEndianWithATrailingNul()
    {
        ExifValue value = MetadataText.Xp("Grüße");
        Assert.Equal(TiffFieldType.Byte, value.FieldType);
        Assert.Equal(0x47, value.RawBytes[0]);
        Assert.Equal(0, value.RawBytes[1]);
        Assert.Equal(0, value.RawBytes[^1]);
        Assert.Equal(0, value.RawBytes[^2]);
        Assert.Equal("Grüße", MetadataText.Xp(value));
    }

    [Fact]
    public void AnXpTagWithAnOddByteOrInvalidTextGivesNoText()
    {
        ExifValue odd = ExifValue.FromRaw(TiffFieldType.Byte, 5, Endianness.LittleEndian,
            [0x47, 0, 0x72, 0, 0x41]);
        Assert.Equal("Gr", MetadataText.Xp(odd));
        // A lone high surrogate is not text.
        Assert.Null(MetadataText.Xp(ExifValue.FromRaw(TiffFieldType.Byte, 2, Endianness.LittleEndian, [0x00, 0xD8])));
        Assert.Null(MetadataText.Xp(ExifValue.FromRaw(TiffFieldType.Ascii, 4, Endianness.LittleEndian, "abcd"u8.ToArray())));
    }

    [Fact]
    public void TheXpTagsAreTheFiveOfThePrimaryDirectoryAndNothingElse()
    {
        for (ushort tag = 0x9C9B; tag <= 0x9C9F; tag++)
        {
            Assert.True(MetadataText.IsXpTag(new ExifTagKey(ExifDirectoryKind.Ifd0, tag)));
            Assert.False(MetadataText.IsXpTag(new ExifTagKey(ExifDirectoryKind.Exif, tag)));
        }
        Assert.False(MetadataText.IsXpTag(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x9C9A)));
    }

    // ---- The two are the interpretation's carriers as well ---------------------------------------------

    [Fact]
    public void TheInterpretationPrintsAnXpTagAsItsText()
    {
        ExifValue value = MetadataText.Xp("Grüße");
        Assert.Equal("Grüße", ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x9C9B), value));
        Assert.Equal("Grüße", ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x9C9F), value));
    }

    [Fact]
    public void TheInterpretationReadsACommentInTheNamedCharset()
    {
        ExifValue value = Raw("UNICODE", MetadataCharsets.Get("windows-1251").Encoding.GetBytes("Привет"));
        var options = new ExifInterpretationOptions { UserCommentCharset = "windows-1251" };
        Assert.Equal("Привет",
            ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Exif, 0x9286), value, options));
        Assert.Throws<ArgumentException>(() => new ExifInterpretationOptions { UserCommentCharset = "klingon" });
    }

    [Fact]
    public void ACommentWhoseBytesAreBinaryIsStillLeftToTheRawValue()
    {
        ExifValue value = Raw("UNICODE", [1, 0, 2, 0, 3, 0]);
        var options = new ExifInterpretationOptions { UserCommentCharset = "windows-1251" };
        Assert.Null(ExifTags.Interpret(new ExifTagKey(ExifDirectoryKind.Exif, 0x9286), value, options));
    }

    // ---- IPTC ------------------------------------------------------------------------------------------

    [Fact]
    public void AnIptcDatasetIsReadInTheCharsetTheCallerNames()
    {
        byte[] text = MetadataCharsets.Get("windows-1251").Encoding.GetBytes("Привет");
        // A packet that declares nothing but its record version: the old shape of an IIM block.
        byte[] bytes = [0x1c, 2, 0, 0, 2, 0, 4, 0x1c, 2, 5, 0, (byte)text.Length, .. text];
        IptcPacket packet = IptcPacket.Parse(bytes);
        Assert.Equal("Привет", packet.GetText(1, "windows-1251"));
        // The packet-wide reading has no marker to go by and falls back to ISO-8859-1, which is not
        // the text the photographer wrote.
        Assert.NotEqual("Привет", packet.GetText(1));
        Assert.Throws<ArgumentException>(() => packet.GetText(1, "klingon"));
        Assert.Throws<ArgumentOutOfRangeException>(() => packet.GetText(7, "windows-1251"));
    }
}
