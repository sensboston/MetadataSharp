using MetadataSharp.Core;
using MetadataSharp.Formats.Bmff;

namespace MetadataSharp.Formats.Cr3;

/// <summary>The payload of one metadata box of a CR3 file: where it starts in the file and how long it is.</summary>
internal readonly record struct Cr3Block(int Start, int Length)
{
    /// <summary>False for a box the file does not have. A payload never starts at zero, where the file type box is.</summary>
    internal bool Present => Start != 0;
}

/// <summary>
/// Where the metadata of a CR3 file is. The four CMT boxes of the Canon box inside the movie box
/// each hold one directory as a TIFF of its own: CMT1 the primary directory, CMT2 the Exif
/// directory, CMT3 the Canon MakerNote and CMT4 the GPS directory. The XMP packet is the payload of
/// a box of its own at the top level.
/// </summary>
internal readonly record struct Cr3Layout(Cr3Block Ifd0, Cr3Block Exif, Cr3Block MakerNote, Cr3Block Gps, Cr3Block Xmp);

/// <summary>
/// One bounded pass over the boxes of a Canon CR3 file. Three levels are opened and nothing else:
/// the top level, the movie box and the Canon box inside it. Every other box, the tracks and the
/// media data among them, is stepped over by its size.
/// </summary>
internal ref struct Cr3Reader
{
    private const uint FileTypeBox = 0x66747970; // 'ftyp'
    private const uint MovieBox = 0x6D6F6F76;    // 'moov'
    private const uint UuidBox = 0x75756964;     // 'uuid'
    private const uint Brand = 0x63727820;       // 'crx '
    private const uint Cmt1 = 0x434D5431, Cmt2 = 0x434D5432, Cmt3 = 0x434D5433, Cmt4 = 0x434D5434;

    /// <summary>85c0b687-820f-11e0-8111-f4ce462b6a48: the Canon box of the movie box.</summary>
    internal static ReadOnlySpan<byte> CanonBox => new byte[] { 0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48 };

    /// <summary>be7acfcb-97a9-42e8-9c71-999491e3afac: the XMP packet.</summary>
    internal static ReadOnlySpan<byte> XmpBox => new byte[] { 0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC };

    private readonly ReadOnlySpan<byte> data;
    private BmffBoxWalk walk;
    private Cr3Block ifd0;
    private Cr3Block exif;
    private Cr3Block makerNote;
    private Cr3Block gps;
    private Cr3Block xmp;
    private bool hasMovie;
    private bool hasCanon;

    private Cr3Reader(ReadOnlySpan<byte> data, MetadataLimits limits)
    {
        this.data = data;
        walk = new BmffBoxWalk(data, limits);
        ifd0 = default;
        exif = default;
        makerNote = default;
        gps = default;
        xmp = default;
        hasMovie = false;
        hasCanon = false;
    }

    /// <summary>
    /// Finds the metadata boxes of a complete CR3 file. A file whose major brand is not <c>crx </c>
    /// is refused; a CR3 file without a movie box, a Canon box or an XMP box is not malformed, it
    /// has nothing to find.
    /// </summary>
    internal static Cr3Layout Locate(ReadOnlySpan<byte> data, MetadataLimits? limits = null)
    {
        limits ??= BmffReadOptions.DefaultLimits;
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The CR3 file exceeds the input budget.", 0);
        return new Cr3Reader(data, limits).Walk();
    }

    private Cr3Layout Walk()
    {
        int position = 0;
        BmffBox fileType = walk.Next(ref position, data.Length, 1);
        if (fileType.Type != FileTypeBox)
            throw walk.Fail("The file does not start with a file type box.", 0);
        if (fileType.Length < 8 || fileType.Length % 4 != 0)
            throw walk.Fail("The file type box payload is truncated or unaligned.", fileType.Payload);
        if (walk.U32(fileType.Payload) != Brand)
            throw walk.Fail("The major brand is not 'crx ': the file is not a Canon CR3.", fileType.Payload);
        while (position < data.Length)
        {
            BmffBox box = walk.Next(ref position, data.Length, 1);
            if (box.Type == MovieBox)
            {
                Movie(box);
            }
            else if (box.Type == UuidBox && Identified(box, XmpBox))
            {
                if (xmp.Present) throw walk.Fail("The XMP box appears more than once.", box.Start);
                xmp = new Cr3Block(box.Payload + 16, box.Length - 16);
            }
        }
        return new Cr3Layout(ifd0, exif, makerNote, gps, xmp);
    }

    private void Movie(BmffBox movie)
    {
        if (hasMovie) throw walk.Fail("The movie box appears more than once.", movie.Start);
        hasMovie = true;
        int position = movie.Payload;
        while (position < movie.End)
        {
            BmffBox box = walk.Next(ref position, movie.End, 2);
            if (box.Type != UuidBox || !Identified(box, CanonBox)) continue;
            if (hasCanon) throw walk.Fail("The Canon box appears more than once.", box.Start);
            hasCanon = true;
            Canon(box);
        }
    }

    private void Canon(BmffBox canon)
    {
        int position = canon.Payload + 16;
        while (position < canon.End)
        {
            BmffBox box = walk.Next(ref position, canon.End, 3);
            if (box.Type == Cmt1) Claim(ref ifd0, box);
            else if (box.Type == Cmt2) Claim(ref exif, box);
            else if (box.Type == Cmt3) Claim(ref makerNote, box);
            else if (box.Type == Cmt4) Claim(ref gps, box);
        }
    }

    private readonly void Claim(ref Cr3Block block, BmffBox box)
    {
        if (block.Present) throw walk.Fail("A CMT box appears more than once.", box.Start);
        block = new Cr3Block(box.Payload, box.Length);
    }

    /// <summary>Whether a uuid box carries the given identifier. A box too short to carry any is malformed.</summary>
    private readonly bool Identified(BmffBox box, ReadOnlySpan<byte> identifier)
    {
        if (box.Length < 16) throw walk.Fail("A uuid box is shorter than its identifier.", box.Start);
        return data.Slice(box.Payload, 16).SequenceEqual(identifier);
    }
}
