# fm1cab: Forza Motorsport 1 mesh exporter

Research tools for **Forza Motorsport (2005, original Xbox)** that end in a mesh exporter: FM1 car and wheel
models to Wavefront OBJ, with their textures.

## Scope

The project started as a reader for the game's `.cab` / `.ca2` archives. It then grew to the formats inside them:
`.carbin` models, `.xpr` textures, and the Turn10 names of parts and materials. It ends as a **mesh exporter**:
`fm1studio`, a standalone FM1 → OBJ tool that works like Forza Studio 4.6. It is research for model conversion in
Forza Studio. It is not a game editor: nothing is written back to FM1.

Verified on every file of the game:

| Format | Files | Result |
|---|---|---|
| `.cab` / `.ca2` archives | 239 + 4 (48,372 entries) | every entry extracts and checks |
| `.carbin` models | 503 (234 cars, 269 wheels) | parse to the last byte |
| `.xpr` textures | 1,823 | decode; every kind checked by eye |

## Mesh exporter (fm1studio)

File > Import opens one `.cab` / `.ca2` / `.carbin` / `.xpr` at a time.

- **Models**: carbin > LOD > part > mesh (one per material) with checkboxes. The view draws the checked meshes.
  Right-click gives **Extract Checked / Extract All**: OBJ + MTL, plus the livery PNG for a car.
- **Textures**: the `.xpr` files. Select one to see it. Right-click gives Save As / Extract Checked / Extract All as PNG.
- **Names**: Turn10's own names. The part name is the one stored in the carbin. The material name is the Turn10 enum
  name of the subpart's material number, read from `default.xbe` (see `fm1_names.md`). The OBJ writes `g <part>` and
  `usemtl <material>`.
- **Axes**: metres, front at -Z (FM1's z negated, the FM2/FM4 frame).
- **Wheels**: import `Mega.ca2` (`tires\art\<style>\tire.carbin`); their textures are in `MegaXPR.ca2`.

It needs XNA 3.1 (Microsoft.Xna.Framework 3.1 in the GAC) and `fm1studio.exe.config` next to the exe. The build line
is at the top of `fm1studio.cs`.

## CAB / CA2 archives (fm1cab)

The format was reverse-engineered and every rule is checked on all the archives of the game.

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

## Other tools

All of them are one-file C# programs built with the .NET 4 `csc`, x86. They are compiled together with
`fm1cab.cs` (and `fm1model.cs` / `fm1xpr.cs`) using `/main:<class>`. The build line is at the top of each file.

| Tool | What it does |
|---|---|
| `fm1studio` | Standalone FM1 → OBJ tool that works like Forza Studio 4.6. File > Import opens one `.cab` / `.ca2` / `.carbin` / `.xpr` at a time. The **Models** tab shows carbin > LOD > part > mesh (one per material, Turn10 names) with checkboxes, and the view draws the checked meshes. The **Textures** tab lists the `.xpr` files. Right-click gives Extract Checked / All (OBJ with MTL and livery PNG, or PNG textures). Needs XNA 3.1 and `fm1studio.exe.config`. |
| `fm1obj` | Exports a car to OBJ, one file per LOD, with Turn10 part and material names, plus the livery PNG and a UV check image. |
| `fm1xpr` | Converts XPR to PNG. `check` decodes every texture of the game. |
| `fm1census` | Checks the `.carbin` / `.xpr` layout on every file of the game. |
| `xbeptr` | Reads string tables from `default.xbe`, e.g. the material enum. |
| `wincap` | Saves a PNG of a program's window without sending any input (used for viewer checks). |

Format notes: `fm1_xpr_format.md`, `fm1_names.md`, `Fm1_Fm2/fm1_vs_fm2_carbin.md`.

## Files

- `fm1studio.cs`: the mesh exporter. Also `fm1studio.ico` and `fm1studio.exe.config`.
- `fm1model.cs` (carbin model), `fm1xpr.cs` (XPR textures), `fm1cab.cs` (archives): shared by all the tools.
- `forza_fm1_cab_reverse_engineering.md`: CAB format notes and verification results.
- `Fm1_Fm2/`: FM1 vs FM2 carbin research (`fm1_vs_fm2_carbin.md`, C# probes in `tool/`), plus the first Python
  research pack (CAB, XPR → JPEG).
- `forza_fm1_cab.py`: first Python version, based on two car archives. It is superseded: it does not
  read `.ca2`, stored entries or the base path.

No game data is included. You need your own copy of the game.

## License

GPL-3.0, see [LICENSE](LICENSE).
