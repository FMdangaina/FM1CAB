#!/usr/bin/env python3
"""Forza Motorsport 1 (original Xbox) CAB extractor.

Reverse-engineered from car CABs. The CAB contains a 0x132-byte header,
24-byte records, then a NUL-terminated path table. Record field 4 (DWORD at
record offset + 12) is a byte offset relative to the beginning of the path
table; each payload is zlib-compressed. Other record fields are preserved
in the manifest; field 6's meaning is not assumed.

Usage:
  python forza_fm1_cab.py car_toyota2000gt.cab
  python forza_fm1_cab.py car_nissanaltima.cab -o out --zip
"""
import argparse
import csv
import hashlib
import json
import struct
import sys
import zipfile
import zlib
from pathlib import Path, PurePosixPath

MAGIC = bytes.fromhex('aa aa c0 c0')
RECORD_TABLE_OFFSET = 0x132
RECORD_SIZE = 24
COUNT_OFFSET = 8


def safe_path(raw: str) -> PurePosixPath:
    p = PurePosixPath(raw.replace('\\', '/'))
    if p.is_absolute() or not p.parts or any(part in ('', '.', '..') for part in p.parts):
        raise ValueError(f'Unsafe archive path: {raw!r}')
    # Disallow drive prefixes and control characters.
    if ':' in p.parts[0] or any(ord(c) < 32 for c in raw):
        raise ValueError(f'Unsafe archive path: {raw!r}')
    return p


def parse_cab(path: Path):
    data = path.read_bytes()
    if len(data) < RECORD_TABLE_OFFSET or data[:4] != MAGIC:
        raise ValueError(f'{path}: not a recognized FM1 CAB (magic={data[:4].hex()})')
    count = struct.unpack_from('<I', data, COUNT_OFFSET)[0]
    if count == 0 or count > 100000:
        raise ValueError(f'Implausible record count: {count}')
    table_end = RECORD_TABLE_OFFSET + count * RECORD_SIZE
    if table_end > len(data):
        raise ValueError('Record table extends beyond end of file')

    # Path table is stored in path-table order. Map byte offset from its start -> path.
    paths_by_relative_offset = {}
    path_rows = []
    pos = table_end
    for i in range(count):
        start = pos
        end = data.find(b'\0', pos)
        if end < 0:
            raise ValueError(f'Path table terminated early at entry {i}')
        raw = data[start:end]
        if not raw:
            raise ValueError(f'Empty path at path-table entry {i}')
        try:
            path_text = raw.decode('ascii')
        except UnicodeDecodeError as exc:
            raise ValueError(f'Non-ASCII path at entry {i}') from exc
        rel = start - table_end
        if path_text in ('', '.') or any(ord(c) < 32 for c in path_text):
            raise ValueError(f'Invalid path at entry {i}: {path_text!r}')
        safe_path(path_text)  # validate before extraction
        paths_by_relative_offset[rel] = path_text
        path_rows.append({'path_table_index': i, 'path_offset': rel, 'path': path_text})
        pos = end + 1
    path_table_end = pos

    entries = []
    seen_paths = set()
    for i in range(count):
        rec_pos = RECORD_TABLE_OFFSET + i * RECORD_SIZE
        data_offset, compressed_size, uncompressed_size, path_ref, field5, field6 = struct.unpack_from('<6I', data, rec_pos)
        if path_ref not in paths_by_relative_offset:
            raise ValueError(f'Record {i} refers to missing path-table offset 0x{path_ref:X}')
        original_path = paths_by_relative_offset[path_ref]
        rel_path = safe_path(original_path)
        if str(rel_path).casefold() in seen_paths:
            raise ValueError(f'Duplicate output path: {original_path}')
        seen_paths.add(str(rel_path).casefold())
        if compressed_size == 0 or data_offset + compressed_size > len(data):
            raise ValueError(f'Record {i} payload bounds invalid: offset=0x{data_offset:X}, size=0x{compressed_size:X}')
        compressed = data[data_offset:data_offset + compressed_size]
        try:
            payload = zlib.decompress(compressed)
        except zlib.error as exc:
            raise ValueError(f'Record {i} ({original_path}): zlib decompression failed') from exc
        if len(payload) != uncompressed_size:
            raise ValueError(f'Record {i} ({original_path}): expected {uncompressed_size} bytes, got {len(payload)}')
        entries.append({
            'record_index': i,
            'record_offset': rec_pos,
            'data_offset': data_offset,
            'compressed_size': compressed_size,
            'uncompressed_size': uncompressed_size,
            'path_table_offset': path_ref,
            'field5': field5,
            'field6_hex': f'0x{field6:08X}',
            'path': original_path,
            'payload': payload,
        })

    # Key validation: every path-table entry is referenced exactly once.
    referenced = [e['path_table_offset'] for e in entries]
    if len(set(referenced)) != count or set(referenced) != set(paths_by_relative_offset):
        raise ValueError('Record/path table relation is not one-to-one; refusing potentially incorrect extraction')
    return data, count, table_end, path_table_end, path_rows, entries


