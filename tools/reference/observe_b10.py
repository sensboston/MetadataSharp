"""B10 read-only pinned CLI probes for the cleared owned wave-2 RAW containers.

One census row is exactly one pinned-CLI command per owned fixture, so a recognition
row and a metadata row never share a command. Outcomes are recorded, never gated: a
refusal is a finding, not a support claim, and an empty print only proves that this
CLI build printed nothing for this fixture. Each vendor-private layout stays blocked
instead of refused because no fixture is cleared for it
(docs/research/E01_B10_RIGHTS_AND_ORACLE_REVIEW.md).

These fixtures are authored containers, not camera captures: each carries the header
contract the pinned build dispatches on plus the shared owned metadata payloads, so a
refusal says nothing about the vendor layout it stands in for and a success says nothing
about real RAW decoding. No pixels are decoded, nothing is written back to a fixture,
and no MakerNote is interpreted. This is bounded census evidence, not E10 product
verification.

Two things are recorded here that earlier batches could not know:

* `-pS` on MRW reaches ``Image::printStructure`` (src/image.cpp:136), which throws
  ``kerUnsupportedImageType``: an honest per-format inventory refusal, not a fixture
  fault, so the outcome is recorded and the row stays observed rather than dropped.
* `-et`'s exit code in this build is **the number of bytes written**, not a status
  (``app/actions.cpp:858`` -> ``src/exif.cpp:389`` -> ``src/basicio.cpp:1757``), and the
  CLI prints "doesn't contain a thumbnail" only when that count is 0. So a nonzero `-et`
  exit with an artifact is the *success* shape (batch B0 recorded 137/46 the same way),
  and `rc == artifact bytes` is asserted per row rather than assumed. A `-et` that exits
  0 and produces nothing is recorded as `zero-exit-no-artifact`, never as `accepted`.
"""
import argparse
import base64
from collections import Counter
import json
import os
from pathlib import Path
import re
import shutil
import zlib

import b10_raw_fixtures as catalog
import b7_exiftool_probe as secondary
import exiv2_baseline as baseline
import observe_b0 as common
from observe_b9 import verify_source

ROOT = Path(__file__).resolve().parents[2]
FLAGS = {'inventory': ['-pS'], 'comments': ['-pc'], 'icc': ['-pC']}
FORMATS = ('MRW', 'RAF', 'RW2', 'SR2')
# Per-format reasons a vendor-private cell cannot be attempted, kept next to the probe so
# the blocked reason travels with the evidence.
BLOCK_REASONS = {fmt: catalog.vendor_reason(fmt) for fmt in FORMATS}
FACT_KEYS = ('dispatch', 'byteOrder', 'fileLength', 'fileSha256', 'trailingPadBytes',
             'lastAccountedByte')
EMBED_KEYS = ('rootOffset', 'magicTag', 'directoryOrder', 'compression', 'make', 'model',
              'carriers', 'thumbnailPresent', 'dataPoolSpans', 'blobLength', 'blobSha256')
BLOCK_KEYS = {'MRW': ('blockBound', 'blockTags', 'ttwAt', 'ttwLength', 'coversFile'),
              'RAF': ('jpegOffset', 'jpegLength', 'cfaOffset', 'cfaLength', 'tiffOffset',
                      'tiffLength', 'printedText', 'unknownSha256'),
              'RW2': ('magic', 'rootOffset', 'identityField', 'headerBytes')}
# The accepted-by map is the dispatch evidence this batch exists to collect: RW2 must be
# rejected by the generic TIFF checker, and SR2 must be accepted by nothing else.
ACCEPT_KEYS = ('mrw', 'tiff', 'rw2', 'raf')


def flags_for(row):
    """One read-only flag per row; extraction is asked for explicitly."""
    if row['family'] == 'inventory':
        return FLAGS['inventory']
    if row['operation'] == 'extract':
        return ['-et']
    return FLAGS.get(row['family'], ['-pa'])


def fixture_for(row):
    return catalog.DIRECTORY / catalog.FIXTURES[row['layout']][1]


def container_facts(data, fmt):
    facts = catalog.structure(fmt, data)
    out = {key: facts[key] for key in FACT_KEYS if key in facts}
    out['acceptedBy'] = {name: facts['recognizers'][name]['accepted'] for name in ACCEPT_KEYS}
    embedded = facts['embedded']
    out['embedded'] = {key: embedded[key] for key in EMBED_KEYS if key in embedded}
    block = facts.get(fmt.lower())
    if block:
        out[fmt.lower()] = {key: block[key] for key in BLOCK_KEYS.get(fmt, ()) if key in block}
    if fmt == 'RAF' and 'preview' in facts:
        out['raf']['preview'] = {key: value for key, value in facts['preview'].items()
                                 if key != 'tiff'}
    return out


