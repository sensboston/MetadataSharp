# MetadataSharp

An MIT-licensed, fully managed .NET library for image metadata: EXIF, IPTC, XMP and camera
MakerNotes, read and written without decoding or recompressing image data. No native code,
no runtime dependencies. Targets .NET 8 and .NET 10.

The goal is exiv2-level functionality as an independent implementation. **Work in progress:**
see [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) for what works today and what is next.

## Highlights

- JPEG, PNG, WebP, TIFF, EXV, DNG (read), XMP sidecars; restricted, safety-first editing for
  JPEG, PNG, WebP and TIFF: an edit whose safety cannot be established is refused, never guessed.
- Typed EXIF vocabulary from the CIPA and TIFF specifications.
- Read-only MakerNotes for Canon, Nikon (including encrypted arrays), Fujifilm, Pentax, Sony,
  Panasonic and Olympus/OM System.
- Bounded parsing of hostile input: every length, offset and count is checked and budgeted.
- Continuously measured against the exiv2 0.28.9 test corpus.

## Documentation

- [Current state and roadmap](docs/CURRENT_STATE.md)
- [Build and test](docs/BUILD_AND_TEST.md)
- [Development policy](docs/DEVELOPMENT.md)
- Usage and preservation limits: [docs/usage](docs/usage)
- Contributor and agent rules: [METADATASHARP_PROMPT.md](METADATASHARP_PROMPT.md)
- Design history: [docs/archive](docs/archive) (read-only)

## License

MIT, see [LICENSE](LICENSE).
