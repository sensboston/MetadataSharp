"""Expand independent documentation facts into candidate operations, not parity scores."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'docs/research/EXIV2_DOCUMENTED_FORMAT_FACTS.json'
OUTPUT = ROOT / 'docs/research/EXIV2_OPERATION_SEEDS.json'

def build(facts):
    rows = []
    for form in facts['formats']:
        for family, claim in sorted(form['documented'].items()):
            if claim not in ('Read', 'Read/Write', '-', ''):
                raise ValueError(f'Unrecognized documentation claim: {claim}')
            # "Write" is not evidence that each of insert/replace/remove actually works.
            operations = ['read'] if claim in ('Read', 'Read/Write') else []
            if claim == 'Read/Write':
                operations += ['insert', 'replace', 'remove']
            rows.append(dict(seedId=f'EX0289-{form["format"]}-{family.upper()}', format=form['format'], family=family,
                             documentedClaim=claim, candidateOperations=operations,
                             operationEvidence='documentation-only; write suboperations inferred, not observed' if claim == 'Read/Write' else 'documentation-only; no executable observation',
                             variantStatus='unsplit', fixtureRights='not-reviewed', baselineObservation='not-run',
                             metadataSharpVerification='not-assessed', disposition='needs-reconciliation'))
    assert len({r['seedId'] for r in rows}) == len(rows)
    return dict(schema=1, baseline=facts['baseline'], commit=facts['commit'], source=facts['source'],
                documentationBlob=facts['documentationBlob'], denominatorFrozen=False,
                scope='Candidate seeds, NOT atomic verified requirements or a coverage percentage. No-claim cells stay visible, not excluded. Extraction/recognition/video/workflows/tag/encoding/layout cases still need separate census.',
                seeds=rows)

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--check', action='store_true'); args = parser.parse_args()
    text = json.dumps(build(json.loads(SOURCE.read_text())), indent=2) + '\n'
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text() != text:
            raise SystemExit('Capability seeds differ from documentation facts; regenerate/review the delta')
    else:
        OUTPUT.write_text(text)

if __name__ == '__main__':
    main()
