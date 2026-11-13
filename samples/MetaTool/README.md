# MetaTool

A small exiv2-style command-line example built on `ImageMetadata`, the public format documents, and `MetadataOperations`.

```sh
dotnet run --project samples/MetaTool -- -p a -K Exif.Photo.DateTimeOriginal photo.jpg
dotnet run --project samples/MetaTool -- set Exif.Image.Artist "Ada Lovelace" photo.jpg
dotnet run --project samples/MetaTool -- extract xmp photo.jpg photo.xmp
```

It also supports `del`, `rename`, thumbnail/XMP extraction and insertion, `fixiso`, `fixcom`, and `-M`/`-m` modify scripts. Script edits are applied family-by-family through the public documents; unsupported container/family combinations fail explicitly.
