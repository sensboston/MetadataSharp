# MetadataSharp: Project Instructions

## Mission

Build an independently implemented, memory-safe, cross-platform metadata library
in managed C# (C# 12+, .NET 8+) under MIT. Read, create, edit, and remove EXIF,
IPTC, and XMP without decoding or recompressing image data. Preserve MakerNotes
only where safety can be established; do not promise arbitrary vendor editing.

Exiv2 is a functional reference used as an external black box, not implementation
material. Implement from specifications. MIT code can be incorporated into GPL
projects, but GPL-derived implementation cannot simply be redistributed as
MIT-only code.

## Current implementation boundary

M01–M11 implement restricted JPEG EXIF/XMP editing, bounded XMP packets/sidecars
and the external-reference audit. See `docs/design/XMP_USAGE_AND_PRESERVATION_LIMITS.md`
for the approved XMP profile. See `docs/audits/INITIAL_JPEG_RELEASE_AUDIT.md` for conformance limitations
and remaining publication gates. No package has been published. M12+ requires
new scope/contracts; the broader mission below is not implemented capability.

## Communication

- Discuss architecture, questions, and progress with the developer in Russian.
- Write code, identifiers, XML documentation, error messages, tests, new project
  documentation, and commit messages in English.

## Authoritative development documents

- [Development policy](docs/DEVELOPMENT.md): safety, licensing, testing, workflow.
- [Roadmap](docs/ROADMAP.md): ordered work units, prerequisites, completion gates.
- [Execution prompts](docs/prompts/README.md): reusable instructions for each unit.

This document replaces the original six-milestone draft. Do not combine old stage
numbers with the new roadmap. These documents are project guidance, not a claim
that any feature has already been implemented. If requirements conflict, stop and
ask rather than silently selecting the easier interpretation.

## Engineering baseline

- Use file-scoped namespaces and nullable reference types. Choose records,
  readonly structs, and required properties where semantically appropriate.
- Use BinaryPrimitives for byte order; do not use BitConverter for binary parsing.
- Minimize allocations in hot paths; do not promise zero allocation for metadata
  object models. Use spans and bounded pooled buffers where appropriate; return
  rentals in finally blocks and define buffer ownership explicitly.
- No unsafe code without explicit justification and approval.
- Validate lengths, offsets, counts, and arithmetic before accessing data.
- Treat pointers as a potentially cyclic graph, not an implicitly safe tree.
- Never silently discard unknown metadata or relocate opaque pointer-bearing data
  under a claim of safe preservation.
- Design changes as analysis, validated write plan, then output. Refuse edits whose
  safety is unknown by default.
- Agree public contracts before implementing them. Every parser and writer needs
  boundary, malformed-input, and preservation tests.

## Intended logical structure

- MetadataSharp.Core: binary primitives, raw TIFF/EXIF model, bounded parsing,
  layout planning, and EXIF serialization.
- MetadataSharp.Formats: container-specific scanning and rewriting.
- Tests: fast independent unit tests, generated regression fixtures, and optional
  external-tool integration tests. Final project boundaries require API approval.

## Specification policy

Record exact editions and relevant sections when designing each feature: TIFF 6.0,
CIPA Exif, JPEG, Photoshop IRB/IPTC IIM, PNG, WebP RIFF, and XMP/RDF. Public access
is not permission to copy a specification, tag dictionary, or fixture. Verify
licenses and redistribution terms before importing material. Verify the exact
package identity, version, license, dependencies, and suitability of any proposed
XMP library rather than treating a package name as pre-approved.

## Immediate next step

M00 contracts were approved on 2026-11-07. Read
[the approved contracts](docs/design/INITIAL_CONTRACTS.md) and
[accepted tradeoffs](docs/decisions/INITIAL_RELEASE_ACCEPTED_TRADEOFFS.md).
M01–M06 restore/build/test passed on Linux, Windows and macOS in GitHub Actions.
The local environment still has no SDK; use CI as described in
[build instructions](docs/BUILD_AND_TEST.md). M07 is next under the standing sequential authorization; verify JPEG normative
prerequisites before coding.
The initial release target is a restricted JPEG EXIF reader/editor with explicit
unsafe-edit refusal.