def extract(cab: Path, out: Path, make_zip: bool):
    data, count, path_start, path_end, path_rows, entries = parse_cab(cab)
    out.mkdir(parents=True, exist_ok=True)
    manifest_entries = []
    total_unpacked = 0
    for entry in entries:
        rel = safe_path(entry['path'])
        dest = out.joinpath(*rel.parts)
        dest.parent.mkdir(parents=True, exist_ok=True)
        payload = entry.pop('payload')
        dest.write_bytes(payload)
        total_unpacked += len(payload)
        entry['sha256'] = hashlib.sha256(payload).hexdigest()
        entry['output'] = rel.as_posix()
        manifest_entries.append(entry)

    (out / 'paths_in_table_order.txt').write_text(
        ''.join(f"{p['path_table_index']:04d}\t+0x{p['path_offset']:08X}\t{p['path']}\n" for p in path_rows),
        encoding='utf-8')
    manifest = {
        'format': 'Forza Motorsport 1 original Xbox CAB (reverse-engineered)',
        'input_file': cab.name,
        'input_size': len(data),
        'magic': data[:4].hex(' ').upper(),
        'entry_count': count,
        'record_table_offset': f'0x{RECORD_TABLE_OFFSET:X}',
        'record_size': RECORD_SIZE,
        'path_table_offset': f'0x{path_start:X}',
        'path_table_end': f'0x{path_end:X}',
        'mapping_rule': 'record DWORD at +0x0C is a byte offset relative to the path table start',
        'compression': 'zlib; all entries decompressed and uncompressed sizes verified',
        'total_uncompressed_bytes': total_unpacked,
        'entries': manifest_entries,
    }
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    csv_fields = ['record_index','record_offset','data_offset','compressed_size','uncompressed_size','path_table_offset','field5','field6_hex','path','output','sha256']
    with (out / 'manifest.csv').open('w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=csv_fields)
        writer.writeheader()
        writer.writerows(manifest_entries)
    (out / 'README.txt').write_text(
        f"Forza Motorsport 1 CAB extraction\nInput: {cab.name}\nEntries: {count}\n"
        f"Extracted bytes: {total_unpacked}\nAll {count} record-to-path references resolved and verified.\n"
        "Files are written using their original paths from the CAB path table.\n"
        "The meaning of field6 is preserved in the manifest but not yet identified.\n", encoding='utf-8')

    zip_path = None
    if make_zip:
        zip_path = out.with_suffix('.zip')
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zf:
            for file in sorted(out.rglob('*')):
                if file.is_file():
                    zf.write(file, file.relative_to(out).as_posix())
    return count, total_unpacked, zip_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cab', type=Path, help='FM1 CAB file')
    parser.add_argument('-o', '--out', type=Path, help='output folder (default: <cab>_extracted)')
    parser.add_argument('--zip', action='store_true', help='also create a ZIP of the extracted tree')
    args = parser.parse_args()
    out = args.out or args.cab.with_name(args.cab.stem + '_extracted')
    try:
        count, total, zip_path = extract(args.cab, out, args.zip)
    except (OSError, ValueError, struct.error) as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        return 2
    print(f'OK: extracted {count} files ({total:,} uncompressed bytes) to {out}')
    print('Original CAB paths reconstructed: 100%')
    if zip_path:
        print(f'ZIP: {zip_path}')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
