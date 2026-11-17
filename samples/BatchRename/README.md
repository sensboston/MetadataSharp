# BatchRename

Renames images using `DateTimeOriginal` (or the library's `DateTaken` fallback) and the library's deterministic strftime-style `MetadataFileNames` formatter. Name collisions get `_1`, `_2`, and so on before the extension. The tool plans all moves before changing any file.

```sh
dotnet run --project samples/BatchRename -- --pattern '%Y%m%d_%H%M%S' --dry-run ./photos/*.jpg
```
