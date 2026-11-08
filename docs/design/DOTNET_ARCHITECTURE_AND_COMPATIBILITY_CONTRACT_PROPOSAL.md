# E03: proposed .NET architecture and compatibility contracts

Status: **PROPOSAL — requires explicit developer approval before any implementation.**
This is E03 of Programme 2 ([EXIV2_PARITY_AND_DOTNET_PROGRAM](../roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md)).
Approval of this document approves exactly the additive surface in section 4 and the
policies in sections 5–8. It approves **no per-format API, no DNG implementation,
no refactor and no behavioral change** to any approved contract.

## 1. Purpose and evidence boundary

E02 proved dual net8.0/net10.0 builds, consumers and equal reflected-signature
sets (633 entries, hash `a9cd2ae9…`, not full metadata/behavior proof). Before
E06–E18 add breadth on top of that base, the library needs three things fixed in
stone: (a) how consumers discover what this build can do, (b) the compatibility
and migration rules every later unit must obey, (c) the architectural invariants
(immutability, ownership, concurrency, error taxonomy) the breadth work assumes.

Evidence used: existing source/contracts, the E01 seeds/baseline work, the E02
receipts, and the binding user directive of 2026-11-08 (parity goal; avoid
external dependencies). No external API was copied; enum values and signatures
are our own design.

## 2. Non-goals

- No changes to approved JPEG/IPTC/XMP/PNG/WebP/TIFF contracts, limits, refusal
  rules or the `ExifEdit` writable-tag subset.
- No new metadata family implementations (E06 owns vocabulary breadth).
- No public common base class/interface for documents (rejected: would force
  families with different invariants into one fiction; “prefer internal shared
  mechanisms before new public bases” stays binding).
- No CLI syntax, no global mutable registries, no machine-global capability
  probing, no native optional dependencies, no network access at runtime.
- No fixed error-code renumbering; unknown codes must remain constructible and
  consumers must tolerate them.
- No public API analyzer packages (dependency-free rule, section 7).

## 3. Architectural decisions (binding if approved)

1. **Owned immutable snapshots.** Every returned metadata model stays independent
   of input buffers and source lifetime; collections stay owned read-only views.
   New families follow the same rule from their first unit — no exceptions.
2. **Per-family keys, no universal tag namespace.** `ExifTagKey`, `IptcDatasetKey`,
   `XmpPropertyName` are the pattern; new families define their own key structs.
   Cross-family correlation (EXIF vs XMP vs IPTC) is a consumer concern, never a
   hidden library merge.
3. **Additive-only evolution.** New enum members, new types, new optional
   properties with behavior-preserving defaults are the only allowed in-major
   changes. Removals/renames/renumbers happen only at a major boundary with an
   `[Obsolete]` window of at least one intervening release and a documented
   replacement. Approved semantics never change silently.
4. **Fail closed.** Unsupported/unknown/unverified is the default. Capability
   rows default to `NotSupported`; a missing row is never “probably fine”.
5. **Typed access honesty (E06 contract pattern).** Typed accessors must expose
   element count (TIFF COUNT / RDF container length / repeated dataset count)
   and byte length as distinct values, and must fail explicitly rather than
   guess encodings, guess RDF structure or coerce ambiguous rationals
   silently. Display formatting is never a prerequisite for binary correctness.
6. **Concurrency.** Documents and snapshots are independent and safely usable on
   separate threads; no shared mutable state anywhere; XMP namespace context is
   per packet, not process-global; streams/paths are not mutated concurrently by
   the library; callers must not concurrently mutate one document; cancellation
   is honored at I/O and XML boundaries; async members mirror sync ones
   (`ConfigureAwait(false)`, no sync-over-async). No promise of concurrent
   mutation of a single stream or document.
7. **Diagnostics stay structured.** Failures keep `MetadataException` with
   `MetadataErrorCode` + optional `SourceOffset`. Strict-by-default preflight.
   Non-fatal diagnostics may ship later only via an approved additive carrier;
   they never turn a refusal into silent success.
8. **Limits inheritance.** Every new surface carries finite budgets with the same
   validation/ordering discipline as `MetadataLimits`; no “best effort” bypasses.
