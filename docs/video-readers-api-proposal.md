# Video-container readers: API proposal (stage 1)

This is the proposed public surface for read-only video-container support. It is a proposal, not a claim that the implementation exists.

## Public surface

- Add `VideoMetadata? Video` to `ImageMetadata`. It is non-null only for a container accepted by one of the four pinned video readers.
- Add `VideoMetadata.Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null)` as the direct reader entry point, following the existing format-reader convention.
- `VideoMetadata` exposes the detected `MetadataFormat` and an immutable, ordered `IReadOnlyList<VideoMetadataEntry> Entries`.
- Each `VideoMetadataEntry` exposes the exact key printed by Exiv2 (for example `Xmp.video.Width` or `Xmp.audio.SampleRate`) and the value in a form that retains its reference-visible scalar/sequence shape and display value. It does not claim that these values came from an embedded RDF/XMP packet: the pinned readers populate their `XmpData` from container fields.
- The dump tool will emit one video record per entry from `ImageMetadata.Read`; the parity reader will compare those keys and the values that Exiv2 0.28.9 prints. The concrete dump record grammar and public value type will be fixed before implementation, against the pinned reference output, rather than introducing an editable XMP packet abstraction.
- Add a `Video` metadata family and read-only capability rows for the four container categories. No edit, write, or removal API is proposed.

`ImageMetadata.Read` remains the unified entry point. A container-reader refusal is reported through `ImageMetadata.Errors` under the video family, independently of the existing EXIF/IPTC/XMP packet properties. The proposal leaves the existing `ImageMetadata.Xmp` meaning unchanged.

## Pinned-reference scope and detection facts

