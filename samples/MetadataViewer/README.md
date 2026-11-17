# MetadataViewer

A small read-only Avalonia desktop viewer. Open a file or drop it onto the window; browse metadata grouped into families and groups, search keys/values, inspect raw and interpreted forms, and copy the selected value. It makes no edits and decodes no image pixels.

```sh
dotnet run --project samples/MetadataViewer -- ./photo.jpg
```

CI builds this project on .NET 10/Linux but does not launch a desktop UI test.
