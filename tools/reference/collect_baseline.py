"""Collect complete E01 observation receipts, without downloading executable artifacts."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zlib
from exiv2_baseline import PIN, require


def decode_receipt(annotations, source):
    pieces = {}
    total = None
    for item in annotations:
        match = re.fullmatch(r'E01Z observations.json (\d+)/(\d+)', item['title'])
        if not match:
            continue
        index, count = map(int, match.groups())
        require(0 <= index < count <= 40 and index not in pieces, 'Duplicate/out-of-range chunk')
        require(total is None or total == count, 'Inconsistent chunk counts')
        require(0 < len(item['message']) <= 2500, 'Invalid chunk size')
        total = count
        pieces[index] = item['message']
    require(total is not None and set(pieces) == set(range(total)), 'Missing chunks')
    packed = base64.b64decode(''.join(pieces[i] for i in range(total)), validate=True)
    decoder = zlib.decompressobj()
    raw = decoder.decompress(packed, 1_000_001)
    require(len(raw) <= 1_000_000 and decoder.eof and not decoder.unused_data and not decoder.unconsumed_tail, 'Incomplete/oversized/concatenated receipt')
    report = json.loads(raw)
    require(report['sourceCommit'] == source and report['status'] == 'passed', 'Wrong source/failed observation run')
    require(report['baseline']['sourceCommit'] == PIN and report['baseline']['version'] == '0.28.9', 'Wrong baseline')
    rows = report['observations']
    require(len(rows) == 4 and {r['operation'] for r in rows} == {'read', 'insert', 'replace', 'remove'}, 'Incomplete operation observations')
    require(len({r['id'] for r in rows}) == 4 and all(r['baselineObservation'] == 'passed-independent-byte-and-print-check' for r in rows), 'Invalid observation status/identity')
    return raw, report


def gh(*args):
    return json.loads(subprocess.check_output(['gh', *args], text=True))


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('run'); parser.add_argument('destination', type=Path); args = parser.parse_args()
    run = gh('run', 'view', args.run, '--json', 'headSha,status,conclusion,url,jobs')
    require(run['status'] == 'completed' and run['conclusion'] == 'success', 'Run is not successful')
    checks = [c for c in run['jobs'] if c['name'] == 'Exiv2 0.28.9 / bounded E01 observations']
    require(len(checks) == 1 and checks[0]['conclusion'] == 'success', 'Missing/failed/duplicate E01 job')
    check_id = checks[0]['databaseId']
    annotations = gh('api', f'repos/sensboston/MetadataSharp/check-runs/{check_id}/annotations?per_page=100')
    raw, report = decode_receipt(annotations, run['headSha'])
    args.destination.mkdir(parents=True, exist_ok=True)
    (args.destination / 'observations.json').write_text(json.dumps(report, indent=2) + '\n')
    (args.destination / 'receipt.json').write_text(json.dumps(dict(runId=args.run, sourceCommit=run['headSha'], url=run['url'], checkRun=check_id, rawJsonSha256=hashlib.sha256(raw).hexdigest(), encoding='complete zlib/base64 chunks <=2500 bytes'), indent=2) + '\n')


if __name__ == '__main__':
    main()
