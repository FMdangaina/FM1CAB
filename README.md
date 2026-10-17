# fm1cab

Reader, extractor and format checker for the `.cab` and `.ca2` archives of
**Forza Motorsport (2005, original Xbox)**.

The format was reverse-engineered and every rule is checked on all the archives of the game:
239 `.cab` + 4 `.ca2`, 48,372 files, 0 problems.

## Build

One C# file, no dependencies, .NET Framework 4 (included in Windows):

```
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /platform:x86 /optimize fm1cab.cs
```

Run it from PowerShell or cmd. Git Bash turns the `/` switches into paths.

## Usage

```
fm1cab list     <file.cab|file.ca2>             entries: offset, size, stored size, z/S, hash, path
fm1cab extract  <file.cab|file.ca2> <out dir>   files under their original paths + <name>.manifest.csv
fm1cab verify   <cabs folder>                   checks every format rule on every .cab and .ca2
fm1cab hashhunt <file> [records]                tries string hashes against the record hash field
```

`.ca2` files are inflated in memory first, so all commands take both kinds.

## What is checked

When a file is extracted:

- zlib entries: inflated size and the Adler-32 checksum of the stream;
- every record points to the start of a path, and every path is used exactly once;
- payloads follow each other with no gap or overlap, in path-table order, up to the end of the file;
- header sizes (file size, unpacked size, path table size) match the archive.

An error stops the tool before a wrong file is written. When a format rule is broken, `list` and
`extract` print a `PROBLEM:` line. When the output is only `OK: ...`, the archive is fully consistent.

## Format in short

| Part | Content |
|---|---|
| Header `0x000-0x131` | magic `AA AA C0 C0`, version 15, count, file size, unpacked size, path table size, 256-byte base path at `0x32` |
| Records `0x132` | `count` x 24 bytes: offset, stored size, size, path offset, 0, path hash; sorted by hash |
| Path table | NUL-terminated ASCII paths in the order the files were added |
| Payloads | back to back to the end of the file; zlib, or stored when stored size == size |

A `.ca2` is a whole CAB split into 4 MiB pieces, each one a zlib stream that starts on a 2048-byte boundary.

The full notes, with the open points, are in
[forza_fm1_cab_reverse_engineering.md](forza_fm1_cab_reverse_engineering.md).

## Open points

- The algorithm of the record path hash is unknown. It is only needed to **add** files; a replaced
  file keeps its hash.
- Writing archives is not done yet.

## Files

- `fm1cab.cs`: the tool.
- `forza_fm1_cab_reverse_engineering.md`: format notes and verification results.
- `forza_fm1_cab.py`: first Python version, based on two car archives. It is superseded: it does not
  read `.ca2`, stored entries or the base path.

No game data is included. You need your own copy of the game.

## License

GPL-3.0, see [LICENSE](LICENSE).