Source of truth: Exiv2 tag `v0.28.9`, commit [`b0859fc61fea5dedfc898aece6b15893ddf4314e`](https://github.com/Exiv2/exiv2/tree/b0859fc61fea5dedfc898aece6b15893ddf4314e). The configured reference build has video support enabled. In `src/image.cpp`, `EXV_ENABLE_VIDEO` adds exactly these registry rows: `qtime`/`isQTimeType`, `asf`/`isAsfType`, `riff`/`isRiffType`, and `mkv`/`isMkvType`. Each row declares EXIF read, IPTC none, XMP read, and comment none. This work follows those readers, not filename suffixes or a generic media taxonomy.

| Factory type | Reader | Predicate in the pinned source | Reader MIME value |
|---|---|---|---|
| `asf` | `AsfVideo` | `isAsfType` reads 16 bytes and compares them with `isASFType`'s ASF Header GUID. | `video/asf` |
| `mkv` | `MatroskaVideo` | `isMkvType` reads 4 bytes and accepts `1A 45 DF A3`. This is the same predicate for Matroska and WebM. | `video/matroska` |
| `qtime` | `QuickTimeVideo` | `isQTimeType` reads 12 bytes; bytes 4–7 must be one of `PICT`, `free`, `ftyp`, `junk`, `mdat`, `moov`, `pict`, `pnot`, `skip`, `uuid`, `wide`, and bytes 8–11 must be in that reader's `qTimeFileType` vocabulary. It deliberately does not accept every BMFF `ftyp` file. | `video/quicktime` |
| `riff` | `RiffVideo` | `isRiffType` reads 4 bytes and accepts `RIFF`; the predicate does not distinguish the form code. The reader then records the form as `Xmp.video.FileType`. | `video/riff` |

The factory uses signatures, not `.asf`, `.wmv`, `.mkv`, `.webm`, `.mp4`, `.mov`, `.3gp`, `.m4v`, `.m4a`, `.avi`, or `.wav` suffixes. QuickTime-family brands such as `3gp*`, `M4A ` and `M4V ` are accepted only where present in the reader's `qTimeFileType` vocabulary. RIFF selects the reader for AVI and WAV alike; the actual form is read from the file. The new `MetadataFormat` members will name these four factory readers; the old BMFF image reader remains distinct.

## What the four readers put in `XmpData`

These are the literal keys assigned by the corresponding pinned reader and its key tables, not a translated metadata schema. Each reader also sets its own `Xmp.video.FileSize` and `Xmp.video.MimeType`. The pin is the source for key spelling and case (including `Xmp.video.duration` and `Xmp.video.VideoScanTpye`). Keys are conditional on the fields found in the input.

- **ASF** (`src/asfvideo.cpp`): `Xmp.video.AspectRatio`, `Xmp.video.StartTimecode`, `Xmp.video.EndTimecode`, `Xmp.video.FrameRate`, `Xmp.video.Width`, `Xmp.video.Height`, `Xmp.video.TimeOffset`, `Xmp.audio.TimeOffset`, `Xmp.video.CodecName`, `Xmp.audio.CodecName`, `Xmp.video.CodecDescription`, `Xmp.audio.CodecDescription`, `Xmp.video.CodecInfo`, `Xmp.audio.CodecInfo`, `Xmp.video.ExtendedContentDescription`, `Xmp.video.Title`, `Xmp.video.Author`, `Xmp.video.Copyright`, `Xmp.video.Description`, `Xmp.video.Rating`, `Xmp.video.FileID`, `Xmp.video.FileLength`, `Xmp.video.CreationDate`, `Xmp.video.DataPackets`, `Xmp.video.duration`, `Xmp.video.SendDuration`, `Xmp.video.Preroll`, and `Xmp.video.MaxBitRate`.
- **Matroska/WebM** (`src/matroskavideo.cpp`): common keys `Xmp.video.FileSize`, `Xmp.video.MimeType`, and `Xmp.video.AspectRatio`; the tag table writes container, track, audio, video, subtitle, attachment, chapter, and tag entries under literal `Xmp.video.*` and `Xmp.audio.*` labels. The implementation's key inventory will be taken from that table and verified with the pinned tool; it will not invent aliases for tag-table spellings.
- **QuickTime-family** (`src/quicktimevideo.cpp`): common keys `Xmp.video.FileSize`, `Xmp.video.MimeType`, and `Xmp.video.AspectRatio`; the reader maps movie, track, media, handler, codec, audio, video, GPS, camera, and metadata atoms to literal `Xmp.video.*` and `Xmp.audio.*` keys. `CompatibleBrands` is a sequence. Atom-derived labels and the brand/codec vocabulary remain source-backed data, not copied explanatory prose.
- **RIFF** (`src/riffvideo.cpp`): common keys `Xmp.video.FileSize`, `Xmp.video.MimeType`, `Xmp.video.Container`, and `Xmp.video.FileType`; AVI paths additionally populate main-header, stream-header, bitmap/audio format, timing, aspect-ratio and INFO-list fields. The source's four-character INFO mapping and audio-compression labels determine the literal keys and values. A WAV input can therefore produce audio keys without video dimensions.

For the last three bullets, stage 2 will include the complete unique key inventory mechanically extracted from the pinned reader tables, and stage 3 will replace that source inventory with corpus-plus-authored-fixture parity measurements: keys present/equal for each reader, with value comparisons including derived duration, frame rate, aspect ratio, codec labels and FourCC translations. No copied media will be checked in.

## Parsing and acceptance constraints

The readers will be implemented as independent bounded parsers under `MetadataLimits`, then called by `ImageMetadata.Read`. Container structure traversal must be iterative or explicitly depth-bounded; child extents are checked against the enclosing data before seeking; a declared size never drives an allocation until bounded by both input and retained-metadata budgets. Segment/box/chunk counts and recursion are charged against limits. Unknown-size EBML elements, 64-bit BMFF sizes, truncated headers, integer overflow, zero-progress/looping structures, excessive nesting, and huge size declarations need adversarial fixtures and explicit tests. All malformed-input paths must return documented `MetadataException` refusals rather than leak implementation exceptions.

Authored, tiny fixtures will be generated by a Python script in `tools/reference` and pinned by manifests. Tests will mutate sizes, nesting, IDs, and truncation points independently for ASF, EBML, QuickTime/BMFF, and RIFF. Corpus samples are fetched by CI as in the existing parity workflow and are never vendored. Read-only capability rows will have no insert, replace, or remove rows.

## Stages

1. **API proposal ready (this stage):** publish this proposal in a draft PR; no parser implementation yet.
2. **Pinned scope and fixtures:** add generator, malformed tests, and the exact source-derived key/detection inventory; fetch and measure suitable corpus files.
3. **Readers and parity:** implement the parsers, `ImageMetadata` integration, and dump output; run differential parity on corpus files plus authored fixtures; compare key sets and reference-visible values and report each container before/after.
4. **Hardening and green checks:** close all hostile-input gaps, document measured source divergences, run platform/audit/parity checks, read PR comments before each stage, and mark the PR ready when green.

### Literal keys present in the pinned reader source

The following unique labels are present as literals in the respective reader's assignments and tag tables at the pinned commit. The list records source spellings; it does not assert that any particular file contains every field. ASF's `CodecName`, `CodecDescription`, and `CodecInfo` are selected with a reader-produced `Xmp.video` or `Xmp.audio` prefix and are stated separately.

**ASF** (24 unique literal keys):

```text
Xmp.audio.TimeOffset
Xmp.video.AspectRatio
Xmp.video.Author
Xmp.video.Copyright
Xmp.video.CreationDate
Xmp.video.DataPackets
Xmp.video.Description
Xmp.video.EndTimecode
Xmp.video.ExtendedContentDescription
Xmp.video.FileID
Xmp.video.FileLength
Xmp.video.FileSize
Xmp.video.FrameRate
Xmp.video.Height
Xmp.video.MaxBitRate
Xmp.video.MimeType
Xmp.video.Preroll
Xmp.video.Rating
Xmp.video.SendDuration
Xmp.video.StartTimecode
Xmp.video.TimeOffset
Xmp.video.Title
Xmp.video.Width
Xmp.video.duration
```

**Matroska/WebM** (76 unique literal keys):

```text
Xmp.audio.BitsPerSample
Xmp.audio.ChannelType
Xmp.audio.CodecDecodeAll
Xmp.audio.CodecDownloadUrl
Xmp.audio.CodecInfo
Xmp.audio.CodecSettings
Xmp.audio.Compressor
Xmp.audio.DefaultDuration
Xmp.audio.DefaultOn
Xmp.audio.Enabled
Xmp.audio.OutputSampleRate
Xmp.audio.SampleRate
Xmp.audio.TrackForced
Xmp.audio.TrackLacing
Xmp.video.AspectRatio
Xmp.video.AspectRatioType
Xmp.video.AttachFileData
Xmp.video.AttachFileDesc
Xmp.video.AttachFileMIME
Xmp.video.AttachFileName
Xmp.video.AttachFileUID
Xmp.video.Codec
Xmp.video.CodecDecodeAll
Xmp.video.CodecDownloadUrl
Xmp.video.CodecInfo
Xmp.video.CodecSettings
Xmp.video.ContentCompressAlgo
Xmp.video.ContentEncodingType
Xmp.video.ContentEncryptAlgo
Xmp.video.ContentSignAlgo
Xmp.video.ContentSignHashAlgo
Xmp.video.CropBottom
Xmp.video.CropLeft
Xmp.video.CropRight
Xmp.video.CropTop
Xmp.video.DateUTC
Xmp.video.DefaultOn
Xmp.video.DisplayUnit
Xmp.video.DocType
Xmp.video.DocTypeReadVersion
Xmp.video.DocTypeVersion
Xmp.video.Duration
Xmp.video.EBMLReadVersion
Xmp.video.EBMLVersion
Xmp.video.Enabled
Xmp.video.FileSize
Xmp.video.FrameRate
Xmp.video.Height
Xmp.video.MimeType
Xmp.video.MuxingApp
Xmp.video.OpColor
Xmp.video.PhysicalEquivalent
Xmp.video.SubTCodec
Xmp.video.SubTCodecDecodeAll
Xmp.video.SubTCodecDownloadUrl
Xmp.video.SubTCodecInfo
Xmp.video.SubTCodecSettings
Xmp.video.SubTDefaultOn
Xmp.video.SubTEnabled
Xmp.video.SubTTrackForced
Xmp.video.SubTTrackLacing
Xmp.video.TagDefault
Xmp.video.TagLanguage
Xmp.video.TagName
Xmp.video.TagString
Xmp.video.TargetType
Xmp.video.TimecodeScale
Xmp.video.Title
Xmp.video.TotalStream
Xmp.video.TrackForced
Xmp.video.TrackLacing
Xmp.video.TrackName
Xmp.video.TranslateCodec
Xmp.video.VideoScanTpye
Xmp.video.Width
Xmp.video.WritingApp
```

**QuickTime-family** (196 unique literal keys):

```text
Xmp.audio.Balance
Xmp.audio.BitsPerSample
Xmp.audio.ChannelType
Xmp.audio.Compressor
Xmp.audio.HandlerClass
Xmp.audio.HandlerType
Xmp.audio.HandlerVendorID
Xmp.audio.MediaCreateDate
Xmp.audio.MediaDuration
Xmp.audio.MediaHeaderVersion
Xmp.audio.MediaLangCode
Xmp.audio.MediaModifyDate
Xmp.audio.MediaTimeScale
Xmp.audio.SampleRate
Xmp.audio.TrackCreateDate
Xmp.audio.TrackDuration
Xmp.audio.TrackHeaderVersion
Xmp.audio.TrackID
Xmp.audio.TrackLayer
Xmp.audio.TrackModifyDate
Xmp.audio.URL
Xmp.audio.URN
Xmp.audio.VendorID
Xmp.audio.channelType
Xmp.audio.sampleRate
Xmp.video.Album
Xmp.video.Arranger
Xmp.video.ArrangerKeywords
Xmp.video.Artist
Xmp.video.AspectRatio
Xmp.video.BitDepth
Xmp.video.Brightness
Xmp.video.CameraByteOrder
Xmp.video.CleanApertureHeight
Xmp.video.CleanApertureWidth
Xmp.video.Codec
Xmp.video.ColorSpace
Xmp.video.Comment
Xmp.video.CompatibleBrands
Xmp.video.Composer
Xmp.video.ComposerKeywords
Xmp.video.Compressor
Xmp.video.CompressorVersion
Xmp.video.Contrast
Xmp.video.Copyright
Xmp.video.CreateDate
Xmp.video.CreationDate
Xmp.video.CurrentTime
Xmp.video.DateDisplayFormat
Xmp.video.DateTimeOriginal
Xmp.video.DateUTC
Xmp.video.DayLightSavings
Xmp.video.Director
Xmp.video.Duration
Xmp.video.Edit1
Xmp.video.Edit2
Xmp.video.Edit3
Xmp.video.Edit4
Xmp.video.Edit5
Xmp.video.Edit6
Xmp.video.Edit7
Xmp.video.Edit8
Xmp.video.Edit9
Xmp.video.EncodedPixelsHeight
Xmp.video.EncodedPixelsWidth
Xmp.video.Encoder
Xmp.video.ExposureCompensation
Xmp.video.ExposureProgram
Xmp.video.ExposureTime
Xmp.video.FNumber
Xmp.video.FileSize
Xmp.video.FilterEffect
Xmp.video.FirmwareVersion
Xmp.video.FocalLength
Xmp.video.Format
Xmp.video.FrameCount
Xmp.video.FrameHeight
Xmp.video.FrameRate
Xmp.video.FrameWidth
Xmp.video.GPSAltitude
Xmp.video.GPSAltitudeRef
Xmp.video.GPSCoordinates
Xmp.video.GPSDateStamp
Xmp.video.GPSImgDirection
Xmp.video.GPSImgDirectionRef
Xmp.video.GPSLatitude
Xmp.video.GPSLatitudeRef
Xmp.video.GPSLongitude
Xmp.video.GPSLongitudeRef
Xmp.video.GPSMapDatum
Xmp.video.GPSSatellites
Xmp.video.GPSTimeStamp
Xmp.video.GPSVersionID
Xmp.video.Genre
Xmp.video.GraphicsMode
Xmp.video.Grouping
Xmp.video.HandlerClass
Xmp.video.HandlerType
Xmp.video.HandlerVendorID
Xmp.video.Height
Xmp.video.HueAdjustment
Xmp.video.ISO
Xmp.video.ISRCCode
Xmp.video.Information
Xmp.video.LensModel
Xmp.video.LensType
Xmp.video.LocationInfo
Xmp.video.Lyrics
Xmp.video.MajorBrand
Xmp.video.Make
Xmp.video.MakerNoteType
Xmp.video.MakerNoteVersion
Xmp.video.MakerURL
Xmp.video.MediaCreateDate
Xmp.video.MediaDuration
Xmp.video.MediaHeaderVersion
Xmp.video.MediaLangCode
Xmp.video.MediaModifyDate
Xmp.video.MediaTimeScale
Xmp.video.MeteringMode
Xmp.video.MimeType
Xmp.video.MinorVersion
Xmp.video.Model
Xmp.video.ModificationDate
Xmp.video.MovieHeaderVersion
Xmp.video.Name
Xmp.video.NextTrackID
Xmp.video.OpColor
Xmp.video.PerformerKeywords
Xmp.video.PerformerURL
Xmp.video.Performers
Xmp.video.PictureControlAdjust
Xmp.video.PictureControlBase
Xmp.video.PictureControlData
Xmp.video.PictureControlName
Xmp.video.PictureControlQuickAdjust
Xmp.video.PictureControlVersion
Xmp.video.PlayMode
Xmp.video.PlaySelection
Xmp.video.PosterTime
Xmp.video.PreferredRate
Xmp.video.PreferredVolume
Xmp.video.PreviewAtomType
Xmp.video.PreviewDate
Xmp.video.PreviewDuration
Xmp.video.PreviewTime
Xmp.video.PreviewVersion
Xmp.video.Producer
Xmp.video.ProducerKeywords
Xmp.video.ProductionApertureHeight
Xmp.video.ProductionApertureWidth
Xmp.video.RecordLabelName
Xmp.video.RecordLabelURL
Xmp.video.RecordingCopyright
Xmp.video.Requirements
Xmp.video.Saturation
Xmp.video.SelectionDuration
Xmp.video.SelectionTime
Xmp.video.Sharpness
Xmp.video.Software
Xmp.video.SoftwareVersion
Xmp.video.SongWriter
Xmp.video.SongWriterKeywords
Xmp.video.SourceCredits
Xmp.video.SourceImageHeight
Xmp.video.SourceImageWidth
Xmp.video.StreamName
Xmp.video.Subtitle
Xmp.video.SubtitleKeywords
Xmp.video.TimeScale
Xmp.video.TimeZone
Xmp.video.Title
Xmp.video.ToningEffect
Xmp.video.ToningSaturation
Xmp.video.Track
Xmp.video.TrackCreateDate
Xmp.video.TrackDuration
Xmp.video.TrackHeaderVersion
Xmp.video.TrackID
Xmp.video.TrackLayer
Xmp.video.TrackModifyDate
Xmp.video.TrackVolume
Xmp.video.URL
Xmp.video.URN
Xmp.video.UnknownInfo
Xmp.video.UnknownInfo2
Xmp.video.VariProgram
Xmp.video.Vendor
Xmp.video.VendorID
Xmp.video.WhiteBalance
Xmp.video.WhiteBalanceFineTune
Xmp.video.Width
Xmp.video.WindowLocation
Xmp.video.WorldTime
Xmp.video.XResolution
Xmp.video.YResolution
```

**RIFF** (110 unique literal keys):

```text
Xmp.audio.BitsPerSample
Xmp.audio.ChannelType
Xmp.audio.Compressor
Xmp.audio.DefaultStream
Xmp.audio.FrameCount
Xmp.audio.SampleRate
Xmp.audio.SampleType
Xmp.audio.SchemeTitle
Xmp.video.ArchivalLocation
Xmp.video.Artist
Xmp.video.AspectRatio
Xmp.video.BaseURL
Xmp.video.Cinematographer
Xmp.video.Codec
Xmp.video.Comment
Xmp.video.Commissioned
Xmp.video.Compressor
Xmp.video.Container
Xmp.video.Copyright
Xmp.video.CostumeDesigner
Xmp.video.Country
Xmp.video.Cropped
Xmp.video.DateTimeDigitized
Xmp.video.DateTimeOriginal
Xmp.video.Dimensions
Xmp.video.Director
Xmp.video.DistributedBy
Xmp.video.DotsPerInch
Xmp.video.Duration
Xmp.video.Edit1
Xmp.video.Edit2
Xmp.video.Edit3
Xmp.video.Edit4
Xmp.video.Edit5
Xmp.video.Edit6
Xmp.video.Edit7
Xmp.video.Edit8
Xmp.video.Edit9
Xmp.video.EditedBy
Xmp.video.EncodedBy
Xmp.video.EndTimecode
Xmp.video.Engineer
Xmp.video.FileDataRate
Xmp.video.FileSize
Xmp.video.FileType
Xmp.video.FrameCount
Xmp.video.FrameRate
Xmp.video.Genre
Xmp.video.Height
Xmp.video.ImageLength
Xmp.video.InfoBannerImage
Xmp.video.InfoBannerURL
Xmp.video.InfoText
Xmp.video.InfoURL
Xmp.video.Language
Xmp.video.Length
Xmp.video.Lightness
Xmp.video.LocationInfo
Xmp.video.LogoIconURL
Xmp.video.LogoURL
Xmp.video.MaxDataRate
Xmp.video.Medium
Xmp.video.MicroSecPerFrame
Xmp.video.MimeType
Xmp.video.MusicBy
Xmp.video.NumIfImpColours
Xmp.video.NumOfColors
Xmp.video.NumOfColours
Xmp.video.NumOfParts
Xmp.video.Organization
Xmp.video.Part
Xmp.video.PerformerKeywords
Xmp.video.PixelDepth
Xmp.video.PixelPerMeterX
Xmp.video.PixelPerMeterY
Xmp.video.Planes
Xmp.video.ProducedBy
Xmp.video.Product
Xmp.video.ProductionDesigner
Xmp.video.ProductionStudio
Xmp.video.Rate
Xmp.video.Rated
Xmp.video.Rating
Xmp.video.RippedBy
Xmp.video.SecondaryGenre
Xmp.video.Sharpness
Xmp.video.Software
Xmp.video.SoftwareVersion
Xmp.video.Source
Xmp.video.SourceForm
Xmp.video.Starring
Xmp.video.StartTimecode
Xmp.video.Statistics
Xmp.video.StreamCount
Xmp.video.StreamQuality
Xmp.video.StreamSampleSize
Xmp.video.Subject
Xmp.video.TapeName
Xmp.video.Technician
Xmp.video.Title
Xmp.video.TrackNumber
Xmp.video.URL
Xmp.video.VegasVersionMajor
Xmp.video.VegasVersionMinor
Xmp.video.VideoQuality
Xmp.video.VideoSampleSize
Xmp.video.WatermarkURL
Xmp.video.Width
Xmp.video.WrittenBy
Xmp.video.Year
```
