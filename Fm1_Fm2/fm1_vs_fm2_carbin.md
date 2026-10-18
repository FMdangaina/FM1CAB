# FM1 vs FM2 .carbin (Toyota Supra RZ / Twin Turbo 98)

**Date:** 18 October 2026\
**Samples:** FM1 `car_supra.cab` -> `fm1b\cars\supra\car.carbin` (1,301,642 bytes, 79 parts; the cab's .txt says
"1998 Toyota Supra Twin Turbo, ordinal 13"); FM2 `toy_suprarz_98.zip` -> `fm2\toy_suprarz_98.carbin`
(3,697,427 bytes, 52 sections). FM1 wheel: `Mega.ca2` -> `mega\tires\art\supra\` (the generic wheels are there).\
**Wrong first sample:** `car_toyotasupra.cab` (`fm1\`, "1992 Toyota Supra", ordinal 124) is another car. Its files
(`fm1_tree.txt`, `fm1_dump.txt`) still served to learn the FM1 grammar, which reads both cars to the last byte.\
**Tools** (`tool\`, C#, csc .NET 4 x86, no ForzaEditor reference):
`fm1scan` (chunk tree), `fm1dump` (every FM1 part), `fm2dump` (FM2 along the legacy "Two" layout, prints the
skipped fields), `fm12cmp` (vertex-level FM1 vs FM2: positions, normals, LODs, UVs, materials), `stats`.
Outputs: `fm1b_dump.txt`, `fm2_dump.txt`, `fm12cmp.txt`.

Marks: **verified** = checked on every record of these files; **probable** = fits the numbers, not proven.

## 1. In short

The byte formats share nothing. FM1 (Xbox, x86) is a little-endian **tagged chunk tree**. FM2 (Xbox 360, PowerPC)
is a big-endian **fixed layout**, the direct ancestor of FM3/FM4.
**The 3D model is the same:** FM2 reuses the FM1 mesh (positions within half-float precision) and adds a more
detailed top LOD.

| | FM1 | FM2 |
|---|---|---|
| Byte order | little-endian | big-endian |
| Container | `.cab` (zlib entries) | `.zip` (deflate) |
| Structure | chunks `[u32 size][u32 version][name\0][data]`, size includes the header | fixed fields; type word 2, version 0x2CA at 0x104 |
| Parts | 79 `Part` (also driver body, brakes, taillights) | 52 sections |
| Vertex buffers | one per part, 20-byte vertices | two shared by the whole car, 60-byte vertices |
| Position | 3 x s16 / 6553.6 m (**verified**) | 4 x half: xyz * w + section offset |
| Axes | front = **+Z** | front = **-Z** (FM2 z = -FM1 z, x and y equal; **verified**) |
| LODs | subpart `a` = 1..4 (5 on a few parts) | subsection `lod` = 1..5; FM2 LOD n+1 ~ FM1 LOD n |
| Materials | number (`b`) | name string |
| Strips | tri strip (prim 6), **no restart markers** | tri strip (itype 6), 0xFFFF restarts |
| Damage | `Damage` chunk: crushed copy of every vertex (12 B) | 32 trailing bytes per vertex (undecoded) |
| UV | u16 / 32768 | sign bit + 15-bit magnitude / 1310.72 (the legacy "*12800" reading) |

## 2. Same mesh (fm12cmp)

- **Positions:** FM1 `(x, y, -z) / 6553.6` lands on FM2 vertices with a median distance of **0.2-0.4 mm** on
  35 of the 44 common parts (bumpers, wings, skirts, hoods, glass, mirrors, trunk, body 0.9 mm). The bounding boxes
  match within 1-5 mm. Different: `interior`, `cagerace` (median 43-72 mm, re-modelled), `steering_wheel`
  (FM1 left-hand drive at x -0.43, FM2 right-hand drive at x +0.43), headlights (3 mm, FM2 has much more geometry).
- **Normals:** mean dot 0.90-1.00 on matched vertices once z is flipped.
- **Wheelbase:** FM1 `Attribs` wheel centres z +1.302 / -1.246 = 2.548 m; FM2 `maxdata.xml` Wheelbase 2.548103.
- **LODs:** FM2 LOD1 has about twice the indices of FM1 LOD1 (trunk 2320 vs 680). FM2 LOD2..5 match FM1 LOD1..4
  (trunk 702/631/486/359 vs 680/623/500/364; skirtLa 294/218/167/109 vs 298/229/189/124). So FM2 added a new top LOD
  and moved the FM1 LODs down by one.
- **UVs:** on the matched vertices FM1 raw / FM2 magnitude = **25.0** (= 32768 / 1310.72) for ~60% of them. The rest
  differ: twin vertices on UV seams picked by the nearest-position match, or FM2 re-mapped areas (open).
- **Index base (FM2, verified):** each section's indices are relative to the sum of (max index + 1) of the earlier
  sections that use the same shared VB, in section order. The sums end exactly at 15178 and 39326.

## 3. FM1 material number -> FM2 name (probable, vertex votes)

| FM1 | FM2 | votes |
|---|---|---|
| 1 | body (paint) | 92% of 7472 |
| 2 | window | 100% of 135 |
| 4 | reflector | 79% |
| 6 | black_glass / rubber_trim | 52 / 48% |
| 7 | chrome | 81% |
| 9 | body / frame / black (mixed) | 47 / 26 / 17% |
| 14 | interior | 100% |
| 15 | frame | 86% |
| 18, 29 | body | 100% (few vertices) |
| 20 | mixed (body, matte_colors, plastic2, weatherstrip) | |
| 21 | rubber_trim | 89% |
| 27 | textured_reflector / misc / chrome / amber glass | mixed |
| 3, 28, 30 | textured_reflector | 100% (few) |
| 31 | reflector | 100% |
| 35 | matte_colors / plastic2 / grille2 | 50 / 25 / 15% |

Mixed rows mean FM2 split one FM1 material into several. Only one car checked.

## 4. FM1 layout (verified on both FM1 cars)

```
Car v3 (size = file size)
  Attribs v1     u32 1, u32 count, then 4 x vec3 floats = wheel centres (m, FM1 axes)
  Driver v1      15 floats (driver position etc.)
  u32 part count
  Part v3 x N
     u8 (1; 0 on calipers, steering_wheel, nocage), 3 bytes varying, u32 name length, name (no NUL)
     Verts v0       u32 count, count x 20 bytes
     Damage v0      u32 count (0 or = vertex count), count x 12 bytes
     Pivots v0      u32 0 or 1; if 1: 22 floats (pivot position + axes)
     Attributes v0  3 floats (1 0.2 0.2 on body panels, 1 0 0 on rigid parts)
     u32 subpart count
     Subpart v2 x M  u32 LOD, u32 material, u32 6 (strip), u32 index count,
                     16 bytes (0 on the first subpart, garbage-looking on the others), u16 indices
  Flare v0 x2    0.5, then a position (probable headlight flares)
  Line v1 x N    u32 count + float polylines (purpose unknown)
