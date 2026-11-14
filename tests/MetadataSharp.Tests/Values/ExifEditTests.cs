using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// What an <see cref="ExifEdit"/> takes and refuses before any file is involved: every directory, every
/// field type, tags the vocabulary does not list, the typed overloads, and the tags that are not edited.
/// </summary>
public sealed class ExifEditTests
{
    private static readonly ExifDirectoryKind[] Directories = Enum.GetValues<ExifDirectoryKind>();
    // An entry of an unknown field type is read one byte per element but never written: this is the
    // list of field types a value can be set with.
    private static readonly TiffFieldType[] Types =
        [.. Enum.GetValues<TiffFieldType>().Where(type => type != TiffFieldType.Unknown)];

    /// <summary>A tag number no publication uses in any of the five directories.</summary>
    private const ushort Unlisted = 0xFDE8;

    [Fact]
    public void AnUnlistedTagIsTakenAsGivenInEveryDirectoryWithEveryFieldTypeAndCount()
    {
        foreach (ExifDirectoryKind directory in Directories)
        {
            var key = new ExifTagKey(directory, Unlisted);
            Assert.Null(ExifTags.Find(key));
            Assert.True(ExifEdit.IsEditable(key));
            foreach (TiffFieldType type in Types)
            {
                foreach (uint count in new uint[] { 1, 2, 5, 300 })
                {
                    ExifValue value = Raw(type, count, 0xFF);
                    var edit = new ExifEdit();
                    edit.Set(key, value);
                    Assert.Same(value, edit.Snapshot().Changes[key]);
                    edit.SetUnchecked(key, value);
                    Assert.Same(value, Assert.Single(edit.Snapshot().Changes).Value);
                }
            }
        }
    }

