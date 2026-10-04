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
def png(width, height, compressed):
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')
for i in range(120):
    width, height = 96, 64
    rows = b''.join(b'\x00' + bytes(((i * 17) % 256, (y * 4) % 256, (255 - i) % 256)) * width for y in range(height))
    (root / 'pictures' / f'image{i}.png').write_bytes(png(width, height, zlib.compress(rows)))
(root / 'corrupt.png').write_bytes(b'not an image')
# A wider-than-preview source with one-pixel stripes, streamed through zlib.
width, height = 6000, 2000
compressor = zlib.compressobj()
parts = []
for y in range(height):
    row = b'\x00' + (b'\x10\x40\xe0\xf0\xc0\x20' if y % 2 else b'\xf0\xc0\x20\x10\x40\xe0') * (width // 2)
    parts.append(compressor.compress(row))
parts.append(compressor.flush())
(root / 'large.png').write_bytes(png(width, height, b''.join(parts)))
PY
export MYOKEN_TEST_ROOT="$fixture"
export XDG_STATE_HOME="$fixture/state"
xvfb-run -a timeout 120s bash run.sh --browser-test "$fixture/pictures"
xvfb-run -a timeout 60s bash run.sh --restore-test "$fixture/pictures"
