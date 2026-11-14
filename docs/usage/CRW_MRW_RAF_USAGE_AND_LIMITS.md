# Canon CRW, Minolta MRW and Fujifilm RAF: usage and limits

These are the three raw formats of the Exiv2 0.28.9 registry that are not a TIFF file. All three are
read; CRW is also written, MRW and RAF are not, as in the reference.

| Format | Signature | Read | Write |
| --- | --- | --- | --- |
| CRW | `II` or `MM`, heap offset, `HEAPCCDR` | `CrwMetadata.Read`: EXIF tags, comment, preview and thumbnail position | `CrwMetadataDocument`: EXIF tags, comment |
| MRW | `\0MRM` | `MrwMetadata.Read`: EXIF, IPTC, XMP of the TTW block | none |
| RAF | `FUJIFILM` | `RafMetadata.Read`: EXIF, IPTC, XMP, comments of the embedded JPEG file; IFD0 tags of the TIFF structure | none |

`ImageMetadata.Read` tells all three by their signature (`MetadataFormat.Crw`, `Mrw`, `Raf`) and
reports a refusal of one kind in `Errors` instead of throwing it.

## Reading

```csharp
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Formats.Crw;
using MetadataSharp.Formats.Raf;

CrwMetadata crw = CrwMetadata.Read(File.ReadAllBytes("CRW_0001.CRW"));
string? comment = crw.Comment;
ExifMetadata? exif = crw.Exif;                    // not editable in place; see CrwMetadataDocument
MakerNote? canon = exif?.MakerNote;               // Exif.Canon.*, Exif.CanonCs.*, Exif.CanonSi.*
long? preview = crw.PreviewOffset;                // a JPEG stream of crw.PreviewLength bytes

RafMetadata raf = RafMetadata.Read(File.ReadAllBytes("DSCF0001.RAF"));
long jpegAt = raf.JpegOffset, jpegLength = raf.JpegLength;
```

- **CRW** has no EXIF block. The records of its heap that the reference maps to tags are given as
  those tags: `Exif.Image.Make` / `Model`, `Orientation`, `Exif.Photo.PixelXDimension` /
  `PixelYDimension`, `DateTimeOriginal`, `ColorSpace`, `SubjectDistance`, `FNumber` and
  `ExposureTime` (computed from the Canon shot information as the reference computes them), the
  thumbnail tags, and the Canon records as a Canon MakerNote. A value that lies in its directory
  entry is the eight bytes of the entry, as the reference has it.
- The capture time of a CRW file is a count of seconds without a time zone. It is given **in UTC**;
  the reference converts it with the local time zone of the machine it runs on.
- **MRW**: the TTW block is read as the TIFF file it is; the XMP packet is the one of tag 0x02BC of
  its primary directory.
- **RAF**: the embedded JPEG file is read as a JPEG file. Bytes that are no JPEG file give nothing
  and are no error. Of the TIFF structure, the tags of the primary directory that the JPEG file does
  not have are added when both are of one byte order; the Fujifilm directory (tag 0xF000) is not read.

## The pictures a raw container embeds

A raw file embeds pictures, and those pictures are files with metadata of their own. The container's
own metadata is what `ImageMetadata.Read` returns; the embedded picture's is a separate read-only
snapshot, `EmbeddedImageMetadata.Read` (`MetadataSharp.Formats`), which names the preview it came from
(`Source`) and holds that preview's EXIF, IPTC, XMP and comments. There is no writer: a caller that
wants to change the embedded picture's metadata extracts the picture and edits it as a file.

This is generic — any container whose preview is a whole image with readable metadata answers it — but
it matters for **Panasonic RW2**, where the two layers are easy to confuse:

- An RW2's first directory is the `PanasonicRaw` directory. Its tags describe the raw data — sensor
  geometry, white-balance levels, strip layout, `RawDataOffset`, `PreviewImage` — and the file names no
  directory after it. Its `ExifTag` (`0x8769`) leads to the EXIF directory and `0x8825` to GPS, and
  that is all the container itself carries.
- Exiv2 does not keep the layers apart: it reads the RW2 and then opens the embedded JPEG preview,
  merges that picture's metadata into the file's, and prints `Exif.Image.*`, `Exif.Thumbnail.*` and
  `Exif.Iop.*` rows that physically belong to the preview. In `Exiv2 -pt` parity those rows are required
  and the print merges them the way the reference does (a container key wins over an embedded one, and
  the `PanasonicRaw` directory never counts as holding one), but that is a rule of the print, not of the
  library: `ImageMetadata.Read` of an RW2 reports the container's `PanasonicRaw`, `Exif`, GPS and
  MakerNote entries and nothing from the preview.