```

**Vertex (20 bytes):** s16 x, y, z | s16 normal (unit * 32767) | 2 bytes with two near-equal values (probable baked
light) + 2 zero bytes | u16 u, v.

**Damage:** each record = s16 position + s16 normal of the same vertex after a crash (first car: 84% of the vertices
move, mean 3.2 cm, max 17 cm; rigid parts 0). FM4 stores 4 bytes of damage weights per vertex instead: no direct copy.

**Parts only in FM1:** driver body (`Head`, `Chest`, `Abdomen`, `Left/Right_*` limbs, `driver01`), brakes
(`14_caliperB_LF..RR`, `14_rotor_steel_LF..RR`), `suspension`, `nocage`, `bands`, `taillightL/R`,
`bumperFe/Fk/Fl`, `hoodh/i/j`, `wingraceA/B/C`.
**Only in FM2:** `seatL/R`, `bumperFrameF/R`, `glassLTL/RTL/LHL/RHL` (light glasses), `wingrace`.
All FM1 body parts carry a `_LOD1` suffix, removed for the comparison.

### 4.1 Census on the whole game (2026-10-18)

`fm1census carbin <cabs>` (in `..\`, reads straight from the archives) checks the layout above strictly (every chunk
must end exactly where its size says, every index must be below the part's vertex count) on **all 503 `.carbin`**:
the 234 car archives plus the 269 wheels (`tires\art\*\tire.carbin`) in `Mega.ca2`. **503/503 parse to the last byte**
once the old versions are handled:

| Chunk | Versions | Difference |
|---|---|---|
| `Car` | v3 x496, v2 x6, v1 x1 | v1/v2 have **no `Driver` chunk** (only some wheels) |
| `Part` | v3 x9512, v2 x8, v1 x8 | v1 has **no name** (u8 + 3 bytes, then `Verts`) |
| `Subpart` | v2 x78341, v1 x8 | v1 has **no 16-byte block** |
| `Attribs` | v1 x502, v0 x1 | the second u32 is **not a wheel count**: 5 on cars, 4 or 1 on wheels, always 4 vec3 (12 floats) |

Corrections and facts from the census:
- Every subpart is a tri strip (prim 6, 78349/78349). One wheel has 0xFFFF in a strip.
- LOD 1..4 almost everywhere; 5 on 943 subparts, 0 on 5, 6 on 1.
- 32 material numbers are used. The most common are 1, 9, 2, 20, 15, 27, 7, 18, 28, 25, 4 and 35.
- `Damage` is either empty or one record per vertex (4909 / 4619 parts). `Pivots` is either 0 or 1 + 22 floats.
  `Attributes` is always 3 floats. `Driver` is always 15 floats.
- Tail chunks: `Line` (5397) and `Flare` (480), nothing else.
- 564 distinct part names. The top ones are wheel, body, interior, steering_wheel, mirrors, glasses, trunk, hood,
  suspension, driver01, calipers and lights.

### 4.2 OBJ export and checks (2026-10-18)

`fm1obj <car_x.cab> <out dir>` (in `..\`) writes `car_lod1..n.obj` (one group per part, original part names, one
material per FM1 material number), `car.mtl`, `livery0hi.png` and `uvcheck_lod1.png` (LOD1 UV wireframe over the
atlas). The Supra export is in `obj\supra\` (game data, not in git). The 3DS format was not used: FM1 parts are a
flat list with no parent field (moving parts only have a pivot), so 3DS would add no hierarchy, and it cuts object
names to 10 characters.

- **UV = s16 / 32768** (signed, **verified**), not u16. On the Supra every material falls in [-0.005, 1]; the u16
  reading put a few vertices at ~2.0. They are UVs on `livery0hi` / `livery0`, with no flip in image space (OBJ
  `vt` = 1 - v). The uvcheck shows lights, taillights, badges and grilles on their drawings. The body (material 1)
  is unwrapped over the whole atlas, with the same layout as the damage slice of `livery0`.
- **Strip winding (verified):** even triangles (a, b, c), odd (b, a, c), after negating z for OBJ. 99.9% of the
  LOD1 triangles agree with the stored vertex normals. This closes open point 5.
- The Supra has LOD 1-4 plus 28 triangles of LOD 5.

## 5. FM2 layout (fm2dump)

```
0x000 u32 2 (type)
0x004 64 floats (4 x 16; unknown)
0x104 u32 0x2CA, 0x108 u32 1
0x10C u32 1, 1 x 140-byte record (contains -0.534 0.120 2.151 = exhaust tip)
0x19C 0x6C bytes (6 vectors, unknown)
0x208 u32 5 = LOD count, 10 draw lists (u16 section, u16 subsection)
      u32 1, u32 52 sections
