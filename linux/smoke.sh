#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
command -v xvfb-run >/dev/null || { printf 'Install xvfb to run the optional X11 integration checks.\n' >&2; exit 1; }
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/pictures/nested folder/日本" "$fixture/state"
python3 - "$fixture" <<'PY'
import pathlib, struct, sys, zlib
root = pathlib.Path(sys.argv[1])
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
for i in range(120):
    width, height = 96, 64
    rows = b''.join(b'\x00' + bytes(((i * 17) % 256, (y * 4) % 256, (255 - i) % 256)) * width for y in range(height))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b'')
    (root / 'pictures' / f'image{i}.png').write_bytes(png)
(root / 'corrupt.png').write_bytes(b'not an image')
PY
export MYOKEN_TEST_ROOT="$fixture"
export XDG_STATE_HOME="$fixture/state"
# One process exercises browser/tree/decoding and saves two tabs; a fresh process
# verifies actual startup restoration, closed-tab exclusion and active selection.
xvfb-run -a timeout 120s bash run.sh --browser-test "$fixture/pictures"
xvfb-run -a timeout 60s bash run.sh --restore-test "$fixture/pictures"
