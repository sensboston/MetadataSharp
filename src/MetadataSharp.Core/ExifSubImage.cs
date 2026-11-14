namespace MetadataSharp.Core;

/// <summary>
/// One image directory of a whole TIFF-based file: a directory its primary directory names with tag
/// 0x014A (SubIFDs), as Digital Negative and several camera raw files do. The directory holds the
/// geometry and encoding of a picture, not metadata of the file, so it is read for listing only:
/// <see cref="ExifMetadata.Entries"/> does not contain it and no edit reaches it.
/// </summary>
public sealed class ExifSubImage
{
    internal ExifSubImage(int index, IReadOnlyList<ExifEntry> entries)
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
        Entries = Array.AsReadOnly(copy);
    }

    /// <summary>The position of the directory among the file's sub-image directories, counted from 1.</summary>
    public int Index { get; }

    /// <summary>
    /// The entries of the directory, in tag order. A tag is keyed as <see cref="ExifTagKey"/> with
    /// <see cref="ExifDirectoryKind.Ifd0"/> and its own tag number, because an image directory uses the
    /// same tag vocabulary as the primary one; the key locates the tag in the vocabulary, not in the
    /// file, and <see cref="Index"/> names the directory it was read from.
    /// </summary>
    public IReadOnlyList<ExifEntry> Entries { get; }
}