9. **D0 consistency.** This proposal adds no member to `MetadataErrorCode`;
   the already-approved D0 appends `InvalidDng` and remains independent. New
   container-level codes (e.g. `InvalidContainer`, `UnsupportedVersion`) are
   deferred until the E08/E09/E11 units that need them and ride those proposals.
10. **Cardinality/casing/style.** Enum member additions are data-compatible;
    public ordering of `IReadOnlyList` views is stable and documented per type.

## 4. Exact proposed surface (additive; requires approval)

Namespace `MetadataSharp.Core`, assembly `MetadataSharp.Core` (Core has no
dependencies; Formats references it). No existing type is modified.

```csharp
namespace MetadataSharp.Core;

/// <summary>Container/file categories for capability discovery. Unknown = 0.
/// Members cover the 31 documented v0.28.9 baseline category rows; the XMP
/// row is represented as XmpSidecar. Every documented category is present so
/// "not supported" is an explicit answer, not an absence of information.</summary>
public enum MetadataContainer
{
    Unknown = 0,
    Jpeg, Png, WebP, Tiff, Dng,
    Arw, Avif, Bmp, Cr2, Cr3, Crw, Dcp, Eps, Exv, Gif,
    Heic, Heif, Jp2, Jxl, Mrw, Nef, Orf, Pef, Pgf, Psd,
    Raf, Rw2, Sr2, Srw, Tga, XmpSidecar
}

/// <summary>Metadata family granularity used for capability rows.</summary>
public enum MetadataFamily
{
    Exif = 0,
    IptcIim,
    Xmp,
    Icc,
    Comment,
    Thumbnail,
    Preview
}

/// <summary>User-visible operation classes; mirrors the E01 ledger axes.</summary>
public enum MetadataOperation
{
    Read = 0,
    Insert,
    Replace,
    Remove,
    Extract
}

/// <summary>Fail-closed support status for one capability row.</summary>
public enum CapabilityStatus
{
    NotSupported = 0,
    Partial,
    Supported
}

/// <summary>Immutable description of one (container, family, operation) row.
/// Note is a bounded, compile-time constant explaining partiality or scope;
/// null when nothing needs qualifying.</summary>
public readonly record struct CapabilityDescriptor(
    MetadataContainer Container,
    MetadataFamily Family,
    MetadataOperation Operation,
    CapabilityStatus Status,
    string? Note);

/// <summary>Build-specific capability table. Static, immutable, deterministic;
/// no reflection probing, no machine-dependent behavior, no I/O. A row is
/// Supported/Partial only with test evidence in this repository; anything
/// else is NotSupported. Missing rows are NotSupported by contract.</summary>
public static class MetadataCapabilities
{
    /// <summary>All declared rows in a stable, documented order.</summary>
    public static IReadOnlyList<CapabilityDescriptor> All { get; }

    /// <summary>Fail-closed lookup; false when the row is absent.</summary>
    public static bool TryGet(MetadataContainer container, MetadataFamily family,
        MetadataOperation operation, out CapabilityDescriptor descriptor);

    /// <summary>Convenience; returns NotSupported when the row is absent.</summary>
    public static CapabilityStatus GetStatus(MetadataContainer container,
        MetadataFamily family, MetadataOperation operation);
}
```

Implementation notes (not additional surface):

- The table is built once from a single internal literal manifest, sorted by
  (Container, Family, Operation); consumers may rely on that order.
- Initial contents reflect only shipped, test-evidenced capability: the current
  JPEG/PNG/WebP/TIFF/sidecar subset as Partial/Supported per the shipped
  contracts, everything else NotSupported. The contents are **data**, pinned by
  tests, and may grow only via reviewed units that update both the manifest and
  its evidence tests in the same PR.
- Enum casing matches existing repo style (`WebP`, `Jpeg`, `Xmp` are unchanged
  type names; enum members use `Jpeg`, `WebP`, `XmpSidecar`, etc.).
- Adding members later is additive and keeps binary compatibility; consumers
  must use `default`/`Unknown` arms, never assume exhaustiveness.

