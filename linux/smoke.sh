#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
command -v xvfb-run >/dev/null || { printf 'Install xvfb to run the optional X11 integration checks.\n' >&2; exit 1; }
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/pictures/nested folder/日本" "$fixture/state" "$fixture/codecs"
python3 - "$fixture" <<'PY'
import pathlib, struct, sys, zlib
root = pathlib.Path(sys.argv[1])
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
def png(width, height, compressed):
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')
def png_rgba(width, height, compressed):
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')
for i in range(120):
    width, height = 96, 64
    rows = b''.join(b'\x00' + bytes(((i * 17) % 256, (y * 4) % 256, (255 - i) % 256)) * width for y in range(height))
    (root / 'pictures' / f'image{i}.png').write_bytes(png(width, height, zlib.compress(rows)))
(root / 'corrupt.png').write_bytes(b'not an image')
# Lossless source used to generate HEIC/AVIF fixtures with the Ubuntu heif-enc
# test dependency. Four quadrants make gross channel/decoder mistakes visible.
width, height = 64, 48
rows = []
for y in range(height):
    row = bytearray([0])
    for x in range(width):
        if x < width // 2 and y < height // 2: rgb = (220, 30, 30)
        elif x >= width // 2 and y < height // 2: rgb = (30, 220, 30)
        elif x < width // 2: rgb = (30, 30, 220)
        else: rgb = (220, 220, 30)
        row.extend(rgb)
    rows.append(bytes(row))
(root / 'codecs' / 'source.png').write_bytes(png(width, height, zlib.compress(b''.join(rows))))
rgba_rows = []
for y in range(height):
    row = bytearray([0])
    for x in range(width):
        if x < width // 2 and y < height // 2: rgba = (220, 30, 30, 255)
        elif x >= width // 2 and y < height // 2: rgba = (30, 220, 30, 255)
        elif x < width // 2: rgba = (30, 30, 220, 255)
        else: rgba = (220, 220, 30, 64)
        row.extend(rgba)
    rgba_rows.append(bytes(row))
(root / 'codecs' / 'source-alpha.png').write_bytes(png_rgba(width, height, zlib.compress(b''.join(rgba_rows))))
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
command -v heif-enc >/dev/null || { printf 'Install libheif-examples for L003c smoke fixtures.\n' >&2; exit 1; }
heif-enc "$fixture/codecs/source.png" -q 92 -o "$fixture/codecs/sample.heic" >/dev/null
heif-enc "$fixture/codecs/source.png" -A -q 92 -o "$fixture/codecs/sample.avif" >/dev/null
test -s "$fixture/codecs/sample.heic"
test -s "$fixture/codecs/sample.avif"
command -v cjxl >/dev/null || { printf 'Install libjxl-tools for L003d smoke fixtures.\n' >&2; exit 1; }
cjxl "$fixture/codecs/source-alpha.png" "$fixture/codecs/sample.jxl" -d 0 -e 3 >/dev/null 2>&1
test -s "$fixture/codecs/sample.jxl"
export MYOKEN_TEST_ROOT="$fixture"
export XDG_STATE_HOME="$fixture/state"
xvfb-run -a timeout 120s bash run.sh --browser-test "$fixture/pictures"
xvfb-run -a timeout 60s bash run.sh --restore-test "$fixture/pictures"
