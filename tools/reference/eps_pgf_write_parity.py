"""Bidirectional writer parity for EPS and PGF against pinned Exiv2.

The fixture executable writes our packets, which Exiv2 then reads. Exiv2 also writes
independent EPS and PGF copies, which the fixture executable reads through ImageMetadata.
The script only uses authored b11eps fixtures; no corpus bytes are stored here.
"""
import argparse
import json
import shlex
import subprocess
import sys
from pathlib import Path


def run(args, *, check=True):
    result = subprocess.run(args, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if check and result.returncode:
        raise RuntimeError(f"command failed ({result.returncode}): {args!r}\n{result.stdout}\n{result.stderr}")
    return result


def exiv2_properties(exiv2, path):
    result = run([exiv2, "-Pkv", str(path)], check=False)
    properties = {}
    for line in result.stdout.splitlines():
        fields = line.split(None, 1)
        if fields and fields[0].startswith(("Exif.", "Iptc.", "Xmp.")):
            properties.setdefault(fields[0], []).append(fields[1].strip() if len(fields) > 1 else "")
    if result.returncode not in (0, 253) or result.returncode == 253 and properties:
        raise RuntimeError(f"Exiv2 could not print {path}: {result.stdout} {result.stderr}")
    return properties


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("work", type=Path)
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--fixture", required=True, help="reference fixture executable command")
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    generator = shlex.split(args.fixture)
    run([*generator, "small-format-writes", str(args.fixtures), str(args.work)])

    outcomes = {}
    expected_keys = {
        "ours-eps-insert.eps": ("Xmp.xmp.CreatorTool", "Parity tool"),
        "ours-eps-replace.eps": ("Xmp.xmp.CreatorTool", "Parity tool"),
        "ours-dos-eps.eps": ("Xmp.xmp.CreatorTool", "Parity tool"),
        "ours-pgf.pgf": ("Exif.Image.Artist", "Parity Artist"),
        "ours-pgf.pgf|iptc": ("Iptc.Application2.Headline", "Parity headline"),
        "ours-pgf.pgf|xmp": ("Xmp.xmp.CreatorTool", "Parity tool"),
    }
    for report_name, (key, value) in expected_keys.items():
        name = report_name.split("|", 1)[0]
        path = args.work / name
        properties = exiv2_properties(args.exiv2, path)
        values = properties.get(key, [])
        if not any(value in item for item in values):
            raise AssertionError(f"Exiv2 did not read our {key}={value!r} from {name}: {values!r}")
        outcomes[f"our-write/exiv2-read/{name}/{key}"] = "equal"
    if "Xmp.xmp.CreatorTool" in exiv2_properties(args.exiv2, args.work / "ours-eps-remove.eps"):
        raise AssertionError("Exiv2 still reads XMP after our EPS whole-packet removal")
    outcomes["our-write/exiv2-read/eps-remove/Xmp.xmp.CreatorTool"] = "equal (removed)"
    comment = run([args.exiv2, "-pc", str(args.work / "ours-pgf.pgf")]).stdout
    if "Parity comment" not in comment:
        raise AssertionError(f"Exiv2 did not read our PGF comment: {comment!r}")
    outcomes["our-write/exiv2-read/ours-pgf.pgf/comment"] = "equal"

    # Exiv2 writes a new XMP packet in plain EPS, and all four PGF metadata families in its PNG carrier.
    (args.work / "eps-modify.txt").write_text("set Xmp.xmp.CreatorTool Reference writer\n", encoding="utf-8")
    eps_written = args.work / "reference.eps"
    eps_written.write_bytes((args.fixtures / "eps-plain.eps").read_bytes())
    run([args.exiv2, "-m", str(args.work / "eps-modify.txt"), "mo", str(eps_written)])

    (args.work / "pgf-modify.txt").write_text(
        "set Exif.Image.Artist Reference Artist\n"
        "set Iptc.Application2.Headline Reference headline\n"
        "set Xmp.xmp.CreatorTool Reference writer\n",
        encoding="utf-8",
    )
    pgf_written = args.work / "reference.pgf"
    pgf_written.write_bytes((args.fixtures / "pgf-png.pgf").read_bytes())
    run([args.exiv2, "-c", "Reference comment", "-m", str(args.work / "pgf-modify.txt"), "mo", str(pgf_written)])

    def inspect(path):
        result = run([*generator, "inspect-small-format", str(path)])
        return json.loads(result.stdout)

    checks = {
        "reference.eps": (inspect(eps_written), {"format": "Eps", "creatorTool": "Reference writer"}),
        "reference.pgf": (inspect(pgf_written), {
            "format": "Pgf", "artist": "Reference Artist", "headline": "Reference headline",
            "creatorTool": "Reference writer", "comments": ["Reference comment"],
        }),
    }
    for name, (actual, expected) in checks.items():
        for key, value in expected.items():
            if actual.get(key) != value:
                raise AssertionError(f"Our reader disagrees with Exiv2's write for {name} {key}: {actual.get(key)!r} != {value!r}")
            outcomes[f"exiv2-write/our-read/{name}/{key}"] = "equal"

    report = {
        "reference": "Exiv2 0.28.9 at b0859fc61fea5dedfc898aece6b15893ddf4314e",
        "outcomes": outcomes,
        "keys_found": sum(value.startswith("equal") for value in outcomes.values()),
        "keys_equal": sum(value.startswith("equal") for value in outcomes.values()),
        "problem_count": 0,
    }
    (args.work / "write-parity.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, sort_keys=True))
    summary = f"EPS/PGF writer parity: {report['keys_found']} target key checks found; {report['keys_equal']} equal; {report['problem_count']} problems.\n"
    summary_path = __import__('os').environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as stream:
            stream.write(summary)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"::error title=EPS/PGF write parity::{str(error).replace('%', '%25').replace(chr(10), '%0A')}", file=sys.stderr)
        raise SystemExit(1)
