# M10–M11: XMP Packet, Sidecar and JPEG Contract Proposal

Status: **APPROVED by the developer on 2026-11-07.**
Date: 2026-11-07. Group: one M10–M11 PR, separate implementation commits and CI gates.
The developer explicitly approved these contracts and authorized sequential execution.

## 1. Dependency and scope decision

Use the .NET 8 framework XML APIs (`System.Xml`, `System.Xml.Linq`), with no new
NuGet or native dependency. This is a deliberately restricted XMP/RDF codec, not a
full RDF engine or a replacement for Adobe's toolkit. Never silently flatten
structures, discard qualifiers or reinterpret unknown RDF constructs.

M10: bounded packets and sidecars; simple literal properties, text Bag/Seq arrays,
and language alternatives. Structured/nested properties, resource-valued properties,
general qualifiers and arbitrary RDF graphs remain opaque and noneditable.
M11: standard JPEG XMP APP1 only. Detect Extended XMP and refuse XMP changes;
do not implement chunk assembly, MD5 identifiers or Extended XMP writing here.
EXIF and IPTC remain independent; no automatic synchronization or privacy claim.

Before implementation, review exact editions/sections of XMP Parts 1 and 3 and
record hashes and findings in a normative-source document. Located primary sources:
`adobe/XMP-Toolkit-SDK`, revision `7093513bd3caaad29da01db0f275d88a39d6bcc2`,
`docs/XMPSpecificationPart1.pdf` (blob `3118eadbdd4b8ba033f36bb68bd291e4d38ea95f`)
and `docs/XMPSpecificationPart3.pdf` (blob `c7ee90e2b2b60e9ccef42bd0b6d676b21bc6591a`).
These PDFs were reviewed before implementation; see
`XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md` for editions, sections and hashes. Do not import
SDK source/tests or redistribute the PDFs. If normative review contradicts this
proposal, document the discrepancy and obtain approval for contract changes.

## 2. Proposed public API

Signatures below omit namespace imports and bodies. Existing EXIF signatures remain.
All Core XMP types are in `MetadataSharp.Core.Xmp`.

```csharp
public readonly record struct XmpPropertyName
{
    public XmpPropertyName(string namespaceUri, string localName);
    public string NamespaceUri { get; }
    public string LocalName { get; }
}

public enum XmpValueKind { Text, Bag, Sequence, LanguageAlternative }

public sealed class XmpValue
{
    // No public constructor; factories copy inputs and validate before mutation.
    public static XmpValue CreateText(string text);
    public static XmpValue CreateArray(XmpValueKind kind, IEnumerable<string> items);
    public static XmpValue CreateLanguageAlternative(
        IEnumerable<KeyValuePair<string, string>> values);
    public XmpValueKind Kind { get; }
    public string? Text { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyDictionary<string, string> Languages { get; }
}

public sealed class XmpReadOptions
{
    public XmpLimits Limits { get; init; } = new();
}

public sealed record XmpLimits
{
    public int MaxPacketBytes { get; init; } = 1_048_576;
    public int MaxXmlCharacters { get; init; } = 1_048_576;
    public int MaxDepth { get; init; } = 32;
    public int MaxNodes { get; init; } = 16_384;
    public int MaxAttributes { get; init; } = 4_096;
    public int MaxProperties { get; init; } = 2_048;
    public int MaxArrayItems { get; init; } = 8_192;
    public int MaxNameCharacters { get; init; } = 1_024;
}

public sealed class XmpEdit
{
    public XmpEdit();
    public bool IsEmpty { get; }
    public void Set(XmpPropertyName name, XmpValue value);
    public void Remove(XmpPropertyName name);
    public void RemoveAllXmp();
}

public sealed class XmpPacket
{
    public static XmpPacket CreateEmpty(XmpReadOptions? options = null);
    public static XmpPacket Parse(ReadOnlySpan<byte> bytes, XmpReadOptions? options = null);
    public static XmpPacket Load(Stream source, XmpReadOptions? options = null);
    public static XmpPacket Load(string path, XmpReadOptions? options = null);
    public static Task<XmpPacket> LoadAsync(Stream source, XmpReadOptions? options = null,
        CancellationToken cancellationToken = default);
    public static Task<XmpPacket> LoadAsync(string path, XmpReadOptions? options = null,
        CancellationToken cancellationToken = default);
    public bool IsEditable { get; }
    public int ByteLength { get; }
    public void CopyRawTo(Span<byte> destination);
    public IReadOnlyDictionary<XmpPropertyName, XmpValue> Properties { get; }
    public XmpPacket? Apply(XmpEdit edit);
    public void Save(Stream destination);
    public Task SaveAsync(Stream destination, CancellationToken cancellationToken = default);
    public void SaveAs(string destinationPath);
    public Task SaveAsAsync(string destinationPath, CancellationToken cancellationToken = default);
}
```

