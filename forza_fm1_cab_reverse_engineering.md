# Forza Motorsport 1 (Xbox): CAB / CA2 format

**Status:** reading and extraction verified on every archive of the game; writing not done yet\
**Last update:** 18 October 2026\
**Reference tool:** `fm1cab.cs` (C#; `fm1cab list | extract | verify | hashhunt`)\
**Test data:** `F:\Xenia\Games\FM1_Extracted\media\cabs\` (239 `.cab` + 4 `.ca2`)

Build: `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /platform:x86 /optimize fm1cab.cs`
(from PowerShell; Git Bash breaks the `/` switches).

`forza_fm1_cab.py` is the first version of this research (2 sample archives). `fm1cab.cs` supersedes it:
the Python script fails on 13 archives (stored entries, `.ca2`) and ignores the base path.

## 1. Verification (2026-10-18)

`fm1cab verify` on the whole `cabs` folder:

```
243 archives, 0 with problems; 48372 entries: 45492 zlib, 2880 stored
```

Every rule below is checked on every archive. Every entry is inflated and compared with its expected
size (plus the Adler-32 checksum for zlib entries).

## 2. Header (0x000 - 0x131)

| Offset | Type | Value | Status |
|---|---|---|---|
| 0x00 | u32 | `AA AA C0 C0` (magic) | 243/243 |
| 0x04 | u32 | 15 (version) | 243/243 |
| 0x08 | u32 | number of entries | 243/243 |
| 0x0C | u32 | 1; 0 only in `mini.cab` (one entry, stored) | guess: "has zlib entries" |
| 0x10 | u32 | size of the CAB file | 243/243 |
| 0x14 | u32 | "all unpacked" size = `0x132 + n*24 + path table + sum(sizes)` | 243/243 |
| 0x18 | u32 | size in bytes of the path table | 243/243 |
| 0x1C | u32 | same as 0x18 (maybe compressed / uncompressed size of the path table) | 243/243 |
| 0x20 | 16 bytes | zero | 243/243 |
| 0x30 | u16 | 1 | 243/243 |
| 0x32 | char[256] | **base path**, NUL-terminated ASCII, rest zero | 243/243 |

The base path is why the record table starts at 0x132 (`0x32 + 0x100`). It is empty in the car CABs.
It is for example `engines\eng_cosmo\` in the 7 `engine_*` CABs, `ui\ntsc\` in `ui_NTSC.ca2` and
`nowhere\` in `mini.cab`. The full path of a file is **base path + entry path**.

## 3. Record table (0x132, n x 24 bytes)

| Offset | Field | Status |
|---|---|---|
| +0x00 | payload offset in the CAB | 243/243 |
| +0x04 | stored size | 243/243 |
| +0x08 | real size | 243/243 |
| +0x0C | offset of the path string, relative to the start of the path table | 243/243, one-to-one |
| +0x10 | always 0 | 48372/48372 |
| +0x14 | **path hash** | see below |

- When stored size == real size the entry is **stored uncompressed**. Otherwise it is a zlib stream
  (`78 DA`) ending with its Adler-32. There are 2880 stored entries: many in `Cars.ca2`, then the `.txt`
  files of some cars and `lodaudio.ini` of `car_s7`.
- Empty files exist: size 0, same offset as the next payload (`ui\ntsc\raceoverlaywindow.ini`).
- **Records are sorted by increasing hash, and the hash is unique within an archive** (243/243). The game
  uses it for a binary search.
- The hash depends on the full path. The same relative path (`audio\lod1\l04\upgrade.ini`) has different
  hashes in engine CABs with different base paths. Identical contents with different paths have
  different hashes.
- **Algorithm not found.** Tried: CRC-32 (4 variants), Adler-32, FNV-1/1a, djb2, sdbm, x31/x33/x65599
  and similar, one-at-a-time, ELF, rotations. Each was tried on the relative path, full path and file
  name; lower, upper and original case; `\` or `/`; prefixes such as `d:\media\`; ASCII or UTF-16; with
  or without NUL; byte-swapped (`fm1cab hashhunt`). The hash is only needed to **add** new files: a
  replaced file keeps its existing hash. Next step: find the function in `default.xbe` (the lookup on
  the record table).

## 4. Path table

Right after the records: `n` NUL-terminated ASCII strings, total length = header 0x18 (243/243).
The order is the order the files were added, folder by folder: it is **not alphabetical** (it looks
sorted in the car CABs, but not in `Mega.ca2` and `MegaXPR.ca2`).

## 5. Payloads

From the end of the path table to the end of the file, back to back, **with no padding or gaps, in
path-table order** (243/243). No alignment.

## 6. `.ca2`

A `.ca2` is a **whole CAB compressed in chunks**: the CAB is split into 4 MiB pieces (the last one is
shorter). Each piece is a zlib stream that starts on a 2048-byte boundary, with zeros between the
streams and at the end. Inside is the same CAB format (its entries are again zlib or stored).

| File | Chunks | Inner CAB | Entries | Content |
|---|---|---|---|---|
| `Cars.ca2` | 2 | 6,555,194 | 16104 | `.xml` 15660, `.ini` 444 |
| `Mega.ca2` | 3 | 12,143,867 | 2305 | `.bin` 1132, `.xml` 671, `.carbin` 269, `.ini` 212, ... |
| `MegaXPR.ca2` | 12 | 46,173,155 | 1090 | `.xpr` |
| `ui_NTSC.ca2` | 1 | 723,097 | 323 | `.ini` (base `ui\ntsc\`) |

## 7. Content (all archives)

`.ini` 26353, `.xml` 16331, `.xpr` 1823, `.livery` 1402, `.bin` 1132, `.carbin` 503, `.xwb` 480,
`.bat` 268, `.txt` 21, `.tga` 16, `.dat` 15, no extension 10, then `.xls`, `.maxdata`, `.ase`, `.html`,
`.statusxml`, `.bak`, `.old`. Many payloads are identical across archives (shared engine audio files).

## 8. Open

1. Algorithm of the record hash (+0x14): only needed to add files.
2. Certain meaning of 0x0C (only one counter-example) and of 0x1C (always equal to 0x18).
3. Writing: rebuild a CAB/CA2 and test it in Xenia. .NET zlib does not reproduce the original streams
   byte for byte, so compare payloads, not whole files.
