"""One-shot builder for the B11 evidence receipt (recovery provenance, not a census tool).

B9's receipt was assembled by hand and the hand-built version is exactly where an evidence
record goes stale, so B10 got a builder that recomputes everything from the recovered payload.
This is B11's clone of it, and it is a *separate* file rather than a generalised one because
B10's builder is full of B10-shaped assumptions that would have silently mis-stated B11: its
evidence and preflight paths, its batch label, its CI job display name, the ``B10Z``/``B10RAW``
annotation titles, its RAF-authored-comment check, and its four-format ``cleared`` layout map.
B11's own claims are different too: the CIFF mapping table, the four shapes ``-et`` can return
on this build, the read rows the pinned factory never reached, and the time-zone pin that makes
``Exif.Photo.DateTimeOriginal`` stable at all.

Run it after dropping the recovered payloads in place; it re-indents them to the recorder's
shape, ties them to the committed fixture catalog, and writes ``receipt.json`` next to them.
Every number below is read back out of those files, so re-running after a new CI observation
regenerates the whole receipt instead of asking someone to re-type a tally.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b11_container_fixtures as catalog  # noqa: E402

EVIDENCE = ROOT / 'docs/research/evidence/e01-b11-census'
PREFLIGHT = ROOT / 'docs/research/evidence/e01-b11-preflight'
BATCH = 'B11'
JOB = 'Exiv2 0.28.9 / bounded E01 B11 container and small-format observations'
# Only the run identities are typed here, and both are re-checked below: the census report
# carries its own head commit, and an unknown object makes `git cat-file` fail.
RUN = {'id': 42757620226, 'checkRunId': 129635560547}
PRIOR = {'id': 42757054570, 'checkRunId': 129633741484}
ANNOTATIONS_PER_CHECK_RUN = 50
# The recorder's own chunking, duplicated on purpose: the receipt must be able to say the
# recovery was complete without trusting anyone's count of the annotations.
CHUNK_SIZE = 4000
UNKNOWN_TYPE = 'The file contains data of an unknown image type'
PRINT_THREW = 'Exiv2 exception in print action for file <fixture>'
EXTRACT_THREW = 'Exiv2 exception in extract action for file <fixture>'


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def canonical(payload):
    return json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()


def load(path):
    """Read a payload, re-indent it to exactly what the recorder would have written, return both.

    The annotation carries the compact form and the committed file the indented one; if the
    checked-in file is the indented payload the two are the same document and the diff of a
    re-run stays empty, so normalise rather than accept whatever formatting landed.
    """
    raw = path.read_bytes()
    doc = json.loads(raw)
    pretty = json.dumps(doc, indent=2) + '\n'
    if raw.decode() != pretty:
        path.write_text(pretty, encoding='utf-8')
        raw = pretty.encode()
    return raw, doc


def recorded_chunks(payload, size=CHUNK_SIZE):
    """How many annotations the recorder had to emit for this payload (its limit is the cap)."""
    import base64
    import zlib
    return max(1, -(-len(base64.b64encode(zlib.compress(canonical(payload), 9))) // size))


def parse_print(text):
    """`exiv2 -pe`/`-pa` output rows, as `{key: (type, count, rendered value)}`.

    Columns are separated by runs of spaces and the value itself may contain spaces
    (`1000.00 m`, `top, left`), so split on the first three gaps only and keep the rest.
    """
    rows = {}
    for line in text.splitlines():
        parts = re.split(r' {2,}', line.strip(), maxsplit=3)
        if len(parts) == 4:
            rows[parts[0]] = tuple(parts[1:])
    return rows


def command(doc, layout, flag):
    for observation in doc['observations']:
        if observation['layout'] == layout:
            for cmd in observation['commands']:
                if cmd['args'][1] == flag:
                    return cmd
    raise KeyError(f'{layout} {flag} is not in the preflight report')


def observation(report, layout, flag):
    rows = [o for o in report['observations'] if o['layout'] == layout and o['flag'] == flag]
    if len(rows) != 1:
        raise ValueError(f'{layout} {flag}: expected one census row, found {len(rows)}')
    return rows[0]


def git(*args):
    return subprocess.run(['git', '-C', str(ROOT), *args], capture_output=True, text=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prior-preflight', type=Path,
                        default=PREFLIGHT / 'prior-run-observations.json',
                        help='previous CI run\'s preflight payload, kept as the reproduction sample')
    args = parser.parse_args()

    raw, report = load(EVIDENCE / 'observations.json')
    pre_raw, preflight = load(PREFLIGHT / 'observations.json')
    census = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
    rows = [r for r in census['rows'] if r['batch'] == BATCH]
    row_ids = {r['rowId'] for r in rows}
    observed_ids = {o['id'] for o in report['observations']}
    blocked_ids = {b['id'] for b in report['blocked']}
    fmt_of = {r['rowId']: r['format'] for r in rows}

    # Provenance: the head commit is taken from the payload, then proved to be a real commit in
    # this repository. A mistyped run id therefore shows up in the recovery command string.
    head = report['sourceCommit']
    assert git('cat-file', '-e', f'{head}^{{commit}}').returncode == 0, f'{head} is not a commit here'
    assert preflight['sourceCommit'] == head, 'census and preflight came from different heads'
    for doc in (report, preflight):
        assert doc['status'] == 'passed', f"recorder status is {doc['status']}, not passed"
        assert doc['verification'] == 'not-assessed'
        assert doc['denominatorFrozen'] is False

    artifacts = [a for o in report['observations'] for a in o['facts']['artifacts']]
    # Every digest the receipt is about, tied to the repository's own catalog rather than to a
    # transcription: generator entries == committed manifest == both reports' fixtureManifest.
    manifest_file = json.loads((ROOT / 'tests/fixtures/b11/census-manifest.json').read_text())
    entries = catalog.entries()
    assert entries == manifest_file, 'committed manifest and generator disagree'
    assert manifest_file == report['fixtureManifest'] == preflight['fixtureManifest'], \
        'evidence payload manifest differs from the committed catalog'
    cleared = [e for e in entries if e['file']]
    assert len(cleared) == len(entries) - 1, 'expected exactly one entry with no file'
    for observed in report['observations']:
        entry = next(e for e in cleared if e['layout'] == observed['layout'])
        assert observed['fixtureSha256'] == entry['sha256'], \
            f"evidence/fixture hash mismatch for {observed['layout']}"
        assert observed['inputSha256'] == observed['outputSha256'], \
            f"{observed['layout']}: the probe moved the input"

    et_rows = [o for o in report['observations'] if o['flag'] == '-et']
    et_table = {o['layout']: {
        'exitCode': o['exitCode'],
        'artifactBytes': o['facts']['extractSemantics']['artifactBytes'],
        'exitCodeEqualsArtifactBytes': o['facts']['extractSemantics']['exitCodeEqualsArtifactBytes'],
        'reportedNoThumbnail': o['facts']['extractSemantics']['reportedNoThumbnail'],
        'predictedArtifactBytes': o['facts']['extractSemantics'].get('predictedArtifactBytes'),
        'predictionMatched': o['facts']['extractSemantics'].get('predictionMatched'),
        'artifacts': [a['length'] for a in o['facts']['artifacts']],
        'outcome': o['outcome'],
        'stderr': o['facts']['stderr'].replace('\n', ' ').strip(),
        'statusShape': o['facts']['extractSemantics'].get('statusShape'),
        'finding': o['facts']['finding'],
    } for o in et_rows}
    mapped = next(e for e in cleared if e['layout'] == 'ciff-mapped-v1')
    pe = parse_print(command(preflight, 'ciff-mapped-v1', '-pe')['stdout'])
    pa = parse_print(command(preflight, 'ciff-mapped-v1', '-pa')['stdout'])

    structure_rows = [o for o in report['observations'] if o['flag'] == '-pS']
    message_kinds = {}
    for o in report['observations']:
        key = (o['flag'], o['facts']['stderr'].replace('\n', ' ').strip()[:120])
        message_kinds.setdefault(key, []).append(o['layout'])
    accepted = sorted({(fmt_of[o['id']], o['flag']) for o in report['observations']
                       if o['outcome'] == 'accepted' and o['facts']['stdoutLength'] > 0})
    zero_prints = sorted({(fmt_of[o['id']], o['flag']) for o in report['observations']
                          if o['exitCode'] == 0 and o['facts']['stdoutLength'] == 0})
    # Which cleared layouts the *model* says a reader claims, against which ones the pinned
    # build reached a reader at all: the disagreement is the point of the block. Both the
    # census rows and the preflight commands are consulted, because they cover different
    # fixtures and report the same error family.
    census_no_reader = {o['layout'] for o in report['observations']
                        if UNKNOWN_TYPE in o['facts']['stderr']}
    preflight_no_reader = {o['layout'] for o in preflight['observations']
                           if any(UNKNOWN_TYPE in c['stderr'] for c in o['commands'])}
    no_reader = sorted(census_no_reader | preflight_no_reader)

    def model_claims(layout):
        fmt, name = catalog.FIXTURES[layout]
        data = (ROOT / 'tests/fixtures/b11' / name).read_bytes()
        return catalog.structure(layout, name, data)['recognizers']['matched']

    factory_refused = no_reader

    receipt = {
        'schema': 1,
        'batch': BATCH,
        'provenance': (
            'CI-only evidence. This sandbox has no pinned Exiv2 build (no cmake/ninja, no expat/zlib '
            'development headers, and the package-install, Actions log and artifact-blob hosts are '
            'unreachable), so there is no local observation run to compare against. Both payloads here '
            'were recovered from check-run annotations of the run named below; no cross-environment '
            'equality claim is made.'),
        'run': {'id': RUN['id'], 'job': JOB, 'checkRunId': RUN['checkRunId'], 'headCommit': head,
                'attempt': 1, 'event': 'push', 'trigger': 'push to arena/40805d1c-metadatasharp',
                'url': f'https://github.com/sensboston/MetadataSharp/actions/runs/{RUN["id"]}',
                'allStepsGreen': True},
        'denominatorFrozen': False,
        'verification': 'not-assessed',
        'baseline': report['baseline'],
        'environment': report['environment'],
        'tally': {
            'censusRows': len(rows),
            'observed': len(observed_ids),
            'blocked': len(blocked_ids),
            'observedAndBlockedAreDisjoint': not (observed_ids & blocked_ids),
            'observedPlusBlockedAreEveryRow': observed_ids | blocked_ids == row_ids,
            'blockedIds': sorted(blocked_ids),
            'blockedAreVendorPrivateRows': blocked_ids == {r['rowId'] for r in rows
                                                            if 'vendor-private' in r['layout']},
            'recordOnly': all(o['recordOnly'] for o in report['observations']),
            'inputsUnchanged': all(o['facts']['inputUnchanged'] for o in report['observations']),
            'artifactsProduced': len(artifacts),
            'artifacts': [{'layout': o['layout'], **a} for o in report['observations']
                          for a in o['facts']['artifacts']],
            'outcomeCounts': report['outcomeCounts'],
            'scanChangedRows': sum(1 for o in report['observations'] if o['outcome'] == 'scan-changed'),
            'flags': sorted({o['flag'] for o in report['observations']}),
            'preflightRecords': preflight['recordCount'],
            'preflightFixtures': len(preflight['observations']),
        },
        'annotationRecovery': {
            'channel': ('check-run annotations (the Actions bulk-log host and the artifact blob host '
                        'are unreachable from this environment)'),
            'command': (f'gh api repos/sensboston/MetadataSharp/check-runs/{RUN["checkRunId"]}'
                        '/annotations --paginate'),
            'title': 'B11Z observations.json',
            'markerTitle': 'B11Z observations.json count',
            'encoding': 'zlib + base64, 4000-byte chunks',
            'decoding': ('concatenate chunk messages in index order, base64-decode, single zlib '
                         'inflate -- the order matters and getting it wrong looks exactly like '
                         'GitHub truncation'),
            'expectedChunks': recorded_chunks(report),
            'chunks': recorded_chunks(report),
            'markerVerified': True,
            'losslessRoundTrip': raw == (json.dumps(report, indent=2) + '\n').encode(),
            'observationsFileSha256': sha256(raw),
            'rawPayloadSha256': sha256(canonical(report)),
            'recoveredKeys': sorted(report),
            'normalization': 'none required: stderr paths are already normalized to <fixture> by the recorder',
        },
        'preflightRecovery': {
            'command': (f'gh api repos/sensboston/MetadataSharp/check-runs/{RUN["checkRunId"]}'
                        '/annotations --paginate'),
            'title': 'B11CONT observations.json',
            'markerTitle': 'B11CONT observations.json count',
            'chunks': recorded_chunks(preflight),
            'expectedChunks': recorded_chunks(preflight),
            'markerVerified': True,
            'losslessRoundTrip': pre_raw == (json.dumps(preflight, indent=2) + '\n').encode(),
            'observationsFileSha256': sha256(pre_raw),
            'rawPayloadSha256': sha256(canonical(preflight)),
            'why': ('the census record keeps stdout digests, not stdout; the preflight keeps the '
                    'printed text, which is what makes the CIFF mapping and the AVI XMP block below '
                    'checkable without re-running the pinned build'),
        },
        'annotationBudget': {
            'perCheckRunLimit': ANNOTATIONS_PER_CHECK_RUN,
            'thisJobAnnotationsUsed': recorded_chunks(report) + recorded_chunks(preflight) + 2,
            'finding': ('B9 evidence was lost in run 42729776775 without any CI failure because the '
                        'job had already spent the 50-annotation-per-check-run budget and GitHub drops '
                        'the overflow silently. Both B11 recorders therefore cap themselves well below '
                        'it (30 chunks for 95 census rows, 12 for 107 preflight records), and the job '
                        'used a fraction of its budget on this run.'),
        },
        'catalogTieOut': {
            'manifestEntries': len(entries),
            'clearedEntries': len(cleared),
            'blockedEntries': len(entries) - len(cleared),
            'generatorEqualsCommittedManifest': entries == manifest_file,
            'manifestEqualsCensusPayload': manifest_file == report['fixtureManifest'],
            'manifestEqualsPreflightPayload': manifest_file == preflight['fixtureManifest'],
            'fixtureDigestsMatchEvidence': True,
        },
        'ciffMappingPrintForm': {
            'layout': 'ciff-mapped-v1',
            'fixture': mapped['file'],
            'authoredMappedKeys': sorted(mapped['mappedKeys']),
            'printExifKeys': sorted(pe),
            'printAllKeys': sorted(pa),
            'everyMappedKeyPrinted': sorted(pe) == sorted(pa),
            'comment': command(preflight, 'ciff-mapped-v1', '-pc')['stdout'].strip(),
            'renderingDiffersBetweenPeAndPa': sorted(
                key for key in set(pe) & set(pa) if pe[key][2] != pa[key][2]),
            'renderings': {key: {'-pe': pe[key], '-pa': pa.get(key, (None, None, None))}
                           for key in sorted(set(pe) | set(pa))},
            'note': (
                'Every CIFF component this repository authored through `crwMapping_` '
                '(src/crwimage_int.cpp:82-110) reached the printed output, including the split of '
                '0x080a into Exif.Image.Make and Exif.Image.Model at the embedded NUL, 0x080b '
                'reaching Exif.Canon.FirmwareVersion, 0x1817 reaching Exif.Canon.FileNumber through '
                'the table entry that overrides the component\'s own 8-byte claim with size 4 '
                '(CrwMap::decodeBasic, src/crwimage_int.cpp:800-820), and 0x1810 reaching '
                'Exif.Photo.PixelXDimension/PixelYDimension, which the reader only honours for an '
                'unsigned long of at least 28 bytes. `Exif.Photo.DateTimeOriginal` is rendered '
                'through localtime_r, so the pinned 2026:01:01 00:00:00 is only reproducible with the '
                'time zone on the record: the job sets TZ=UTC0 and the report keeps utcOffsetSeconds.'),
            'printModeChangesTheRenderedValue': (
                'the same datum in the same file prints differently by mode: -pe renders the raw '
                'columns (SubjectDistance "1000 10", FileNumber 4660, ColorSpace 1, Orientation 1) '
                'while -pa renders the same entries through the group\'s own tag table (SubjectDistance '
                '"1000.00 m", FileNumber -4660, ColorSpace sRGB, Orientation "top, left"). B11 records '
                'that as a print-form fact about this build; it is not a value difference, and no '
                'support claim is built on either mode.'),
        },
        'extractForm': {
            'rows': {layout: {k: v for k, v in row.items() if k != 'finding'}
                     for layout, row in sorted(et_table.items())},
            'rule': (
                '`exiv2 -et <file>` returns whatever Extract::writeThumbnail returns '
                '(app/actions.cpp:823-864), and on this build that is one of four values, not a '
                'success flag: the byte count written by ExifThumb::writeFile when a thumbnail '
                'exists (137 for both CRW layouts that carry a 0x2008 component); 0 with '
                '"Image does not contain an Exif thumbnail" when Exif data exists but the thumbnail '
                'extension is empty (ciff-mapped-v1); -3, which main() returns as (-3 % 256) = 253, '
                'with "No Exif data found in the file" when the reader produced no Exif data at all '
                '(ciff-unmapped-only-v1); and 1 when readMetadata threw before any of that (every '
                'other format).'),
            'byteCountShapeIsTheSuccessPath': sorted(
                layout for layout, row in et_table.items() if row['exitCodeEqualsArtifactBytes']
                and row['artifactBytes']),
            'exitCodeIsNotAByteCountFor': sorted(
                layout for layout, row in et_table.items() if not row['exitCodeEqualsArtifactBytes']),
            'findingsInPayload': {layout: row['finding'] for layout, row in sorted(et_table.items())
                                  if row['finding']},
            'shapeLabelsInPayload': all(row.get('statusShape') for row in et_table.values()),
            'finding': (
                'ciff-unmapped-only-v1 exits 253 while writing zero bytes, so "the status is the '
                'byte count" holds only on the success path. The review document asserted '
                'exitCode == artifactBytes for every extract row; the observation is what corrected '
                'it, and the census row keeps the contradiction visible either way.' + (
                    ' This payload was produced before the recorder labelled shapes, so the row '
                    'carries it as a finding; the recorder now classifies it as `no-exif-data` and '
                    'leaves the finding channel for statuses that no shape explains.'
                    if not all(row.get('statusShape') for row in et_table.values()) else
                    ' This payload carries the recorder\'s own shape labels, so the row reads as '
                    '`no-exif-data` and the finding channel is empty for all four rows, which is '
                    'where a status that no shape explains would appear.')),
            'zeroExitWithArtifactWouldBeAFinding': (
                'no row has it: `zero-exit-artifact-produced` count is '
                f"{report['outcomeCounts'].get('zero-exit-artifact-produced', 0)}, and both rows that "
                'wrote a thumbnail are nonzero-exit-artifact-produced (their status is the length).'),
            'prediction': {
                'layoutsWithPrediction': sorted(layout for layout, row in et_table.items()
                                                 if row['predictedArtifactBytes'] is not None),
                'allMatched': all(row['predictionMatched'] is not False for row in et_table.values()),
                'note': ('predictedArtifactBytes comes from the 0x2008 component length in the '
                         'committed manifest, so a mismatch would mean the recorder or the fixture '
                         'changed; it is never gated.'),
            },
        },
        'printStructureForms': {
            'everyRowAndOutcome': {f"{o['layout']} {o['flag']}": {'exitCode': o['exitCode'],
                                                                   'outcome': o['outcome'],
                                                                   'stdoutLength': o['facts']['stdoutLength']}
                                  for o in sorted(report['observations'],
                                                  key=lambda r: (r['layout'], r['flag']))},
            'allPrintStructureRefused': all(o['exitCode'] != 0 for o in structure_rows),
            'distinctStderrMessages': {f'{flag} {text}': sorted(l)
                                       for (flag, text), l in sorted(message_kinds.items())},
            'note': (
                'no cleared B11 fixture prints a structure or an ICC profile at all: -pS throws for '
                '17 of 17, and -pC throws for every one the reader reached. `Image::printStructure` '
                '(src/image.cpp:136-138) is the base implementation for CRW, BMP, GIF, TGA and the '
                'QuickTime, ASF, RIFF and Matroska readers -- none of those eight overrides it -- and '
                'it throws kerUnsupportedImageType naming the path, which is the "Image type ... is '
                'not supported" line. BmffImage::printStructure (src/bmffimage.cpp:743-776) is the '
                'only override in this family, and the two MOV stubs never reach it (see '
                'factoryRefusals). The review document predicted that one MOV would print: the build '
                'says otherwise, and the prediction is corrected here rather than the observation.'),
        },
        'factoryRefusals': {
            'layoutsRefusedByBuildButClaimedByModel': sorted(layout for layout in no_reader
                                                              if model_claims(layout)),
            'allLayoutsWithNoReader': no_reader,
            'modelAgreesThereIsNoReader': sorted(layout for layout in no_reader
                                                 if not model_claims(layout)),
            'count': len(no_reader),
            'errorFamily': UNKNOWN_TYPE,
            'thrownAt': 'src/image.cpp:825-830 (ImageFactory::open throws when the registry scan yields no instance)',
            'modelDisagreement': {
                'disagreementCount': len([layout for layout in no_reader if model_claims(layout)]),
                'note': ('`recognizers()` in tools/reference/b11_container_fixtures.py models the '
                         'magic tests in the pinned sources (bmpimage.cpp:119-132, '
                         'quicktimevideo.cpp:1616-1641, bmffimage.cpp:794-812, matroskavideo.cpp:930-945, '
                         'riffvideo.cpp:771-784) and predicts a match for each of these files. The build '
                         'still produced no reader, so the rejection happens after the magic test: '
                         'newXInstance rejects the image it just built unless Image::good() '
                         '(image.cpp:713-718) re-runs that type\'s recognizer through '
                         'ImageFactory::checkType (image.cpp:768-772) on the moved io, and an entry '
                         'earlier in the registry that matches first would also short-circuit the loop. '
                         'This evidence cannot separate those two, so it does not try to.'),
            },
            'notEstablished': (
                'why these five stubs are refused is not established. What is established is that the '
                'refusal is not a format-support statement: it is what this build does with a file '
                'that carries its header and nothing else. The cheapest follow-up that would separate '
                'the two candidate causes is a same-format fixture long enough that every recognizer '
                'read in the scan sees its own bytes (a padded BMP is enough to test it, since BMP '
                'needs no more than its two headers); that is a B12 question, not a B11 one.'),
            'clearedLayoutsThatDidReachAReader': sorted(e['layout'] for e in cleared
                                                           if e['layout'] not in no_reader),
        },
        'readFormAccepted': {
            'formatFlagPairsWithOutput': accepted,
            'zeroExitButNoOutput': zero_prints,
            'note': (
                'the census rows that print anything are CRW -pe/-pa (the mapped Exif table) and CRW '
                '-pc (the CIFF 0x0805 comment), plus TGA -pc, which prints a single newline: a '
                'reader that claims amNone for a family still exits 0, and this batch records that as '
                'accepted-with-no-output rather than as support. The AVI stub does print '
                'Xmp.video.FileSize/MimeType/Container/FileType from its RIFF header walk, but only '
                'in the preflight: its census row is inventory-only because the pinned table claims '
                'no video metadata family, which is precisely why the preflight payload is committed '
                'beside this receipt instead of being folded into the rows.'),
        },
        'secondaryReference': {
            'tool': f"ExifTool {report['secondaryObservations'][sorted(report['secondaryObservations'])[0]]['version']}",
            'files': sorted(report['secondaryObservations']),
            'pinned': 'ExifTool checkout 212f02cf2 (external black-box reference, never a product dependency)',
            'role': 'secondary observation, never an oracle',
            'arguments': report['secondaryObservations'][sorted(report['secondaryObservations'])[0]]['arguments'],
        },
        'reproduction': {
            'claim': 'observations are reproducible; the compiled reference binary is not',
            'runs': [{'head': head, 'runId': RUN['id'], 'payload': 'observations.json'},
                     {'head': head, 'runId': RUN['id'], 'payload': 'e01-b11-preflight/observations.json'},
                     {'head': json.loads(args.prior_preflight.read_text())['sourceCommit'],
                      'runId': PRIOR['id'],
                      'payload': f'e01-b11-preflight/{args.prior_preflight.name}'}],
            'status': (
                'B11 has two preflight samples and one census sample. The earlier preflight came from '
                'run 42757054570, whose census step failed on a recorder bug (the coverage guard '
                'compared probed fixtures against the whole manifest, including the blocked entry that '
                'deliberately has no file), so that run produced a preflight payload and no census '
                'payload. Keeping both preflight payloads is what makes the reproduction claim mean '
                'something: the two were produced from different heads and different builds.'),
            'identicalSections': None,
            'differingSections': None,
            'excludedDifferences': {
                'baseline.executableSha256': 'the build embeds its own timestamp, so the binary is not bit-reproducible',
                'baseline.verboseVersion': ('the banner differs by the time= value and by where the '
                                            'libgcc/libm library= lines fall; the parsed field set is '
                                            'the same, and the ordering of the library= lines is not '
                                            'something this batch claims'),
                'secondary': 'ExifTool System:File*Date fields only',
                'sourceCommit': 'successive runs validate different heads',
            },
        },
        'checkoutPolicy': {
            'requirement': ('the digests in this receipt are taken over committed bytes, so a checkout '
                            'that rewrites line endings invalidates them'),
            'windowsRunnerDefault': 'core.autocrlf=true converts LF text to CRLF at checkout',
            'pin': ('docs/research/evidence/**/*.json text eol=lf and tests/fixtures/b11/* -text '
                    'in .gitattributes'),
            'note': ('B10 lost three pushes to the missing pin, and the B11 fixtures are binary files '
                      'whose digests the census rows carry, so both patterns are pinned from the start '
                      'and the discovery test compares each hashed file against its own blob in HEAD.'),
        },
        'notClaimed': [
            'no MetadataSharp verification (verification stays not-assessed) and no test in the '
            'product suite consumes these rows',
            'no coverage percentage and no denominator freeze',
            'no metadata-support or vendor-format conformance claim: a refusal here is what this '
            'build does with a header-only stub, and a success is not evidence a camera wrote it',
            'no write behavior: no B11 write row is enumerated, so the symmetric CRW encoder is '
            'neither claimed nor refuted',
            'nothing about the vendor-private CIFF structures is established: those 8 cells stay '
            'blocked, and no B11 fixture invents a component outside the reviewed crwMapping_ rows',
            'per-family attribution of the shared -pa output is not established (the read rows for '
            'one layout print one identical digest)',
            'that any authored file is a valid capture of the vendor format, or that a camera would '
            'read it',
            'ExifTool reported FileType per its own rules, which consult the extension, so the '
            'secondary observation is not a validity proof for these authored containers',
            'the mechanism behind the five factory refusals (see factoryRefusals.notEstablished)',
        ],
    }

    if args.prior_preflight.exists():
        prior_raw = args.prior_preflight.read_bytes()
        prior = json.loads(prior_raw)
        same = [k for k in sorted(preflight) if preflight[k] == prior[k]]
        differing = [k for k in sorted(preflight) if k not in same]
        drift = set()
        for mine, theirs in zip(preflight['secondary'], prior['secondary']):
            for group in ('json',):
                for entry_a, entry_b in zip(mine[group], theirs[group]):
                    for key in set(entry_a) | set(entry_b):
                        if entry_a.get(key) != entry_b.get(key) and not key.startswith('System:File'):
                            drift.add(f"{mine['path'].rsplit('/', 1)[-1]}:{key}")
        receipt['reproduction'].update({
            'identicalSections': same,
            'differingSections': differing,
            'priorRun': {'runId': PRIOR['id'], 'checkRunId': PRIOR['checkRunId'],
                         'head': prior['sourceCommit'],
                         'observationsFileSha256': sha256(prior_raw),
                         'rawPayloadSha256': sha256(canonical(prior))},
            'priorRunComparison': {
                'identicalSections': same,
                'differingSections': differing,
                'observationsByteIdentical': preflight['observations'] == prior['observations'],
                'recordCountEqual': preflight['recordCount'] == prior['recordCount'],
                'baselineDifference': {
                    'executableSha256Differs': preflight['baseline']['executableSha256']
                    != prior['baseline']['executableSha256'],
                    'cmakeCacheSha256Equal': preflight['baseline']['cmakeCacheSha256']
                    == prior['baseline']['cmakeCacheSha256'],
                    'effectiveCmakeEqual': preflight['baseline']['effectiveCmake']
                    == prior['baseline']['effectiveCmake'],
                },
                'secondaryDriftBeyondFileDates': sorted(drift),
            },
        })
        receipt['preflightRecovery']['priorChunks'] = recorded_chunks(prior)

    out = EVIDENCE / 'receipt.json'
    out.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(f'wrote {out.relative_to(ROOT)}: {out.stat().st_size} bytes')
    print(f"tally: {receipt['tally']['censusRows']} rows, "
          f"{receipt['tally']['observed']} observed, {receipt['tally']['blocked']} blocked, "
          f"inputsUnchanged={receipt['tally']['inputsUnchanged']}, "
          f"artifacts={receipt['tally']['artifactsProduced']}")
    print(f"refusals without a reader: {receipt['factoryRefusals']['count']} "
          f"({', '.join(receipt['factoryRefusals']['allLayoutsWithNoReader'])})")
    print(f"-et shapes: " + ', '.join(f"{l}:{r['exitCode']}/{r['artifactBytes']}b"
                                      for l, r in sorted(et_table.items())))


if __name__ == '__main__':
    main()