def extract_semantics(flag, rc, artifacts, stderr):
    """What `-et` means in this build: the status is a byte count, not a success flag."""
    if flag != '-et':
        return None
    written = sum(item['length'] for item in artifacts)
    return {'exitCodeIsByteCountInPinnedBuild': True,
            'artifactBytes': written,
            'exitCodeEqualsArtifactBytes': rc == written,
            'reportedNoThumbnail': 'thumbnail' in stderr.lower()}


def observe_row(row, exe, work):
    if catalog.FIXTURES[row['layout']][0] != row['format']:
        raise ValueError(f"census layout {row['layout']} and format {row['format']} disagree")
    source = fixture_for(row)
    original = source.read_bytes()
    # Keep the owned extension: the pinned build dispatches on header bytes, and the
    # recorded exit codes are only interpretable for a file named as its format.
    target = work / ('case' + source.suffix)
    target.write_bytes(original)
    flags = flags_for(row)
    commands = {'commands': []}  # throwaway sink: soft_run's stdout excerpt stays out of the report
    rc, stdout, stderr = common.soft_run(commands, [exe, *flags, target], cwd=work)
    after = target.read_bytes()
    artifacts = [{'file': p.name, 'length': p.stat().st_size,
                  'sha256': baseline.digest(p.read_bytes())}
                 for p in sorted(work.iterdir()) if p.is_file() and p != target]
    text = stdout.decode('utf-8', 'replace')
    err = re.sub(r'\S*/case\.[A-Za-z0-9]+', '<fixture>', stderr.decode('utf-8', 'replace'))[:300]
    semantics = extract_semantics(flags[0], rc, artifacts, err)
    # A read-only probe that mutated the input is a finding even if the CLI claimed success.
    if semantics is None:
        outcome = ('scan-changed' if original != after else
                   common.classify(rc, True, None, True, artifact_produced=bool(artifacts)))
    elif original != after:
        outcome = 'scan-changed'
    elif rc == 0:
        # An extract row is judged by the byte-count rule rather than by accepted/refused:
        # in this build the status is what the CLI wrote, so 0 with no artifact means "this
        # file has no thumbnail" -- not a success, and not a refusal either.
        outcome = 'zero-exit-artifact-produced' if artifacts else 'zero-exit-no-artifact'
    else:
        outcome = 'nonzero-exit-artifact-produced' if artifacts else 'refused'
    if semantics is not None and not semantics['exitCodeEqualsArtifactBytes']:
        # The one shape this build must not produce: a status that is not the byte count.
        result_finding = (f'-et exit code {rc} does not equal the {semantics["artifactBytes"]} '
                          'bytes the command wrote')
    else:
        result_finding = None
    return {'id': row['rowId'], 'seedId': f"EX0289-{row['format']}-{row['family'].upper()}",
            'operation': row['operation'], 'family': row['family'], 'layout': row['layout'],
            'flag': flags[0], 'exitCode': rc,
            'duplicates': row['duplicates'], 'textEncoding': row['textEncoding'],
            'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'fixtureRights': 'owned-synthetic', 'inputSha256': baseline.digest(original),
            'outputSha256': baseline.digest(after), 'recordOnly': True, 'expectOk': None,
            'outcome': outcome,
            'facts': {'finding': result_finding,
                      'inputUnchanged': original == after,
                      'stdoutSha256': baseline.digest(stdout), 'stdoutLength': len(stdout),
                      'stderr': err, 'artifacts': artifacts,
                      'container': container_facts(original, row['format']),
                      'printedTokens': sorted(token for token in catalog.TOKENS if token in text),
                      'extractSemantics': semantics}}


