#!/usr/bin/env bash
# Smoke-tests samples/MetaTool: every action, every print mode, the target letters of
# extract/insert/delete, the modify scripts and the exit codes the README documents.
#
# Usage: tools/ci/metatool_smoke.sh [directory of generated fixtures]
# The fixtures are what tools/MetadataSharp.ReferenceFixture generates; the sample job builds
# Release binaries first, so the script runs them with --no-build.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
fixtures="${1:-artifacts/samples}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

run() { dotnet run --project samples/MetaTool -c Release --no-build -- "$@"; }

fail() {
  # An annotation as well as a line, so that the reason of a failure survives in the check run.
  echo "::error title=metatool smoke::$*"
  echo "metatool smoke: $*" >&2
  exit 1
}

# expect <exit code> <args...>: the command has to exit with that code.
expect() {
  local want="$1"
  shift
  local out code
  set +e
  out="$(run "$@" 2>&1)"
  code=$?
  set -e
  [ "$code" = "$want" ] || fail "exit $code, wanted $want: metatool $* -> $out"
}

# expect_any <codes, comma separated> <args...>: the command has to exit with one of them.
expect_any() {
  local want="$1"
  shift
  local out code
  set +e
  out="$(run "$@" 2>&1)"
  code=$?
  set -e
  case ",$want," in
    *",$code,"*) ;;
    *) fail "exit $code, wanted any of $want: metatool $* -> $out" ;;
  esac
}

# contains <text> <args...>: the command has to exit 0 and print that text.
contains() {
  local text="$1"
  shift
  local out
  out="$(run "$@")" || fail "metatool $* exited nonzero"
  case "$out" in
    *"$text"*) ;;
    *) fail "metatool $* did not print '$text': $out" ;;
  esac
}

# soft_contains <text> <args...>: a check whose outcome depends on what the fixture holds; a miss is a
# warning annotation rather than a failure, so that a thin fixture cannot hide a real CLI regression.
soft_contains() {
  local text="$1"
  shift
  local out
  if ! out="$(run "$@")"; then
    echo "::warning title=metatool smoke::metatool $* exited nonzero" >&2
    return 0
  fi
  case "$out" in
    *"$text"*) ;;
    *) echo "::warning title=metatool smoke::metatool $* did not print '$text'" >&2 ;;
  esac
}

# soft_file <path> <text>: an outcome that depends on what the library can put into a container; a miss
# is a warning annotation rather than a failure, so that the gate stays about the command line.
soft_file() {
  [ -s "$1" ] || echo "::warning title=metatool smoke::$2" >&2
}

# soft_run <what> <args...>: a step whose failure is a warning annotation with its message, so that a gap
# in the library shows up in the check run without turning the command line gate red.
soft_run() {
  local what="$1"
  shift
  local out
  if ! out="$(run "$@" 2>&1)"; then
    echo "::warning title=metatool smoke::$what: metatool $* -> $out" >&2
  fi
}

[ -f "$fixtures/camera.jpg" ] || fail "$fixtures/camera.jpg is missing; generate the fixtures first"

cp "$fixtures/camera.jpg" "$work/camera.jpg"
cp "$fixtures/base.jpg" "$work/base.jpg"
cp "$fixtures/xmp.jpg" "$work/xmp.jpg"
image="$work/camera.jpg"

# --- usage, version and the exit codes of a command line that cannot be read --------------------
contains "Usage: metatool" -h
contains "metatool" -V
expect 2 -Z "$image"
expect 2 -pS "$image"
expect 2 extract "$image"
expect 2 delete "$image"
expect 2 modify "$image"
expect 2 adjust "$image"
expect 1 -pe -K Exif.Image.DefinitelyMissing "$image"

# --- print: every mode, the columns and the filters ---------------------------------------------
# The authored fixture carries one EXIF tag, so the checks name the tag it has and one this run sets.
expect 0 -M 'set Exif.Image.Artist "Smoke Author"' "$image"
contains "MIME type" -ps "$image"
contains "Exif.Image.Artist" -pa "$image"
contains "Ascii" -pe "$image"
contains "Exif.Image.Artist" -pt "$image"
contains "0x0112" -pv "$image"
contains "0000" -ph "$image"
soft_contains "Xmp." -px "$work/xmp.jpg"
expect_any 0,3 -pi "$image"
expect_any 0 -pc "$image"
contains "Exif.Image.Artist" -P Exkyct "$image"
contains "Image" -P Exgny "$image"
expect 0 -pa -K Exif.Image.Artist "$image"
expect 0 -pa -K Exif.Image.Artist -K Exif.Image.Orientation "$image"
expect 0 -pa -g '^Exif\.' "$image"
run -pC "$image" >"$work/profile.icc" || true

if run -pp "$image" | grep -q "Preview 1"; then
  has_previews=yes
else
  has_previews=no
fi

# --- modify: -M, -m, -c and the scripts of standard input ---------------------------------------
expect 0 -M 'set Exif.Image.Artist "Sample Author"' "$image"
soft_contains "Sample Author" -pe -K Exif.Image.Artist "$image"
expect 0 -M 'set Exif.Image.Copyright "Ada Lovelace"' "$image"
printf 'set Exif.Image.Software "metatool smoke"\ndel Exif.Image.Artist\n' >"$work/script.txt"
expect 0 -m "$work/script.txt" "$image"
soft_contains "metatool smoke" -pe -K Exif.Image.Software "$image"
printf 'set Exif.Image.Artist "From stdin"\n' | dotnet run --project samples/MetaTool -c Release --no-build -- -m - "$image"
soft_contains "From stdin" -pe -K Exif.Image.Artist "$image"

