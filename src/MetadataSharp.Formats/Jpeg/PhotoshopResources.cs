using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>The approved 8BIM framing subset; no public arbitrary-resource editor.</summary>
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
            if (!payload.AsSpan(at, 4).SequenceEqual("8BIM"u8))
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Only 8BIM Photoshop resource framing is supported.");
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
    internal Resource? Iptc => resources.FirstOrDefault(r => r.Id == 0x0404);
    internal void ValidateTargets()
    {
        if (resources.Count(r => r.Id == 0x0404) > 1 || resources.Count(r => r.Id == 0x0425) > 1)
            throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate IPTC or digest resources.");
        Resource? digest = resources.FirstOrDefault(r => r.Id == 0x0425);
        if (digest is not null && (digest.Data.Length != 16 || Iptc is null))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "An invalid-length or orphan IPTC digest cannot be interpreted safely.");
    }
    internal static PhotoshopResources Empty => new([], new());
    internal byte[]? Rewrite(byte[]? iptc)
    {
        ValidateTargets();
        var output = new List<ReadOnlyMemory<byte>>(); long size = 14;
        void Check(long length)
        {
            if (size + length > 65533) throw new MetadataException(MetadataErrorCode.IptcTooLarge, "Photoshop resources exceed one JPEG APP13 payload.");
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
