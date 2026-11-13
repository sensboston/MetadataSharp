using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Bmff;

public sealed partial class BmffMetadataDocument
{
    /// <summary>
    /// Writes the file with <paramref name="profile"/> as the ICC profile of its primary item, or
    /// without one when it is null. The profile is a <c>colr</c> property of the kind <c>prof</c>
    /// in the property container, which is where <see cref="IccProfiles.Read"/> looks for it.
    /// <list type="bullet">
    /// <item>A profile the primary item has (<c>prof</c> or <c>rICC</c>) is replaced where its
    /// property is. Every item that is given that property has the new profile: the tiles of a
    /// grid and a thumbnail commonly share the property of the image they belong to.</item>
    /// <item>A profile the item does not have becomes a new property after the last one. The
    /// primary item alone is given it, before its other properties and as one a reader may ignore.</item>
    /// <item>For null the property becomes a <c>free</c> box of eight bytes, which keeps the number
    /// of every later property, and no item is given it any more.</item>
    /// </list>
    /// Colour information of the kind <c>nclx</c> is another property and is not touched. The
    /// item properties box is rewritten in the bytes it has when it keeps its length or becomes at
    /// least eight bytes shorter; what it no longer needs becomes free space after it. Otherwise
    /// the <c>meta</c> box changes its length where it is if it is the last box of the file, or a
    /// new one is appended and the old one becomes a <c>free</c> box, with the old profile still
    /// in it. No item and no other box moves. A file that already holds exactly this profile, and a file without a profile that is
    /// to have none, is copied as it is. The profile is not interpreted; the array is copied before
    /// the method returns.
    /// </summary>
    /// <param name="destination">A writable, seekable, empty stream.</param>
    /// <param name="profile">The whole profile, or null for none.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIccProfile"/> when the profile is shorter than the 128
    /// bytes of a header, its size field is not the number of bytes given, or the <c>acsp</c>
    /// signature is absent.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when it is longer than
    /// <see cref="MetadataLimits.MaxValueBytes"/> or
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> of the limits, when the output would
    /// exceed their input budget, or when the property container or the entry of the primary item
    /// holds no further property.
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when the primary item has two profiles.
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when a profile is to be added and the
    /// file has no <c>meta</c> box, names no primary item or has no item properties box with a
    /// property container and an association box; for a second association box or one of an
    /// unknown version; and when the <c>meta</c> box has to change its length and the file has a
    /// top-level box of a kind that may not be followed by new data.
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the <c>meta</c> box has to change its
    /// length and cannot (a box or an item that runs to the end of the file, an item addressed past
    /// it), or when the planned output fails its verification.
    /// <see cref="MetadataErrorCode.InvalidBmff"/> for item properties that cannot be accounted
    /// for; otherwise the code the reader reports for the file.
    /// </exception>
    public void SaveIccProfile(Stream destination, byte[]? profile) =>
        SaveIcc(destination, Clone(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfile(Stream, byte[])"/>
    public Task SaveIccProfileAsync(Stream destination, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveIcc(destination, Clone(profile), true, cancellationToken);

    /// <summary>As <see cref="SaveIccProfile(Stream, byte[])"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <param name="destinationPath">Path for publishing ICC profile changes in BMFF format.</param>
    /// <param name="profile">Complete ICC profile bytes, or null to remove the profile.</param>
    public void SaveIccProfileAs(string destinationPath, byte[]? profile) =>
        SaveIccPath(destinationPath, Clone(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfileAs(string, byte[])"/>
    public Task SaveIccProfileAsAsync(string destinationPath, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveIccPath(destinationPath, Clone(profile), true, cancellationToken);

    private static byte[]? Clone(byte[]? bytes) => bytes is null ? null : (byte[])bytes.Clone();

    // Replace the primary item’s ICC property while retaining unrelated BMFF boxes.
    private async Task SaveIcc(Stream destination, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(Stream.Null, destination);
        byte[] output = PrepareIcc(profile, token);
        await ContainerIo.Write(destination, output, asynchronous, token).ConfigureAwait(false);
    }

    // Publish the ICC profile update as a new BMFF file after verification.
    private async Task SaveIccPath(string destinationPath, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        byte[] output = PrepareIcc(profile, token);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token,
            file => ContainerIo.Write(file, output, asynchronous, token).AsTask()).ConfigureAwait(false);
    }

    /// <summary>The bytes of the file with the profile, verified; the source itself when it is that file already.</summary>
    private byte[] PrepareIcc(byte[]? profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        MetadataLimits limits = options.Limits;
        if (profile is not null)
        {
            IccProfiles.Exact(profile);
            if (profile.Length > EmbeddedFile.Budget(limits))
                throw EmbeddedFile.Limit("The ICC profile exceeds its budget.");
        }
        if (refusal is not null) throw new MetadataException(refusal.Code, refusal.Message, refusal.SourceOffset);
        BmffFileMap layout = map!;
        // What the reader makes of the source: the output is held against it, so a source the reader
        // refuses cannot be edited.
        BmffMetadata before = BmffReader.Read(source, limits);
        BmffColourPlan? plan = BmffColourPlanner.Create(source, layout, profile, limits);
        if (plan is null) return source;

        token.ThrowIfCancellationRequested();
        byte[] output = plan.Apply(source);
        BmffColourVerifier.Verify(source, layout, before, output, plan, limits);
        token.ThrowIfCancellationRequested();
        return output;
    }
}
