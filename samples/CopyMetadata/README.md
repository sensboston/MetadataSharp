# CopyMetadata

Copies readable EXIF tags, IPTC datasets, XMP, and the ICC profile from one image to another. It applies `MetadataSync.Reconcile` after the copy so mapped EXIF/IPTC/XMP values and the IPTC digest follow the library's sync policy. A dry-run validates and reports the planned operation without writing.

```sh
dotnet run --project samples/CopyMetadata -- --dry-run converted-source.jpg converted-target.jpg
```

**Known public-API limitation:** a MakerNote is not copied. The current public edit API permits safe in-place edits of named MakerNote fields but not moving/copying a complete note between files. The sample prints a notice when it encounters one; `docs/samples/API_PROPOSAL.md` proposes the missing public operation instead of using internals or silently relocating offsets. TIFF-based reduced-image previews are also not treated as EXIF thumbnails.
