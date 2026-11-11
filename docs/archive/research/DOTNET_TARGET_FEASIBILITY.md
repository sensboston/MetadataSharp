# E02 preliminary runtime/API feasibility — no target changes yet

Developer approved targeting net8.0 and net10.0, with Framework 4.8.x only if cheap.
This is source-level feasibility, not a successful multi-target build or final
compatibility policy. Existing Directory.Build.props still targets only net8.0.

Official dotnet/core release metadata pinned in the programme records .NET 8 end
of support 2026-11-10 and .NET 10 LTS end of support 2028-11-14. Rechecked official
[releases overview](https://github.com/dotnet/core/blob/de2e9337026c41d9ab1af031a9ce968f98a7a2c6/releases.md).
Maintaining a net8.0 assembly does NOT extend Microsoft's security servicing.
Recommend net10.0 for new deployments; publish an explicit EOL-runtime caveat.
Do not infer Framework lifecycle from modern .NET dates or promise every Windows
version supported by the runtime is tested by this library.

## Proposed modern-target implementation gate

- Separate library TFMs net8.0/net10.0, explicit SDK selection and language version
  policy; SDK availability alone is not proof a target ran. No production API
  changes merely to make multi-targeting compile.
- Tests per TFM on Windows/Linux/macOS (six combinations), TRX target identity and
  count verification, required external-reference behavior and package-only consumer
  for both TFMs. Never let tests silently select only the newer runtime.
- Audit project graph: test/tool apps may need explicit TFMs while shared library
  properties change. Update tools/audit feed assertions expecting one lib/net8.0
  DLL to deliberately validate both TFM assets; do not just loosen DLL checks.
- Record runtime/SDK/architecture and compare supported behavior, errors, async/
  cancellation/source identity, culture/Unicode, serialization and coded-data
  preservation. Rebaseline measurements as new measurements, not a net8→10 speedup
  claim inferred from SDK version.
- Confirm package dependency groups, XML docs per TFM and offline consumers. Decide
  whether API surfaces must remain identical; if not, propose contracts first.
  No speculative NativeAOT/trimming/strong-naming/Framework compatibility claim.

## Framework 4.8/4.8.1 initial cost map

| Existing dependency/pattern | Examples in current repository | Work needed before claiming feasibility |
| --- | --- | --- |
| Modern guard helpers | ArgumentNullException.ThrowIfNull throughout Core; ObjectDisposedException.ThrowIf in document facades | Internal shims or equivalent checks, preserving parameter names/errors; many call sites |
| Spans/BinaryPrimitives | Raw values and TIFF/container framing | Reviewed compatibility packages and target API map; C# compiler support alone insufficient |
| IAsyncDisposable/ValueTask and memory-based stream overrides | Source ownership and async documents/ContainerIo | Public contract/package implications, actual Framework stream I/O/short-read/cancellation tests; no Task.Run fallback marketed as equivalent |
| Modern crypto helpers | IncrementalHash / CryptographicOperations and span-based identity I/O | API equivalents, incremental hash behavior and allocation/performance/constant-time requirements |
| Compression | ZLibStream in PNG text handling | Framework lacks the same convenient zlib surface; replacement/dependency review and bounded expansion/validation tests, no unsafe handwave around wrappers |
| Language/runtime support | Records/init members, range/span APIs and modern collections | Compiler/runtime shim and transitive dependency inventory; do not fork the engine |
| Packaging/test infrastructure | net8-only tools/consumers and three-OS matrix | Isolated Windows Framework runner/reference assemblies/consumer and separate support policy; cross-compilation is not runtime validation |

Current evidence does **not** establish a light addition. No net48/net481 or
netstandard2.0 target, compatibility package, native bridge or source fork added.
Framework is provisionally deferred, pending an itemized API inventory and bounded
Windows spike if the developer still wants to investigate cost. Reject inclusion
if it requires weakening async/safety contracts, duplicated parsers or broad
conditional compilation. Modern .NET work need not wait for speculative legacy
support. Final cheap/not-cheap decision remains an E02 deliverable, not this scan.

## Actual checks / blockers

Source inspection and earlier recorded net8 evidence only. Neither net10 nor
Framework was compiled/run in this checkpoint. Local SDK absent; prior Actions
billing blocker is not resolved by editing target strings. Implementation requires
its own green gates; PR #13's documentation exception does not apply.

## Superseding E02 implementation evidence

The preliminary no-build status above is historical. E02 now implements dual modern
targets and passes 42446034373 at 2949589: six target/OS correctness jobs, six strict
package/consumer audits and two references. See
../audits/DOTNET_8_10_SUPPORT_AND_E02_AUDIT.md for exact scope/evidence/SDK/runtime
checks. Framework remains deferred (cheap threshold not established); no legacy
build was executed. E03/new API compatibility approval is not inferred.