    [Theory]
    [InlineData(0x8769)]
    [InlineData(0x8825)]
    [InlineData(0xA005)]
    [InlineData(0x014A)]
    [InlineData(0x0111)]
    [InlineData(0x0117)]
    [InlineData(0x0144)]
    [InlineData(0x0145)]
    [InlineData(0x0201)]
    [InlineData(0x0202)]
    [InlineData(0x927C)]
    [InlineData(0xC634)]
    public void PointersImageDataAddressesAndTheMakerNoteAreRefusedWithTheirCode(int tag)
    {
        foreach (ExifDirectoryKind directory in Directories)
        {
            var key = new ExifTagKey(directory, (ushort)tag);
            var edit = new ExifEdit();
            Assert.False(ExifEdit.IsEditable(key));
            Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.Set(key, Raw(TiffFieldType.Long, 1, 0))).Code);
            Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.SetUnchecked(key, Raw(TiffFieldType.Undefined, 4, 0))).Code);
            Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.Remove(key)).Code);
            Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.Set(key, 1U)).Code);
            Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.Set(key, "text")).Code);
            Assert.True(edit.IsEmpty);
        }
    }

    [Fact]
    public void EveryOtherTagOfTheFiveDirectoriesIsEditable()
    {
        int refused = 0;
        foreach (ExifDirectoryKind directory in Directories)
            for (int tag = 0; tag <= ushort.MaxValue; tag++)
                if (!ExifEdit.IsEditable(new ExifTagKey(directory, (ushort)tag))) refused++;
        Assert.Equal(12 * Directories.Length, refused);
        Assert.False(ExifEdit.IsEditable(new ExifTagKey((ExifDirectoryKind)99, 0x010F)));
    }

    [Fact]
    public void AListedTagIsHeldToItsDefinitionUnlessTheCallerOptsOut()
    {
        var orientation = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112);
        var date = new ExifTagKey(ExifDirectoryKind.Exif, 0x9003);
        var artist = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B);
        var width = new ExifTagKey(ExifDirectoryKind.Exif, 0xA002);
        var edit = new ExifEdit();

        // The wrong field type, the wrong count, a missing NUL and an eight-bit character.
        ArgumentException type = Assert.Throws<ArgumentException>(() => edit.Set(orientation, 6U));
        Assert.Contains("Orientation (0x0112)", type.Message);
        Assert.Contains("SetUnchecked", type.Message);
        Assert.Throws<ArgumentException>(() => edit.Set(orientation, new ushort[] { 1, 2 }));
        Assert.Throws<ArgumentException>(() => edit.Set(date, "2024:01:02"));
        Assert.Throws<ArgumentException>(() => edit.Set(artist, Raw(TiffFieldType.Ascii, 3, 0x41)));
        Assert.Throws<ArgumentException>(() => edit.Set(artist, ExifValue.FromRaw(TiffFieldType.Ascii, 2, Endianness.LittleEndian, [0xE9, 0])));
        Assert.True(edit.IsEmpty);

        // Either of two listed types is taken.
        edit.Set(width, (ushort)4000);
        edit.Set(width, 4000U);
        edit.Set(orientation, (ushort)6);
        edit.Set(date, "2024:01:02 03:04:05");

        // As given: the same refused values, written on the caller's word.
        edit.SetUnchecked(orientation, Raw(TiffFieldType.Long, 1, 6));
        edit.SetUnchecked(date, Raw(TiffFieldType.Ascii, 11, 0x41));
        edit.SetUnchecked(artist, ExifValue.FromRaw(TiffFieldType.Ascii, 2, Endianness.LittleEndian, [0xE9, 0]));
        Assert.Equal(4, edit.Snapshot().Changes.Count);
        Assert.Equal(TiffFieldType.Long, edit.Snapshot().Changes[orientation]!.FieldType);
    }

    [Fact]
    public void AThumbnailDirectoryTagIsHeldToTheDefinitionsOfThePrimaryDirectory()
    {
        var orientation = new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0112);
        var compression = new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0103);
        var edit = new ExifEdit();
        Assert.Throws<ArgumentException>(() => edit.Set(orientation, 1U));
        edit.Set(orientation, (ushort)1);
        edit.Set(compression, (ushort)6);
        edit.Remove(compression);
        Assert.Null(edit.Snapshot().Changes[compression]);
    }

    [Fact]
    public void TheTypedOverloadsBuildTheFieldTypeTheyName()
    {
        var key = new ExifTagKey(ExifDirectoryKind.Exif, Unlisted);
        var edit = new ExifEdit();

        edit.Set(key, "Ab");
        Check(TiffFieldType.Ascii, 3, [0x41, 0x62, 0]);
        edit.Set(key, "");
        Check(TiffFieldType.Ascii, 1, [0]);
        edit.Set(key, (ushort)0x1234);
        Check(TiffFieldType.Short, 1, [0x34, 0x12]);
        edit.Set(key, 0x12345678U);
        Check(TiffFieldType.Long, 1, [0x78, 0x56, 0x34, 0x12]);
        edit.Set(key, new UnsignedRational(1, 250));
        Check(TiffFieldType.Rational, 1, [1, 0, 0, 0, 250, 0, 0, 0]);
        edit.Set(key, new SignedRational(-1, 3));
        Check(TiffFieldType.SRational, 1, [0xFF, 0xFF, 0xFF, 0xFF, 3, 0, 0, 0]);
        edit.Set(key, new ushort[] { 1, 0x0203 });
        Check(TiffFieldType.Short, 2, [1, 0, 3, 2]);
        edit.Set(key, new uint[] { 1, 0x02030405 });
        Check(TiffFieldType.Long, 2, [1, 0, 0, 0, 5, 4, 3, 2]);
        edit.Set(key, new[] { new UnsignedRational(42, 1), new UnsignedRational(30, 1), new UnsignedRational(1234, 100) });
        Check(TiffFieldType.Rational, 3, [42, 0, 0, 0, 1, 0, 0, 0, 30, 0, 0, 0, 1, 0, 0, 0, 0xD2, 4, 0, 0, 100, 0, 0, 0]);
        edit.Set(key, new[] { new SignedRational(-2, 1), new SignedRational(2, -1) });
        Check(TiffFieldType.SRational, 2, [0xFE, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 2, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]);

        // An integer literal is a SHORT when it fits one and a LONG when it does not.
        edit.Set(key, 4000);
        Assert.Equal(TiffFieldType.Short, edit.Snapshot().Changes[key]!.FieldType);
        edit.Set(key, 70000);
        Assert.Equal(TiffFieldType.Long, edit.Snapshot().Changes[key]!.FieldType);

        void Check(TiffFieldType type, uint count, byte[] bytes)
        {
            ExifValue value = edit.Snapshot().Changes[key]!;
            Assert.Equal(type, value.FieldType);
            Assert.Equal(count, value.Count);
            Assert.Equal(Endianness.LittleEndian, value.ByteOrder);
            byte[] raw = new byte[value.ByteLength];
            value.CopyRawTo(raw);
            Assert.Equal(bytes, raw);
        }
    }

    [Fact]
    public void WhatIsNotAValueIsRefusedAndLeavesTheEditAsItWas()
    {
        var key = new ExifTagKey(ExifDirectoryKind.Gps, Unlisted);
        var edit = new ExifEdit();
        Assert.Throws<ArgumentNullException>(() => edit.Set(key, (ExifValue)null!));
        Assert.Throws<ArgumentNullException>(() => edit.SetUnchecked(key, null!));
        Assert.Throws<ArgumentNullException>(() => edit.Set(key, (string)null!));
        Assert.Throws<ArgumentException>(() => edit.Set(key, "caf\u00E9"));
        Assert.Throws<ArgumentException>(() => edit.Set(key, "a\0b"));
        Assert.Throws<ArgumentException>(() => edit.Set(key, Raw(TiffFieldType.Short, 0, 0)));
        Assert.Throws<ArgumentException>(() => edit.SetUnchecked(key, Raw(TiffFieldType.Undefined, 0, 0)));
        Assert.Throws<ArgumentException>(() => edit.Set(key, ReadOnlySpan<ushort>.Empty));
        Assert.Throws<ArgumentException>(() => edit.Set(key, ReadOnlySpan<uint>.Empty));
        Assert.Throws<ArgumentException>(() => edit.Set(key, ReadOnlySpan<UnsignedRational>.Empty));
        Assert.Throws<ArgumentException>(() => edit.Set(key, ReadOnlySpan<SignedRational>.Empty));
        var nowhere = new ExifTagKey((ExifDirectoryKind)99, 0x010F);
        Assert.Throws<ArgumentException>(() => edit.Set(nowhere, "x"));
        Assert.Throws<ArgumentException>(() => edit.Remove(nowhere));
        Assert.True(edit.IsEmpty);
    }

    [Fact]
    public void TheLastOperationForAKeyWins()
    {
        var key = new ExifTagKey(ExifDirectoryKind.Interop, 0x0001);
        var edit = new ExifEdit();
        edit.Set(key, "R98");
        edit.Remove(key);
        Assert.Null(Assert.Single(edit.Snapshot().Changes).Value);
        edit.Set(key, "THM");
        Assert.Equal(4U, Assert.Single(edit.Snapshot().Changes).Value!.Count);
        Assert.False(edit.IsEmpty);
    }

    private static ExifValue Raw(TiffFieldType type, uint count, byte fill)
    {
        int width = type switch
        {
            TiffFieldType.Short or TiffFieldType.SShort => 2,
            TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
            TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
            _ => 1
        };
        byte[] bytes = new byte[count * width];
        Array.Fill(bytes, fill);
        return ExifValue.FromRaw(type, count, Endianness.LittleEndian, bytes);
    }
}