## 5. Compatibility and migration policy (binding if approved)

- **TFMs.** `net8.0` + `net10.0` remain the only targets; C# 12; LangVersion
  not raised without a TFM review. net8 stays while supported and reviewed;
  dropping it requires a documented decision (it does not silently ride an
  E-unit). net10 is the recommended consumer runtime.
- **Surface audit.** The reflection-signature receipt in
  `audits/evidence/e02` remains the compatibility alarm. Every PR changing the
  public surface regenerates the receipt; the delta must match the approved
  proposal exactly; net8 and net10 receipts must remain equal. This is a
  regression alarm, NOT a metadata/behavior/ABI proof.
- **Annotations.** Nullable reference types stay enabled; reference-annotation
  changes to existing members are breaking for downstream nullability analysis
  and are treated as removals for policy purposes.
- **Versioning.** SemVer. Pre-1.0, breaking changes need a minor bump and a
  migration note; post-1.0, major only. No SemVer promise until E18 release
  readiness is approved; until then the policy is “additive only anyway”.
- **No package publication** under this approval. Publication is E18 and requires
  separate developer authorization.
- **XML documentation** for every public member; capability notes stay bounded
  (<= 240 characters) and English.
- **Deprecation mechanic.** `[Obsolete("… use X; removal after <version>")]`;
  error-state only at the announced major; no behavioral shim accumulation.
- **Test dependencies** remain private assets; the shipped packages keep zero
  third-party dependencies (section 7).

## 6. Verification plan for the implementing unit

- Unit tests: table contents vs an independently authored expectation list;
  ordering stability; `TryGet`/`GetStatus` fail-closed behavior; note-length
  bound; all enum members round-trip `All`.
- Consistency: every Supported/Partial row links to at least one test-suite
  category (document mapping in the manifest; enforced by a policy test).
- Reflection receipt regenerated; delta reviewed against this proposal; both
  TFMs equal; 633-entry predecessor baseline preserved.
- `tools/audit` receipts unchanged in kind; no new external dependency.
- Cross-platform CI + existing gates unchanged in count/meaning; no skipped,
  weakened or self-selected tests.
- Docs: capability table mirrored in user docs via generated reference (E17/E18
  owns the published matrix; links back here).

## 7. Zero third-party runtime dependencies (reaffirms the 2026-11-08 directive)

Core and Formats ship **no** PackageReference — measured 2026-11-08 for this
proposal. Binding rules:

- BCL-only implementation; no P/Invoke, native assets, analyzers, source
  generators or embedded executables in shipped assemblies.
- External tools remain CI-only differential oracles.
- Any exception requires its own reviewed proposal with license/maintenance
  review, per-TFM verification and explicit developer approval; it is never
  introduced by a feature unit. Default position: implement with BCL or refuse.
- The implementer extends `tests/ProjectInfrastructureTests` to fail the build
  if any `src/*.csproj` declares a `PackageReference` (test-local guard; no
  dependency added by the guard itself).

## 8. What approving this proposal does and does not do

Approving grants: the section 4 surface, sections 3/5/7 policies, and the
verification plan. It does **not** grant: DNG/D1 implementation (D0 approved;
D1 still needs its own explicit go-ahead and the normative/fixture/oracle
prerequisites), any E06–E18 format/family work, any change to approved
contracts, any exclusions from the E01 denominator, or package publication.
If the developer approves only parts, the implementer records exactly which and
stops at the rest.

## 9. Open questions for the developer

1. Capability granularity: is (container, family, operation) sufficient for
   consumers, or must GPS/Interop be split from EXIF now (would add rows, not
   mechanism)?
2. Should `MetadataCapabilities.All` be published as a stable data contract
   (docs guarantee set membership semantics only, not formation), or kept as a
   convenience with the reflection receipt as the contract?
3. Net8 end of servicing was 2026-11-10 during E02; keep net8 builds after that
   date until an explicit removal decision (this proposal), or schedule its
   removal at the next release?
4. Is the test-local zero-`PackageReference` guard welcome in the implementing
   unit, or preferred as a CI-only check in `build-and-test.yml`?
