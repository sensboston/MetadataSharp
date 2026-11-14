using System.Text;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Core;

/// <summary>
/// The character-set prefix of an EXIF <c>UserComment</c>: the eight bytes the standard puts in
/// front of the text, named the way Exiv2 names the four it knows.
/// </summary>
public enum UserCommentCharset
{
    /// <summary><c>ASCII\0\0\0</c>: seven-bit text.</summary>
    Ascii,

    /// <summary><c>UNICODE\0</c>: UTF-16 in the byte order of the file, without a byte-order mark.</summary>
    Unicode,

    /// <summary>Eight zero bytes: the writer declared no character set; the body is read as UTF-8.</summary>
    Undefined,
}

/// <summary>
/// The text of the three metadata carriers whose character set is not the file's own: an EXIF
/// <c>UserComment</c>, the Windows XP tags of the primary directory, and an IPTC packet's undecorated
/// text.
/// </summary>
/// <remarks>
/// <para>
/// Each carrier is read and written here in one place, so that a value written by
/// <see cref="UserComment(string, UserCommentCharset, Endianness)"/> is read back by
/// <see cref="UserComment(ExifValue, string?)"/> and the bytes are described by the same rule the
/// reference tool applies. The rules are Exiv2 0.28.9's: a <c>UNICODE</c> body is UCS-2 in the byte
/// order of the file unless a byte-order mark says otherwise, an XP tag is UCS-2 little-endian with
/// trailing NULs that carry no text, and a body with an unknown prefix is left to the raw value.
/// </para>
/// <para>
/// A character set is named as <see cref="MetadataCharsets"/> names it. Naming one says what the
/// bytes of a <c>UNICODE</c> comment are written in - the statement Exiv2's <c>-n</c> option makes
/// for its <c>fc</c> action - and never changes a byte. Without a name, the byte order of the file
/// decides, with a byte-order mark honored, which is what the reference tool's print does
/// (<c>CommentValue::detectCharset</c>).
/// </para>
/// </remarks>
public static class MetadataText
{
    private const int CommentPrefixLength = 8;

    /// <summary>The XP tags of the primary directory: <c>XPTitle</c>, <c>XPComment</c>, <c>XPAuthor</c>, <c>XPKeywords</c>, <c>XPSubject</c>.</summary>
    private static ReadOnlySpan<ushort> XpTags => [0x9C9B, 0x9C9C, 0x9C9D, 0x9C9E, 0x9C9F];

    /// <summary>True for a tag whose text is UCS-2 little-endian: the Windows XP tags of the primary directory.</summary>
    /// <param name="key">The directory and tag ID to test.</param>
    /// <returns><see langword="true"/> for <c>Exif.Image.XPTitle</c> and its four neighbours.</returns>
    public static bool IsXpTag(ExifTagKey key) =>
        key.Directory == ExifDirectoryKind.Ifd0 && XpTags.Contains(key.TagId);