`CreateArray` accepts only Bag/Sequence. Inactive value views are empty (`Text` is
null outside Text). Properties and values are immutable, deeply owned snapshots;
read-only interfaces do not expose mutable backing collections. Array factories
stop enumerating and reject inputs beyond finite default value/item budgets;
caller-supplied enumerators that block are outside library cancellation guarantees.
Property identity is namespace URI plus XML NCName, not prefix; equality is ordinal,
without URI canonicalization or Unicode normalization. Empty/relative namespace
URIs and reserved RDF/XML/XMP wrapper namespaces cannot be addressed as properties.
The default struct name is invalid and rejected at operation boundaries.

JPEG additions to `MetadataSharp.Formats.Jpeg.JpegMetadataDocument`:

```csharp
public bool HasExtendedXmp { get; }
public XmpPacket? ReadXmp(XmpReadOptions? options = null);
public Task<XmpPacket?> ReadXmpAsync(XmpReadOptions? options = null,
    CancellationToken cancellationToken = default);
public void SaveXmp(Stream destination, XmpEdit edit);
public Task SaveXmpAsync(Stream destination, XmpEdit edit,
    CancellationToken cancellationToken = default);
public void SaveXmpAs(string destinationPath, XmpEdit edit);
public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit,
    CancellationToken cancellationToken = default);
```

ReadXmp performs source hash/length preflight before reading recorded ranges and
returns an independently owned packet (null if no standard or extended XMP).
It uses per-call XMP options. JPEG SaveXmp uses default XmpLimits, plus the document's
existing MetadataLimits; smaller applicable limits win. Both paths account for the
retained EXIF plus XMP payloads against MaxRetainedMetadataBytes; this is a logical
metadata budget, not a claim of exact total managed-memory accounting. Multiple changes to EXIF
and XMP are not combined into one save in this group; reopen an intermediate output
when both are desired. This avoids changing existing save overload resolution.

Append `InvalidXmp` and `XmpTooLarge` to MetadataErrorCode without renumbering existing
members. Reuse LimitExceeded, UnsupportedFeature, AmbiguousMetadata and SourceChanged.
Caller argument errors use ArgumentException; cancellation and ordinary I/O retain
the existing exception conventions. Invalid XML/encoding/XMP root is InvalidXmp;
well-formed unsupported RDF is not mislabeled malformed.

## 3. RDF subset, opacity and output guarantees

Accept UTF-8 (optional BOM) only in this first profile; reject UTF-16/32 and
conflicting XML declarations explicitly. XML characters and namespace bindings
must be valid. Parse optional XMP wrapper/packet processing instructions according
to normative review, one RDF root and one empty-subject rdf:Description. Supported
properties may use simple attributes or simple elements, unqualified literal array
items, or rdf:Alt items with unique xml:lang. Duplicate addressed property names
are ambiguous. Duplicate language names compare case-insensitively; reject them.
Language names receive bounded syntax checks, not full BCP 47 registry validation.
Preserve text whitespace/Unicode exactly; do not infer dates, numbers or booleans.
No property namespace dictionary or bulk tag database is imported.

For a well-formed XMP packet containing unsupported RDF, IsEditable is false.
Properties throws UnsupportedFeature rather than returning a misleading partial
model. Raw copy/no-op and explicit whole-packet removal remain possible.
A read-only packet wrapper also prevents property changes. Unknown namespaces by
themselves are not unsupported: a simple property with an unknown namespace is
retained and can be addressed by its expanded name. Unsupported packet syntax is
not discarded or automatically repaired.

Empty edit returns original bytes exactly. RemoveAllXmp cannot mix with addressed
changes; repeating it is idempotent. It makes Apply return null, explicitly meaning
packet absence. It does not delete a sidecar on disk. Removing the last property
alone leaves an empty valid packet. Last addressed operation wins; removing a
missing property alone is a no-op. Invalid Set validates before changing the edit.

