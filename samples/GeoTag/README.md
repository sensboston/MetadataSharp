# GeoTag

Reads GPX track points and writes the nearest point's GPS coordinates to each image's EXIF, using the capture time. `--offset` is added to each image timestamp before matching; `--max-gap` bounds the allowed nearest-point distance. No image is changed in dry-run mode.

```sh
dotnet run --project samples/GeoTag -- --gpx walk.gpx --offset 00:00:00 --max-gap 90s --dry-run *.jpg
```

The example uses only the public `ImageMetadata` reader, `ExifEdit`, and public format documents. The input needs a readable capture timestamp and a writable EXIF-bearing format.