section: u32 0x2C9, offset vec3, half extents (symmetric, like FM4), 0x1C bytes,
         arrayA x16 B and arrayB x2 B (only on glass sections), vbTest1 vbTest2, u8 name length + name,
         8 bytes, own VB (empty here), subsections
subsection: 8 bytes, material name, itype 6, lod 1..5, u32 3, index count + size, indices
after the sections: VB A (vbTest1 = 1: glass, body, headlights, bumperFb; 15178 x 60)
                    VB B (vbTest1 = 0: all the other parts; 39326 x 60); each VB ends with 000decaf
```

**Vertex (60 bytes):** half x, y, z, w | uv0 (2 x 16 bits, see table) | uv1 (always 0) | normal DEC3N 11:11:10 |
8 bytes | 32 bytes, non-zero on ~90-97% of the vertices (probable damage data, undecoded).

## 6. Open

1. FM2 UVs that do not match FM1 (seams or re-mapped areas).
2. FM1 materials 9, 20, 27, 35 (mixed) and the ones not seen on this car.
3. FM1 `Line` chunks, `Flare` fields, part head bytes, subpart 16 bytes, the 2 light bytes.
4. FM2: the 64 header floats, the 0x6C block, the 32 trailing vertex bytes (compare with the FM1 crushed copy),
   arrayA/arrayB on glass.
5. ~~FM1 strip winding~~: verified, see 4.2.
