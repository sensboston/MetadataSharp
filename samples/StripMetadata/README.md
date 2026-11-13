# StripMetadata

Removes metadata families with the public `MetadataOperations.Delete` operation. With no `--keep`, it removes EXIF, IPTC, XMP, comments, and ICC; the `--keep` list selects families to leave in place. `--dry-run` performs the operation and reports without replacing the image.

```sh
dotnet run --project samples/StripMetadata -- --keep icc,orientation,copyright --dry-run ./photos/*.jpg
```

The public API works at metadata-family granularity; `exif` keeps the full EXIF block (including its thumbnail), not just one EXIF key.
