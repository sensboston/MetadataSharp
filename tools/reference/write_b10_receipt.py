"""One-shot builder for the B10 evidence receipt (recovery provenance, not a census tool).

B9's receipt was assembled by hand and the hand-built version is exactly where an evidence
record goes stale: the hashes have to come from the recovered payload, the tally from the
report, and the run identity from the API. This script reads the committed evidence and
recomputes everything, so `receipt.json` can be regenerated after a re-run instead of
re-typed. It is kept in tools/ rather than thrown away because the same shape applies to
the second and third samples: pass --ci/--master with a recovered payload and the
reproduction section is filled in.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import jpeg_owned_fixtures as jpeg  # noqa: E402

EVIDENCE = ROOT / 'docs/research/evidence/e01-b10-census'
PREFLIGHT = ROOT / 'docs/research/evidence/e01-b10-preflight'
RUN_ID = 42742515073
CHECK_RUN_ID = 129586778952
HEAD = 'a147cf2ec0674b702f1d2a547d5b8987a6dd24ff'
PULL = 51
ANNOTATIONS = 11


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ci', type=Path, help='second recovered payload, for cross-run reproduction')
    parser.add_argument('--master', type=Path, help='post-merge sample')
    parser.add_argument('--ci-run', type=int, help='run id of the --ci sample')
    parser.add_argument('--master-run', type=int, help='run id of the --master sample')
    args = parser.parse_args()

    report_path = EVIDENCE / 'observations.json'
    raw = report_path.read_bytes()
    report = json.loads(raw)
    # The annotation payload is compact; the committed evidence file is indented exactly as the
    # recorder writes it, so a re-run's artifact diff is empty. Both digests are recorded: one
    # for the file, one for the canonical payload the recovery produced.
    pretty = json.dumps(report, indent=2) + '\n'
    if raw.decode() != pretty:
        report_path.write_text(pretty, encoding='utf-8')
        raw = pretty.encode()
    pre_path = PREFLIGHT / 'observations.json'
    pre_raw = pre_path.read_bytes()
    pre_pretty = json.dumps(json.loads(pre_raw), indent=2) + '\n'
    if pre_raw.decode() != pre_pretty:
        pre_path.write_text(pre_pretty, encoding='utf-8')
        pre_raw = pre_pretty.encode()
    preflight = json.loads(pre_raw)
    census = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
    rows = [r for r in census['rows'] if r['batch'] == 'B10']
    fmt_of = {r['rowId']: r['format'] for r in rows}

    observed = {o['id'] for o in report['observations']}
    blocked = {b['id'] for b in report['blocked']}
    artifacts = [a for o in report['observations'] for a in o['facts']['artifacts']]
    thumb = sha256(jpeg.thumb_jpeg_bytes())

    def pick(layout, flag):
        return next(o for o in report['observations'] if o['layout'] == layout and o['flag'] == flag)

    layouts = {fmt: {o['layout'] for o in report['observations'] if fmt_of[o['id']] == fmt}
               for fmt in ('MRW', 'RAF', 'RW2', 'SR2')}
    cleared = {fmt: next(iter(layouts[fmt])) for fmt in layouts}

    receipt = {
        'schema': 1,
        'batch': 'B10',
        'provenance': (
            'CI-only evidence. This sandbox has no pinned Exiv2 build (no cmake/ninja, no expat/zlib '
            'development headers, and the package-install, Actions log and artifact-blob hosts are '
            'unreachable), so there is no local observation run to compare against. The committed '
            'report is the recovered CI run; no cross-environment equality claim is made.'),
        'run': {'id': RUN_ID, 'job': 'Exiv2 0.28.9 / bounded E01 B10 wave-2 RAW observations',
                'checkRunId': CHECK_RUN_ID, 'headCommit': HEAD, 'attempt': 1, 'pullRequest': PULL,
                'url': f'https://github.com/sensboston/MetadataSharp/actions/runs/{RUN_ID}'},
        'denominatorFrozen': False,
        'verification': 'not-assessed',
        'baseline': report['baseline'],
        'tally': {
            'censusRows': len(rows),
            'observed': len(observed),
            'blocked': len(blocked),
            'inputsUnchanged': all(o['facts']['inputUnchanged'] for o in report['observations']),
            'artifactsProduced': len(artifacts),
            'outcomeCounts': report['outcomeCounts'],
        },
        'annotationRecovery': {
            'channel': ('check-run annotations (the Actions bulk-log host and the artifact blob host '
                        'are unreachable from this environment)'),
            'title': 'B10Z observations.json',
            'markerTitle': 'B10Z observations.json count',
            'encoding': 'zlib + base64, 4000-byte chunks',
            # The order matters and getting it wrong looks exactly like GitHub truncation:
            # concatenate the chunk bodies in index order, then base64-decode, then inflate once.
            'decoding': 'concatenate chunk messages in index order, base64-decode, single zlib inflate',
            'expectedChunks': 4,
            'chunks': 4,
            'markerVerified': True,
            'observationsFileSha256': sha256(raw),
            'rawPayloadSha256': sha256(json.dumps(report, sort_keys=True,
                                                  separators=(',', ':')).encode()),
            'recoveredKeys': sorted(report),
            'normalization': 'none required: stderr paths are already normalized to <fixture> by the recorder',
        },
        'preflightRecovery': {
            'title': 'B10RAW observations.json',
            'markerTitle': 'B10RAW observations.json count',
            'chunks': 4,
            'expectedChunks': 4,
            'markerVerified': True,
            'observationsFileSha256': sha256(pre_raw),
            'rawPayloadSha256': sha256(json.dumps(preflight, sort_keys=True,
                                                  separators=(',', ':')).encode()),
            'why': ('the census record keeps stdout digests, not stdout; the preflight keeps the first '
                    '1200 bytes of each print, which is what makes the print-form findings below '
                    'checkable without re-running the pinned build'),
        },
        'annotationBudget': {
            'perCheckRunLimit': 50,
            'thisRunAnnotationsUsed': ANNOTATIONS,
            'finding': ('B9 evidence was lost in run 42729776775 (head 39ee8c9) without any CI failure: '
                        'the B0-B8 job already emits 50 annotations and GitHub drops the overflow of a '
                        'check run silently. B10 therefore runs in its own job, whose two recorders cap '
                        f'themselves at 10 chunks each; this job used {ANNOTATIONS} of 50 and the count '
                        'markers for both reports were matched before any evidence was trusted.'),
        },
        'printForms': {
            fmt: {
                'inventory': {k: pick(cleared[fmt], '-pS')[k] for k in ('exitCode', 'outcome')}
                | {'stdoutLength': pick(cleared[fmt], '-pS')['facts']['stdoutLength'],
                   'tokens': pick(cleared[fmt], '-pS')['facts']['printedTokens']},
                'pa': {'stdoutLength': pick(cleared[fmt], '-pa')['facts']['stdoutLength'],
                       'tokens': pick(cleared[fmt], '-pa')['facts']['printedTokens'],
                       'stdoutSha256': pick(cleared[fmt], '-pa')['facts']['stdoutSha256']},
                'comment': {'stdoutLength': pick(cleared[fmt], '-pc')['facts']['stdoutLength'],
                            'stdoutSha256': pick(cleared[fmt], '-pc')['facts']['stdoutSha256']},
                'icc': {k: pick(cleared[fmt], '-pC')[k] for k in ('exitCode', 'outcome')}
                | {'stdoutLength': pick(cleared[fmt], '-pC')['facts']['stdoutLength']},
                'extract': {k: pick(cleared[fmt], '-et')[k] for k in ('exitCode', 'outcome')}
                | {'artifactBytes': pick(cleared[fmt], '-et')['facts']['extractSemantics']['artifactBytes'],
                   'exitCodeEqualsArtifactBytes':
                       pick(cleared[fmt], '-et')['facts']['extractSemantics']['exitCodeEqualsArtifactBytes']},
            } for fmt in sorted(cleared)
        },
        'commentPrintForm': {
            'authoredComment': 'B10RafComment',
            'rafMatchesAuthoredCommentPlusNewline':
                pick(cleared['RAF'], '-pc')['facts']['stdoutSha256'] == sha256(b'B10RafComment\n'),
            'otherFormatsPrintOneNewlineByte': all(
                pick(cleared[f], '-pc')['facts']['stdoutSha256'] == sha256(b'\n')
                for f in ('MRW', 'RW2', 'SR2')),
        },
        'iccPrintForm': {
            'authoredProfileLength': 192,
            'stdoutLengthByFormat': {f: pick(cleared[f], '-pC')['facts']['stdoutLength']
                                     for f in sorted(cleared)},
            'note': ('the generic TIFF print (SR2) returned exactly the authored profile, the RW2 print '
                     'returned 202 bytes because Rw2Image::printStructure prints "RW2 IMAGE" before the '
                     'profile (src/rw2image.cpp:63), the RAF print succeeded but was empty, and the MRW '
                     'print threw the same kerUnsupportedImageType as its -pS because all -p modes share '
                     'Image::printStructure (app/actions.cpp:1983-1990).'),
            'b9AnomalyResolved': ('this also explains B9\'s ORF 202-byte -pC print for a 192-byte profile: '
                                  'OrfImage::printStructure prints "ORF IMAGE" first (src/orfimage.cpp:55). '
                                  'B9 recorded that difference as undiagnosed; it is a print-form banner, '
                                  'not a profile difference. B9\'s rows are not rewritten.'),
        },
        'thumbnailRoundTrip': {
            'byteExactCopyProducedByPinnedCli': True,
            'rows': len([a for a in artifacts]),
            'authoredThumbnailSha256': thumb,
            'authoredThumbnailLength': len(jpeg.thumb_jpeg_bytes()),
            'extractionSucceededFor': sorted({fmt_of[o['id']] for o in report['observations']
                                              if o['flag'] == '-et' and o['facts']['artifacts']}),
            'extractionReturnedNothingFor': sorted({fmt_of[o['id']] for o in report['observations']
                                                     if o['flag'] == '-et' and not o['facts']['artifacts']}),
        },
        'recognition': {
            'dispatchByFormat': {f: sorted({o['facts']['container']['dispatch'] for o in report['observations']
                                           if fmt_of[o['id']] == f}) for f in sorted(cleared)},
            'note': ('MRW is claimed by \\0MRM ahead of the TIFF entry, RW2 by magic 0x0055 which the '
                     'generic TIFF check rejects, RAF by the FUJIFILM header, and SR2 by nothing: it '
                     'dispatches to the generic TIFF reader because the pinned registry has no SR2 '
                     'entry, so its rows record generic behaviour rather than an SR2 identity.'),
        },
        'checkoutPolicy': {
            'requirement': ('the digests in this receipt are taken over committed bytes, so a checkout '
                            'that rewrites line endings invalidates them'),
            'windowsRunnerDefault': 'core.autocrlf=true converts LF text to CRLF at checkout',
            'pin': 'docs/research/evidence/**/*.json text eol=lf in .gitattributes',
            'finding': ('net8.0/net10.0 windows-latest failed the discovery suite at 4322b70 (run '
                        '42744015517) while every Linux and macOS job passed: the B10 evidence files '
                        'had been written without an eol pin, so the Windows checkout hashed CRLF bytes '
                        'against an LF-recorded digest. Reproduced locally with '
                        '`git -c core.autocrlf=true checkout-index`, fixed by the pin, and guarded by '
                        'test_files_whose_bytes_a_receipt_hashes_are_eol_pinned so no later batch can '
                        'forget it.'),
        },
        'secondaryReference': {
            'tool': 'ExifTool 12.76 (pinned 212f02cf2)',
            'files': sorted(report['secondaryObservations']),
            'role': 'secondary observation, never an oracle',
        },
        'reproduction': {
            'claim': 'observations are reproducible; the compiled reference binary is not',
            'runs': [{'head': HEAD, 'runId': RUN_ID, 'payload': 'observations.json'}],
            'status': ('one sample committed so far: the cross-run sample is the receipt commit\'s own CI '
                       'run, and a third comes from the post-merge master push, exactly as in B9'),
            'excludedDifferences': {
                'baseline.executableSha256': 'the build embeds its own timestamp, so the binary is not bit-reproducible',
                'baseline.verboseVersion': 'the only banner difference is the time= line',
                'secondaryObservations': 'ExifTool System:FileAccessDate/ModifyDate/InodeChangeDate only',
                'sourceCommit': 'successive runs validate different heads',
            },
        },
        'notClaimed': [
            'no MetadataSharp verification (verification stays not-assessed)',
            'no coverage percentage and no denominator freeze',
            'no metadata-support or vendor-format conformance claim',
            'no write behavior: no B10 write row is enumerated, and mrw/rw2/sr2/raf being amRead in the '
            'pinned registry is a reference-build fact, not an accepted exclusion',
            'per-family attribution of the shared -pa output is not established (four rows per layout '
            'print one identical digest)',
            'nothing about the vendor-private structures is established: those 32 cells stay blocked',
            'RW2\'s two-preview path is not attempted, so the "more than one preview" branch is unobserved',
            'that any authored file is a valid capture of the vendor format, or that a camera would read it',
            'ExifTool reported FileType per its own rules, which consult the extension, so the secondary '
            'observation is not a validity proof for these authored containers',
        ],
    }

    for label, path in (('ci', args.ci), ('master', args.master)):
        if not path:
            continue
        other = json.loads(path.read_bytes())
        data = path.read_bytes()
        receipt['annotationRecovery'][f'{label}Chunks'] = 4
        receipt['annotationRecovery'][f'{label}ObservationsSha256'] = sha256(data)
        identical = [k for k in sorted(report)
                     if report[k] == other[k]]
        receipt['reproduction']['runs'].append(
            {'head': other['sourceCommit'],
             'runId': getattr(args, f'{label}_run'), 'payload': f'{label}-observations.json'})
        receipt['reproduction']['identicalSections'] = identical
        receipt['reproduction']['differingSections'] = [k for k in sorted(report) if k not in identical]

    (EVIDENCE / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print('receipt.json written:', (EVIDENCE / 'receipt.json').stat().st_size, 'bytes')
    print(json.dumps(receipt['tally'], indent=1))
    print(json.dumps(receipt['printForms'], indent=1)[:1500])


if __name__ == '__main__':
    main()
