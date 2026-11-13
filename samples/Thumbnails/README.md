# Thumbnails

Lists every embedded preview the public `Previews` API discovers, and extracts every supported JPEG or raw-RGB preview. Coded BMFF video previews are listed and reported as non-extractable rather than guessed at.

```sh
dotnet run --project samples/Thumbnails -- list camera.raw
dotnet run --project samples/Thumbnails -- extract camera.raw ./previews
```