def chunks(report, size=4000, limit=10):
    """Compress the report into check-run annotations, the only retrievable channel.

    Bulk Actions logs are not reachable from this environment, so a report that would
    need more annotations than the limit is a hard error instead of silent truncation:
    the evidence must be recoverable whole or the batch is not recorded.
    """
    if not report.get('observations') and not report.get('blocked'):
        raise ValueError('B10 report carries neither observations nor blocked cells')
    payload = json.dumps(report, separators=(',', ':'), sort_keys=True)
    encoded = base64.b64encode(zlib.compress(payload.encode(), 9)).decode()
    parts = [encoded[i:i + size] for i in range(0, len(encoded), size)]
    if not parts:
        raise ValueError('B10 report is empty; there would be nothing to recover')
    if len(parts) > limit:
        raise ValueError(f'B10 report needs {len(parts)} annotations; the recovery limit is {limit}')
    return parts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--exiftool-checkout', type=Path, required=True)
    parser.add_argument('--census', type=Path, default=ROOT / 'docs/research/EXIV2_CENSUS.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01')
    args = parser.parse_args()
    upstream, build = args.upstream.resolve(), args.build.resolve()
    out = (args.output / 'b10').resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'batch': 'B10',
              'scope': 'read-only pinned CLI probes of owned synthetic wave-2 RAW containers '
                       '(MRW block chain with an embedded TTW TIFF, RAF big-endian offset table '
                       'with a preview JPEG and a camera-settings TIFF, RW2 magic 0x0055 with root '
                       'IFD 24, and a generic TIFF marker recording the absence of an SR2-specific '
                       'observable) and blocked vendor-private cells; no product verification, no '
                       'support claim, no vendor layout conformance claim',
              'limitations': 'fixtures are authored containers, not camera captures: they carry no '
                             'vendor private raw data, no MakerNote and no real preview, so a '
                             'refusal is not evidence that a vendor layout is unsupported and a '
                             'success is not evidence of RAW support; the MRW and RAF embedded blocks '
                             'are parsed by the generic TiffParser, so tag naming follows the TIFF '
                             'mapping and not any vendor tag list; the pinned build has no SR2 reader '
                             'and no SR2 entry in TiffImage::mimeType, so SR2 rows record absence '
                             'rather than presence; the RW2 preview branch is exercised only through '
                             'the single authored preview, so the "more than one preview" warning '
                             'path is not attempted in this batch; no B10 write suboperation is '
                             'enumerated because the pinned registry marks mrw/rw2/sr2/raf amRead; '
                             'ExifTool 12.76 is a secondary reference, not an oracle.',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'),
              'baseline': {'version': baseline.VERSION, 'sourceCommit': baseline.PIN},
              'denominatorFrozen': False, 'verification': 'not-assessed',
              'observations': [], 'blocked': [], 'secondaryObservations': {},
              'outcomeCounts': {}}

    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing E01_SOURCE_COMMIT')
        exe = verify_source(upstream, build, report)
        report['fixtureManifest'] = catalog.verify()
        rows = [r for r in json.loads(args.census.read_text())['rows'] if r['batch'] == 'B10']
        if len(rows) != 64:
            raise ValueError(f'unexpected B10 census row count: {len(rows)}')
        for i, row in enumerate(rows):
            if row['layout'] not in catalog.FIXTURES:
                report['blocked'].append({'id': row['rowId'],
                                          'reason': BLOCK_REASONS[row['format']]})
                save()
                continue
            work = out / 'work' / f'{i:04d}'
            if work.exists():
                shutil.rmtree(work)
            work.mkdir(parents=True)
            report['observations'].append(observe_row(row, exe, work))
            save()
        if len({o['id'] for o in report['observations']}) != len(report['observations']):
            raise ValueError('duplicate B10 observation id')
        if {o['id'] for o in report['observations']} & {b['id'] for b in report['blocked']}:
            raise ValueError('B10 row both observed and blocked')
        if {b['id'] for b in report['blocked']} != {r['rowId'] for r in rows
                                                    if r['layout'] not in catalog.FIXTURES}:
            raise ValueError('B10 blocked set does not match the unobserved layouts')
        used = sorted({o['fixture'] for o in report['observations']})
        if set(used) != {entry['file'] for entry in report['fixtureManifest']}:
            raise ValueError('a cleared B10 fixture was never probed')
        # Every extract row must show the byte-count semantics; a mismatch means the
        # pinned build stopped matching the source this batch read, and must be visible.
        for item in report['observations']:
            semantics = item['facts']['extractSemantics']
            if semantics is not None and not semantics['exitCodeEqualsArtifactBytes']:
                item['facts']['extractSemantics']['finding'] = (
                    'exit code differs from the bytes written; see app/actions.cpp:858')
        for name in used:
            report['secondaryObservations'][name] = secondary.observe(
                args.exiftool_checkout, catalog.DIRECTORY / name)
        counts = Counter(o['outcome'] for o in report['observations'])
        counts['blocked-rights'] = len(report['blocked'])
        counts['static-not-applicable'] = 0
        report['outcomeCounts'] = dict(sorted(counts.items()))
        report['status'] = 'passed'
        save()
        parts = chunks(report)
        for i, part in enumerate(parts):
            print(f'::notice title=B10Z observations.json {i}/{len(parts)}::{part}', flush=True)
        # Recovery marker, emitted last: a check run keeps only 50 annotations, so the
        # recovery side must confirm it saw exactly `len(parts)` chunks (and this line)
        # before treating the evidence as whole. A silent drop becomes a mismatch.
        print(f'::notice title=B10Z observations.json count::{len(parts)}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
