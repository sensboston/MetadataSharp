# CopyMetadata

Copies selected EXIF, IPTC, XMP, ICC, and comment metadata through `MetadataOperations.CopyMetadata`. The sample opts into the library's existing EXIF/IPTC/XMP reconciliation rules. MakerNotes are copied only when their layout and any dependent keys can be safely relocated; otherwise the result includes a structured skip note. A dry-run validates and reports the plan without writing.

```sh
dotnet run --project samples/CopyMetadata -- --dry-run converted-source.jpg converted-target.jpg
```

The operation preserves destination image data and is transactional: if it is refused, the destination is left unchanged. TIFF-based reduced-image previews are not treated as EXIF thumbnails.