    /// <summary>
    /// A <c>UserComment</c> value: the eight-byte character-set prefix of
    /// <paramref name="charset"/> and the text behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UserCommentCharset.Unicode"/> writes UTF-16 in <paramref name="byteOrder"/> without
    /// a byte-order mark, which is what the standard asks for; the other two write seven-bit ASCII and
    /// UTF-8. A <c>JIS</c> body is not written and is left to the raw value when it is read: the
    /// reference tool prints its bytes without converting them, so a text this library produced would
    /// not be what that tool shows.
    /// </para>
    /// <para>
    /// The caller answers for <paramref name="byteOrder"/>: it is the byte order of the file the value
    /// is written into, which a TIFF value carries and an edit does not convert.
    /// </para>
    /// </remarks>
    /// <exception cref="EncoderFallbackException">The text holds a character the character set cannot write.</exception>
    /// <param name="text">The text of the comment; a trailing NUL is not part of it.</param>
    /// <param name="charset">The character set the prefix names.</param>
    /// <param name="byteOrder">The byte order of the file the value belongs to.</param>
    /// <returns>The value to store behind <c>Exif.Photo.UserComment</c>.</returns>
    public static ExifValue UserComment(string text, UserCommentCharset charset = UserCommentCharset.Unicode,
        Endianness byteOrder = Endianness.LittleEndian)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReadOnlySpan<byte> code = charset switch
        {
            UserCommentCharset.Ascii => "ASCII\0\0\0"u8,
            UserCommentCharset.Unicode => "UNICODE\0"u8,
            _ => "\0\0\0\0\0\0\0\0"u8,
        };
        byte[] body = charset switch
        {
            UserCommentCharset.Ascii => MetadataCharsets.Create(20127).GetBytes(text),
            UserCommentCharset.Unicode => new UnicodeEncoding(byteOrder == Endianness.BigEndian, false, true).GetBytes(text),
            _ => MetadataCharsets.Create(65001).GetBytes(text),
        };
        byte[] raw = new byte[CommentPrefixLength + body.Length];
        code.CopyTo(raw);
        body.CopyTo(raw, CommentPrefixLength);
        return ExifValue.FromRaw(TiffFieldType.Undefined, (uint)raw.Length, byteOrder, raw);
    }

    /// <summary>
    /// The text of a <c>UserComment</c>: the body behind its prefix, without the padding the standard
    /// adds, or <see langword="null"/> when the prefix names a character set this library does not know
    /// or the body does not decode in the character set it names.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <param name="charset">
    /// The character set the <c>UNICODE</c> body is written in, named as <see cref="MetadataCharsets"/>
    /// names it - what Exiv2's <c>-n</c> states for its <c>fc</c> action; null reads the body as UCS-2
    /// in the byte order of the value, with a byte-order mark honored, which is what the reference
    /// tool's print does.
    /// </param>
    /// <returns>The text, or <see langword="null"/> when there is none to read.</returns>
    /// <exception cref="ArgumentException">The name is not a character set of <see cref="MetadataCharsets"/>.</exception>
    public static string? UserComment(ExifValue value, string? charset = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        Encoding? encoding = charset is null ? null : MetadataCharsets.Get(charset).Encoding;
        return DecodeUserComment(value, encoding);
    }

    /// <summary>The text of an XP tag: UCS-2 little-endian, without its trailing NULs.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <param name="value">The stored value of <see cref="IsXpTag"/>.</param>
    /// <returns>The text, or <see langword="null"/> when the value is not a byte run or does not decode.</returns>
    public static string? Xp(ExifValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return DecodeXp(value);
    }

    /// <summary>An XP tag value: the text as UCS-2 little-endian bytes and the NUL that ends it.</summary>
    /// <exception cref="EncoderFallbackException">The text holds a character UTF-16 cannot write.</exception>
    /// <param name="text">The text; a trailing NUL is not part of it.</param>
    /// <returns>The value to store behind one of the XP tags.</returns>
    public static ExifValue Xp(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] body = new UnicodeEncoding(false, false, true).GetBytes(text + "\0");
        return ExifValue.FromRaw(TiffFieldType.Byte, (uint)body.Length, Endianness.LittleEndian, body);
    }

    // The comment behind its eight-byte character code, decoded with the character set the caller
    // chose or the byte order of the value. The trimming and the "is this text at all" question are
    // the interpretation's, not the carrier's.
    internal static string? DecodeUserComment(ExifValue value, Encoding? unicodeEncoding)
    {
        if (value.FieldType != TiffFieldType.Undefined || value.Count < CommentPrefixLength)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        ReadOnlySpan<byte> code = bytes[..CommentPrefixLength];
        ReadOnlySpan<byte> body = bytes[CommentPrefixLength..];
        string? text;
        if (code.SequenceEqual("ASCII\0\0\0"u8))
            text = DecodeAscii(body);
        else if (code.SequenceEqual("UNICODE\0"u8))
            text = unicodeEncoding is null ? DecodeUtf16(body, value.ByteOrder) : DecodeStrict(body, unicodeEncoding);
        else if (code.SequenceEqual("\0\0\0\0\0\0\0\0"u8))
            text = DecodeUtf8(body);
        else
            return null;
        return text?.TrimEnd(' ', '\0');
    }

    internal static string? DecodeXp(ExifValue value)
    {
        if (value.FieldType is not (TiffFieldType.Byte or TiffFieldType.Undefined) || value.Count == 0)
            return null;
        ReadOnlySpan<byte> bytes = value.RawBytes;
        if ((bytes.Length & 1) != 0)
            bytes = bytes[..^1]; // the reference drops a trailing odd byte the same way
        while (bytes.Length >= 2 && bytes[^1] == 0 && bytes[^2] == 0)
            bytes = bytes[..^2];
        if (bytes.Length == 0)
            return string.Empty;
        try
        {
            return new UnicodeEncoding(false, false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // Seven-bit text up to its first NUL; a byte above 0x7F is not ASCII text at all.
    private static string? DecodeAscii(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length >= 0)
            bytes = bytes[..length];
        foreach (byte b in bytes)
            if (b > 0x7F)
                return null;
        return Encoding.ASCII.GetString(bytes);
    }

    // UTF-8 up to its first NUL, strictly: malformed bytes give no text rather than replacements.
    private static string? DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length >= 0)
            bytes = bytes[..length];
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // The bytes in the caller's character set, strictly: Exiv2 -n never replaces a byte either.
    private static string? DecodeStrict(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        try
        {
            Encoding strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return strict.GetString(bytes).TrimEnd(' ', '\0');
        }
        catch (DecoderFallbackException) { return null; }
        catch (ArgumentException) { return null; }
    }
    // A byte-order mark wins over the byte order of the file; an odd trailing byte is not a character.
    private static string? DecodeUtf16(ReadOnlySpan<byte> bytes, Endianness byteOrder)
    {
        bool bigEndian = byteOrder == Endianness.BigEndian;
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                bigEndian = true;
                bytes = bytes[2..];
            }
            else if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                bigEndian = false;
                bytes = bytes[2..];
            }
        }
        if ((bytes.Length & 1) != 0)
            bytes = bytes[..^1];
        try
        {
            return new UnicodeEncoding(bigEndian, false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