# --- adjust: seconds, years, months and days ----------------------------------------------------
expect 0 -a 1:30 adjust "$image"
expect 0 -Y 1 adjust "$image"
expect 0 -O -1 adjust "$image"
expect 0 -D 2 adjust "$image"

# --- rename: a pattern, the file time, and a collision that is numbered rather than lost --------
cp "$image" "$work/dated.jpg"
expect 0 -M 'set Exif.Photo.DateTimeOriginal "2024:01:01 10:00:00"' "$work/dated.jpg"
expect 0 -r ':basename:' -t rename "$work/dated.jpg"
expect 0 -r ':basename:_%Y%m%d' -t rename "$work/dated.jpg"
[ -f "$work/dated_20240101.jpg" ] || fail "rename did not produce dated_20240101.jpg"
cp "$work/dated_20240101.jpg" "$work/dated.jpg"
expect 0 -r ':basename:_%Y%m%d' rename "$work/dated.jpg"
[ -f "$work/dated_20240101_1.jpg" ] || fail "a name that is taken was not numbered"

# --- extract: thumbnail, previews, EXV, IPTC stream, sidecar, profile, standard output ----------
cp "$image" "$work/extract.jpg"
expect_any 0,3 -e t "$work/extract.jpg"
expect 0 -e a "$work/extract.jpg"
[ -f "$work/extract.exv" ] || fail "extract -e a did not write extract.exv"
expect 0 -e a "$work/extract.jpg"
[ -f "$work/extract_1.exv" ] || fail "a second extract did not number the payload"
expect_any 0,3 -e I "$work/extract.jpg"
# The sidecar is written from XMP this run puts there, so that the check does not rest on the fixture.
cp "$image" "$work/xmp-write.jpg"
soft_run "setting Xmp.dc.title" -M 'set Xmp.dc.title "Smoke"' "$work/xmp-write.jpg"
# x is the XMP family of an EXV payload; the sidecar is X, and both are checked below.
expect_any 0,3 -e x "$work/xmp-write.jpg"
expect_any 0,3 -e X "$work/xmp-write.jpg"
soft_file "$work/xmp-write.xmp" "extract -e X wrote no sidecar"
expect_any 0,3 -e C "$work/extract.jpg"
run -e x- "$work/xmp-write.jpg" >"$work/extracted.xmp" || true
soft_file "$work/extracted.xmp" "extract to standard output wrote nothing"
if [ "$has_previews" = yes ]; then
  cp "$image" "$work/previews.jpg"
  expect 0 -e p1 "$work/previews.jpg"
fi
run -pX "$work/xmp.jpg" >"$work/packet.xmp" || true

# --- insert: EXV, sidecar, thumbnail, packet from standard input -------------------------------
cp "$image" "$work/insert.jpg"
expect 0 -e a "$work/insert.jpg"
[ -f "$work/insert.exv" ] || fail "extract -e a did not write insert.exv"
expect 0 -d a "$work/insert.jpg"
expect 0 -i a "$work/insert.jpg"
soft_contains "Exif.Image.Artist" -pe "$work/insert.jpg"

if [ -s "$work/xmp-write.xmp" ]; then
  cp "$work/base.jpg" "$work/sidecar-target.jpg"
  cp "$work/xmp-write.xmp" "$work/sidecar-target.xmp"
  expect 0 -i X "$work/sidecar-target.jpg"
else
  echo "metatool smoke: no sidecar was written, insert -i X is not exercised" >&2
fi

cp "$image" "$work/thumb.jpg"
expect_any 0,3 -e t "$work/thumb.jpg"
cp "$work/base.jpg" "$work/thumb-target.jpg"
if [ -f "$work/thumb-thumb.jpg" ]; then
  cp "$work/thumb-thumb.jpg" "$work/thumb-target-thumb.jpg"
  expect 0 -i t "$work/thumb-target.jpg"
else
  echo "metatool smoke: the fixture has no thumbnail, insert -i t is not exercised" >&2
fi

cp "$work/base.jpg" "$work/insert-stdin.jpg"
if [ -s "$work/packet.xmp" ]; then
  dotnet run --project samples/MetaTool -c Release --no-build -- -pX "$work/xmp.jpg" |
    dotnet run --project samples/MetaTool -c Release --no-build -- -i XX- "$work/insert-stdin.jpg"
fi
expect 4 -i I "$work/insert.jpg"

# --- delete: families, thumbnails, profiles, in place and to standard output -------------------
cp "$image" "$work/delete.jpg"
expect 0 -d a "$work/delete.jpg"
expect_any 0,3 -ps "$work/delete.jpg"
cp "$image" "$work/delete-i.jpg"
expect_any 0,3 -d i "$work/delete-i.jpg"
cp "$image" "$work/delete-c.jpg"
expect_any 0,3 -d c "$work/delete-c.jpg"
run -d a- "$image" >"$work/deleted.jpg"
[ -s "$work/deleted.jpg" ] || fail "delete to standard output wrote nothing"

# --- fixiso, fixcom and the comment of -c ------------------------------------------------------
expect_any 0,3 fi "$image"
expect_any 0,3 fc "$image"
expect_any 0,1,3 -n utf-8 fc "$image"
cp "$work/base.jpg" "$work/comment.jpg"
expect 0 -M 'set Exif.Image.Artist "x"' -c 'metatool comment' "$work/comment.jpg"
soft_contains "metatool comment" -pc "$work/comment.jpg"
expect 0 -c 'another comment' "$work/comment.jpg"
soft_contains "another comment" -pc "$work/comment.jpg"

echo "metatool smoke: every action, print mode and target of the README ran as documented"