The preview's own `ExifTag` value is an offset inside the preview file, so it does not agree with the
container's — a difference that only exists while the two layers are merged.

## Writing a CRW file

```csharp
using MetadataSharp.Core;
using MetadataSharp.Formats.Crw;

var edit = new ExifEdit();
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), (ushort)6);            // Orientation
edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x9003), "2024:05:17 10:30:00"); // DateTimeOriginal, UTC
edit.SetTextByKey("Exif.Canon.OwnerName", "A. Photographer");

using (CrwMetadataDocument document = CrwMetadataDocument.Open("CRW_0001.CRW"))
{
    document.SaveExifAs("edited.CRW", edit);
    document.SaveCommentAs("commented.CRW", "Harbour at dawn");   // null clears the comment
}
```

| What | Record | Rule |
| --- | --- | --- |
| `Exif.Image.Make`, `Model` | 0x080A | one record: both are set, or both removed |
| `Exif.Image.Orientation` | 0x1810, rotation | 1, 3, 6 or 8; set, not removed |
| `Exif.Photo.PixelXDimension`, `PixelYDimension` | 0x1810 | one LONG; set, not removed |
| `Exif.Photo.DateTimeOriginal` | 0x180E | `YYYY:MM:DD HH:MM:SS`, 1970 to 2106; the rest of the record is kept |
| `Exif.Photo.ColorSpace` | 0x10B4 | SHORT |
| `Exif.Photo.SubjectDistance` | 0x1807 | stored as a LONG, so a LONG is what is set |
| Canon MakerNote tags the file has | their records | by key (`SetByKey`, `SetTextByKey`, `RemoveByKey`), under the rules of every Canon MakerNote |
| Thumbnail | 0x2008 | `SetThumbnail`, `RemoveThumbnail` |
| Comment | 0x0805 | UTF-8; the record keeps its length when the comment is shorter; cleared, it stays, filled with zero bytes |

- `Exif.Photo.FNumber` and `ExposureTime` follow `Exif.CanonSi.ApertureValue` and
  `ShutterSpeedValue`; they and every tag not in the table are refused with `TagNotEditable`.
  `RemoveAllExif` is refused with `UnsupportedFeature`.
- The heap is written again whole, in the layout the reference writes: in each directory the values
  in the order of the entries, each padded to an even length, then the entries. A record the edit
  does not name keeps its bytes and its place among the entries; where it lies in the file may
  change. The bytes before the heap are kept.
- The output is read again before it is returned. Its records have to be the planned ones, the tags
  that were set have to read as set, every other tag, the comment and the preview as before; if not,
  nothing is written (`UnsafeRelocation`).
- A record that is not in the directory the reference writes it to is not changed
  (`UnsupportedFeature`). The document has synchronous members only; it reads the file whole.

## Measured against Exiv2 0.28.9

CI job "Exiv2 0.28.9 / differential parity" and "MakerNote edit parity" (push runs; run 43327984837):

| Measurement | Files | Keys expected | Found | Equal |
| --- | ---: | ---: | ---: | ---: |
| Stored expectations of the corpus (4 CRW, 2 RAF) | 6 | 116 | 116 | 114 |
| The pinned tool, run in the job, on the 13 CRW files of the corpus and on 12 MRW and 12 RAF files authored from corpus TIFF and JPEG files | 37 (24 read by both) | 1,509 | 1,504 | 1,498 |

- The two stored values that differ are `Exif.Photo.DateTimeOriginal` of two CRW files: the
  expectation was made eight hours east of UTC. Run in the job, in UTC, the tool prints our value.
- 13 of the 37 files are refused here and read by the tool, at least in part (71 keys): 5 CRW and 8
  MRW files, all made to show a defect of a reader. The 8 TIFF files inside the MRW files are refused
  as TIFF files too.
- Not found: `Exif.Image.XMLPacket` (3; the dump prints the packet as XMP, as for a TIFF file) and
  the `DocumentName` of the authored TIFF structure in 2 RAF files whose JPEG file is of the other
  byte order. Not equal: the model of 3 defect files, which is not ASCII, and 3 multi-line values of
  `Reagan.tiff` that the comparison cuts at the line end.
- Written CRW files (`tools/reference/crw_parity.py`): each of up to ten changes is made in a copy of
  each file and printed by the tool (`exiv2 -u -PEkv`, `exiv2 -pc`). 39 edits read back as set with
  every other key as before (15 EXIF tags, 16 Canon tags, 8 comments); 6 were refused here
  (5 `UnsupportedFeature`, such as an image record shorter than its fields; 1 `UnsafeRelocation`); 5 files
  are not read. An edit the tool does not read back as set fails the job.
