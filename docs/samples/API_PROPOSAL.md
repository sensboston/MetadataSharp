# Sample-discovered public API proposal

## Safe whole-MakerNote copy for `CopyMetadata`

`ExifEdit` can set or remove named MakerNote values in place, where the destination already has a compatible decoded note. `MetadataOperations.InsertExv` intentionally does not carry a MakerNote. Consequently, a sample using only today's public API cannot faithfully copy a source MakerNote onto a converted destination image without using implementation internals or risking invalid offsets/cipher keys.

Proposed API for discussion (not implemented by this samples PR):

```csharp
MetadataOperations.CopyMetadata(source, destination, MetadataCopyOptions options);
```

The operation should be bounded and transactional, preserve the destination's image data, copy EXIF/IPTC/XMP/ICC and comments as selected, and copy a MakerNote only when the library can prove that the destination container and note layout permit safe relocation or reconstruction. Otherwise it must return an explicit structured note or refuse; it must never copy opaque bytes with stale offsets or cipher dependencies. The existing sync/reconciliation rules should be an explicit option. A byte-array/stream overload may be considered alongside path overloads.

The sample implementation therefore copies editable standard EXIF values and the other supported families, reports a skipped MakerNote, and remains entirely within the existing public API. No API is added in this change.