Any nonempty effective property change to an editable packet may serialize the
whole XML packet with stable generated prefixes, UTF-8, no BOM and no padding;
lexical form, attribute order and packet size need not be preserved. Supported
untouched values/namespaces and Bag/Seq/Alt meaning must be preserved; Bag duplicates
remain, Seq order remains, language alternative keys/values remain. x-default
ordering and packet wrapper emission must follow the reviewed specification.
Do not claim arbitrary RDF/XML semantic equivalence or lexical preservation.

## 4. Resource, stream and sidecar rules

Disable DTDs, external entities, network/file resolution, XInclude and schema loading.
Reject DOCTYPE, cap bytes before allocating input, set XML character limits and use
a bounded forward reader to enforce depth/node/attribute budgets before constructing
any DOM. Check output size and all operation limits before writing. Snapshot options;
reject nonpositive settings. Factories use default finite limits; Apply also enforces
the packet's captured limits. No decompression or hidden network work.

Stream loads consume from the current position to EOF, permit short reads and
nonseekable input, leave borrowed streams open and retain no source reference.
Path loads close their owned source. Async APIs perform actual asynchronous I/O,
check cancellation during bounded processing and leave borrowed streams open.
Saving requires a writable, seekable, empty destination at position zero; the
packet is already detached from its source. Preflight fails before any writes;
I/O/cancellation after writing starts may leave partial caller output.

SaveAs uses a CreateNew same-directory temporary file, flush/close then publish
without overwriting any existing target. No implicit source overwrite or deletion.
Preserve competing targets and clean temporary files best-effort. Cancellation after
successful publication does not falsely report a canceled save. No universal
atomicity/crash-durability promise. No packet/edit concurrent mutation guarantee.

## 5. JPEG discovery and independence

Standard APP1 signature: `http://ns.adobe.com/xap/1.0/` plus one NUL (29 bytes).
Maximum standard packet size is 65,504 bytes (65,533 payload bytes minus signature).
Extended signature: `http://ns.adobe.com/xmp/extension/` plus one NUL.
Use byte-exact signatures, not APP1 marker number alone. Record packet counts and
extension presence without parsing unrelated metadata. Do not broaden the M07
single-scan baseline JPEG acceptance subset.

Duplicate standard packets cause AmbiguousMetadata for XMP reads/changes, including
whole removal. Any Extended XMP segment causes UnsupportedFeature for XMP reads
and effective XMP changes, including whole removal, even without a standard packet.
Also refuse property changes if the standard packet advertises
xmpNote:HasExtendedXMP without chunks; never serialize a stale extension reference.
Whole removal of that unique standard packet remains allowed when no extension
segments exist, because it removes the dangling reference as well. A malformed extension header
with the exact signature still counts as an extension for conservative refusal.

Existing EXIF-only operations and exact no-op copies preserve all standard/extended
XMP bytes, including unsupported XML and duplicates, and do not start parsing XML.
An empty XmpEdit likewise copies a supported source exactly; it does not resolve
XMP ambiguity or promise packet readability. Existing EXIF validation at Open is
unchanged; this group does not add repair of malformed EXIF/JPEG.

SaveXmp preflights source identity, XMP validity/safety and size before output.
Replace the one standard segment in place or insert at the existing metadata
insertion point; remove only the exact matching standard segment when permitted.
Preserve EXIF, APP13, unknown APP1, marker ordering outside the addressed range and
encoded image bytes exactly. Removing XMP is not a complete metadata/privacy scrub.
Malformed standard XML can be removed whole when its segment is uniquely identified
and no Extended XMP/reference hazard exists; never perform a partial property edit.

## 6. Gates and execution sequence

1. Obtain approval of this public API, restricted RDF/UTF-8 profile, no new package,
   opaque-packet policy, finite defaults, sidecar/path rules and extension refusals.
2. Review normative sources; implement only M10. Test independent expected XML,
   namespaces, attribute/element forms, arrays/languages, Unicode, opaque structures,
   exact no-op, semantic preservation, malformed encodings/XML, entity/depth/size
   attacks, short async reads, cancellation and publication failure/races.
3. Commit M10 separately and require green Linux/macOS/Windows CI before M11.
4. Implement M11 with boundary, signature, duplicate, extended/stale-reference,
   unrelated EXIF/APP13, encoded-byte and failure tests. Extend the independent CLI
   harness with owned XMP fixtures; do not weaken existing EXIF validation checks.
5. Record discrepancies and final group CI; merge only after all gates pass.
   No IPTC implementation, packaging or publication in this group.
