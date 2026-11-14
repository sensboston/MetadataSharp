namespace MetadataSharp.Core;

/// <summary>
/// One image directory of a whole TIFF-based file: a directory its primary directory names with tag
/// 0x014A (SubIFDs), as Digital Negative and several camera raw files do. The directory holds the
/// geometry and encoding of a picture, not metadata of the file, so it is read for listing only:
/// <see cref="ExifMetadata.Entries"/> does not contain it and no edit reaches it.
/// </summary>
/// <summary>How a whole TIFF-based file named an image directory.</summary>
public enum ExifSubImageKind
{
    /// <summary>An entry of the primary directory's tag 0x014A (SubIFDs), as DNG and several raws do.</summary>
    SubIfd,
    /// <summary>The next directory of the chain that starts after the primary directory.</summary>
    ImageDirectory
}

public sealed class ExifSubImage
{
    internal ExifSubImage(int index, IReadOnlyList<ExifEntry> entries,
        ExifSubImageKind kind = ExifSubImageKind.SubIfd)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentNullException.ThrowIfNull(entries);
        var copy = new ExifEntry[entries.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            ExifEntry entry = entries[i];
            ArgumentNullException.ThrowIfNull(entry);
            copy[i] = entry;
        }
        Index = index;
        Kind = kind;
        Entries = Array.AsReadOnly(copy);
    }

    /// <summary>
    /// The position of the directory among the directories of its own kind, counted from 1 for a
    /// SubIFD list and from 2 for a chain, whose first directory is the thumbnail one.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// How the file named the directory: by tag 0x014A, or by chaining it after the thumbnail
    /// directory, as Canon CR2 and Pentax PEF do with their picture directories.
    /// </summary>
    public ExifSubImageKind Kind { get; }

    /// <summary>
    /// The entries of the directory, in tag order. A tag is keyed as <see cref="ExifTagKey"/> with
    /// <see cref="ExifDirectoryKind.Ifd0"/> and its own tag number, because an image directory uses the
    /// same tag vocabulary as the primary one; the key locates the tag in the vocabulary, not in the
    /// file, and <see cref="Index"/> names the directory it was read from.
    /// </summary>
    public IReadOnlyList<ExifEntry> Entries { get; }
}
