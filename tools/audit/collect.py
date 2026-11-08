"""Recover small compressed evidence via GitHub API annotations, not blob downloads."""
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zlib

def gh(*args):
    return json.loads(subprocess.check_output(['gh', *args], text=True))

def main(run_id, destination):
    run = gh('run', 'view', run_id, '--json', 'headSha,status,conclusion,url')
    assert run['status'] == 'completed' and run['conclusion'] == 'success', run
    checks = gh('api', f'repos/sensboston/MetadataSharp/commits/{run["headSha"]}/check-runs', '-f', 'per_page=100', '--method', 'GET')['check_runs']
    receipts = []
    for check in checks:
        if not check['name'].startswith('Audit / ') or f'/runs/{run_id}/' not in check['details_url']:
            continue
        assert check['conclusion'] == 'success', check
        annotations = gh('api', f'repos/sensboston/MetadataSharp/check-runs/{check["id"]}/annotations?per_page=100')
        platform = check['name'].split('/')[-1].strip().replace('-latest', '')
        for file in ['baseline.json', 'packages.json']:
            pieces = {}
            for a in annotations:
                if a['title'].startswith(f'M18Z {file} '):
                    index, count = map(int, a['title'].split()[-1].split('/'))
                    pieces[index] = a['message']
            assert pieces and len(pieces) == count and set(pieces) == set(range(count))
            raw = zlib.decompress(base64.b64decode(''.join(pieces[i] for i in range(count))))
            value = json.loads(raw); assert value['sourceCommit'] == run['headSha']
            path = destination / f'{platform}-{file}'
            path.write_text(json.dumps(value, indent=2) + '\n')
            receipts.append(dict(file=path.name, checkRun=check['id'], rawJsonSha256=hashlib.sha256(raw).hexdigest()))
    assert len(receipts) == 6, receipts
    (destination / 'receipt.json').write_text(json.dumps(dict(run=run, runId=run_id, encoding='zlib/base64 chunks <=2500 bytes', files=receipts), indent=2)+'\n')

if __name__ == '__main__':
    destination = Path(sys.argv[2]); destination.mkdir(parents=True, exist_ok=True)
    main(sys.argv[1], destination)
