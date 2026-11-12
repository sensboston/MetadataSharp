using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>
/// Photoshop image resource framing; no public arbitrary-resource editor. A resource is framed by
/// 8BIM or by one of the three other signatures Adobe applications write (AgHg, DCSR, PHUT), and a
/// rewrite keeps the framing of every resource as it found it.
/// </summary>
internal sealed class PhotoshopResources
{
    internal sealed record Resource(ushort Id, ReadOnlyMemory<byte> Block, ReadOnlyMemory<byte> NameHeader, ReadOnlyMemory<byte> Data);
    private readonly IReadOnlyList<Resource> resources;
    private readonly IptcLimits limits;
    internal IReadOnlyList<Resource> Resources => resources;
    private PhotoshopResources(List<Resource> resources, IptcLimits limits)
    { this.resources = resources.AsReadOnly(); this.limits = limits with { }; }
    internal static PhotoshopResources Parse(byte[] payload, IptcLimits limits)
    {
        limits.Validate(); if (payload.Length > limits.MaxPacketBytes) throw IptcPacket.Limit();
        if (!payload.AsSpan().StartsWith("Photoshop 3.0\0"u8)) throw Invalid();
        int at = 14; var resources = new List<Resource>();
        while (at < payload.Length)
        {
            if (resources.Count >= limits.MaxResources) throw IptcPacket.Limit();
            int start = at;
            if (payload.Length - at < 7) throw Invalid();
            if (!IsSignature(payload.AsSpan(at, 4)))
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unknown Photoshop resource signature.");
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(at + 4));
            int nameSize = 1 + payload[at + 6]; nameSize += nameSize & 1;
            at += 6;
            if (payload.Length - at < nameSize + 4) throw Invalid();
            at += nameSize;
            ReadOnlyMemory<byte> header = payload.AsMemory(start, at - start);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(at)); at += 4;
            if ((long)size + (size & 1) > payload.Length - at) throw Invalid();
            var data = payload.AsMemory(at, (int)size); at += (int)(size + (size & 1));
            resources.Add(new(id, payload.AsMemory(start, at - start), header, data));
        }
        return new(resources, limits);
    }
    private static MetadataException Invalid() => new(MetadataErrorCode.InvalidPhotoshopResources, "Malformed or truncated Photoshop resource framing.");
    private static bool IsSignature(ReadOnlySpan<byte> signature) =>
        signature.SequenceEqual("8BIM"u8) || signature.SequenceEqual("AgHg"u8) || signature.SequenceEqual("DCSR"u8) || signature.SequenceEqual("PHUT"u8);
    internal Resource? Iptc => resources.FirstOrDefault(r => r.Id == 0x0404);
    /// <summary>
    /// The IPTC resource for reading only. Repeats of resource 0x0404 that hold the same bytes are one
    /// stream; repeats that differ stay ambiguous. The digest is not consulted: it matters to a writer.
    /// </summary>
    internal Resource? IptcForReading()
    {
        Resource? first = null;
        foreach (Resource resource in resources)
        {
            if (resource.Id != 0x0404) continue;
            if (first is null) first = resource;
            else if (!resource.Data.Span.SequenceEqual(first.Data.Span))
                throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "IPTC resources with different contents.");
        }
        return first;
    }
    /// <summary>The error an edit of these resources fails with, or null when they can be edited.</summary>
    internal MetadataException? EditRefusal()
    {
        try { ValidateTargets(); return null; }
        catch (MetadataException failure) { return failure; }
    }
    internal void ValidateTargets()
    {
        if (resources.Count(r => r.Id == 0x0404) > 1 || resources.Count(r => r.Id == 0x0425) > 1)
            throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate IPTC or digest resources.");
        Resource? digest = resources.FirstOrDefault(r => r.Id == 0x0425);
        if (digest is not null && (digest.Data.Length != 16 || Iptc is null))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "An invalid-length or orphan IPTC digest cannot be interpreted safely.");
    }
    internal static PhotoshopResources Empty => new([], new());
    /// <summary>
    /// The resources with the stream replaced, added or (null) removed. Every other resource keeps its
    /// bytes and its place. The digest (0x0425) is removed with the stream; otherwise it is left as it
    /// is - stale on purpose, see <see cref="IptcWriteOptions.UpdateDigest"/> - or recomputed when
    /// <paramref name="updateDigest"/> is set. One is never added.
    /// </summary>
    internal byte[]? Rewrite(byte[]? iptc, bool updateDigest = false, int ceiling = 65533)
    {
        ValidateTargets();
        var output = new List<ReadOnlyMemory<byte>>(); long size = 14;
        void Check(long length)
        {
            if (size + length > ceiling) throw new MetadataException(MetadataErrorCode.IptcTooLarge, "Photoshop resources exceed what their container holds.");
            if (size + length > limits.MaxPacketBytes || output.Count >= limits.MaxResources) throw IptcPacket.Limit();
        }
        void Add(ReadOnlyMemory<byte> data)
        {
            Check(data.Length); size += data.Length; output.Add(data);
        }
        foreach (Resource resource in resources)
        {
            if (resource.Id is 0x0404 or 0x0425)
            {
                if (iptc is null) continue;
                if (resource.Id == 0x0425 && !updateDigest) { Add(resource.Block); continue; }
                int dataLength = resource.Id == 0x0404 ? iptc.Length : 16;
                Check(resource.NameHeader.Length + 4L + dataLength + (dataLength & 1));
                byte[] data = resource.Id == 0x0404 ? iptc : MD5.HashData(iptc);
                Add(Frame(resource.Id, data, resource.NameHeader));
            }
            else Add(resource.Block);
        }
        if (iptc is not null && Iptc is null)
        {
            Check(12L + iptc.Length + (iptc.Length & 1));
            Add(Frame(0x0404, iptc, default));
        }
        if (output.Count == 0) return null;
        byte[] result = new byte[(int)size]; "Photoshop 3.0\0"u8.CopyTo(result); int at = 14;
        foreach (var block in output) { block.Span.CopyTo(result.AsSpan(at)); at += block.Length; }
        return result;
    }
    private static byte[] Frame(ushort id, byte[] data, ReadOnlyMemory<byte> header)
    {
        int prefix = header.IsEmpty ? 8 : header.Length;
        byte[] frame = new byte[prefix + 4 + data.Length + (data.Length & 1)];
        if (header.IsEmpty) { "8BIM"u8.CopyTo(frame); BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), id); }
        else header.Span.CopyTo(frame);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(prefix), (uint)data.Length);
        data.CopyTo(frame, prefix + 4); return frame;
    }
}
