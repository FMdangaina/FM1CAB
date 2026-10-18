# Forza Motorsport 1 (Xbox): XPR0 textures

**Status:** layout verified on all 1823 `.xpr` of the game; decoding checked by eye on every kind (cube maps: face 0 only)\
**Last update:** 18 October 2026\
**Tools:** `fm1xpr.cs` (XPR -> PNG, `check` decodes all), `fm1census.cs xpr` (layout census). Both read straight
from the `.cab` / `.ca2` archives with `fm1cab.cs`.\
**Starting point:** `Fm1_Fm2/FM1_ReverseEngineering_Pack/tools/fm1_xpr_to_jpeg.py` (format word bits, DXT decoder,
3D swizzle idea), checked on one file, here ported to C# and checked on the whole game.

Build:
```
csc /nologo /platform:x86 /optimize /main:Fm1Xpr /r:System.Drawing.dll fm1xpr.cs fm1cab.cs
csc /nologo /platform:x86 /optimize /main:Fm1Census fm1census.cs fm1cab.cs
```

## 1. File

| Offset | Content |
|---|---|
| 0x00 | `XPR0` |
| 0x04 | total size = file size (1823/1823) |
| 0x08 | header size, always a multiple of 2048 (1823/1823) |
| 0x0C | resources, ended by `0xFFFFFFFF` |
| header size | texture data; the file is padded with zeros to the next 2048 bytes |

Every FM1 `.xpr` holds **exactly one texture resource** (1823/1823). Texture resource (20 bytes, standard Xbox D3D):

| +0x00 Common | type in bits 16-18 = 4 (texture) |
|---|---|
| +0x04 Data | offset of the pixels from the header end (0 in FM1) |
| +0x08 Lock | 0 |
| +0x0C Format | bit 2 cube, bits 4-7 dimensions (2 or 3), bits 8-15 format, 16-19 mip levels, 20-23 / 24-27 / 28-31 log2 width / height / depth |
| +0x10 Size | 0 for swizzled textures; for linear ones width-1 (bits 0-11), height-1 (12-23), pitch/64-1 (24-31) |

With these fields the expected size of every texture (all mip levels, volume depth halving per level, linear pitch)
fills the data exactly; the only slack is the final padding to 2048 (1823/1823).

## 2. What the game uses

| Format | Layout | Files | Examples |
|---|---|---|---|
| 0x0F DXT5 | 2D | 817 | `livery0hi.xpr` (1024x1024, 11 levels), wheels, UI |
| 0x0F DXT5 | **volume** | 518 | `livery0.xpr` (512x512x2), `wheel_vol.xpr` (64x64x4) |
| 0x06 A8R8G8B8 | **volume**, swizzled | 243 | `livery0nc.xpr` (512x512x2) |
| 0x12 LIN_A8R8G8B8 | linear, pitch = row rounded up to 64 bytes | 227 | front-end icons and text |
| 0x0C DXT1 | 2D | 12 | loading screens |
| 0x0C DXT1 | volume | 1 | `temp\headlightsbusted.xpr` (128x128x8) |
| 0x00 L8 | 2D, swizzled | 2 | `cars\carmedia\grille1/2.xpr` |
| 0x06 A8R8G8B8 | 2D, swizzled | 1 | `drivers\driver.xpr` |
| 0x0F DXT5 | cube | 2 | `frontend\showroom.xpr` |

## 3. Pixel order (checked by eye)

- **DXT, 2D:** 4x4 blocks row by row (not swizzled).
- **DXT, volume:** groups of 4x4x4 texels. For each block position the blocks of the (up to) 4 slices of the group
  follow each other; groups go row by row, then by z. With depth 2 a group has 2 slices. A 3D Morton order on the
  blocks and "one slice after the other" both give garbage. Confirmed on depth 2, 4 and 8.
- **Uncompressed, swizzled:** Morton order, the bits of x, y (and z for volumes) interleaved in that order while each
  axis still has bits. Confirmed on `livery0nc` (volume) and `grille1` (2D).
- **Linear:** rows of `pitch` bytes.
- **Cube:** not decoded yet (only face 0 is written).

## 4. What the volumes are

- `livery0.xpr` / `livery0nc.xpr` (depth 2): **slice 0 = clean livery, slice 1 = damaged livery** (scratches,
  dirt, cracked lights). Probably the game blends the two along z with the damage amount. `livery0hi.xpr` is the
  same clean livery as a 2D 1024x1024 DXT5 with mips. `nc` is the uncompressed copy.
- `wheel_vol.xpr` (depth 4): the wheel with **growing motion blur**, from still to fast.
- `headlightsbusted.xpr` (depth 8): 8 frames of a headlight beam.

## 5. Where the car textures are

There is no `shared.cab`. The FM1 equivalent of FM2+ `media\cars\shared.zip` is **`MegaXPR.ca2`**:
- `cars\carmedia\grille1/2.xpr` (L8 grille meshes);
- `cars\cardropshadow.xpr` and `tiredropshadow.xpr` (shadows);
- `temp\glass.xpr`, `temp\headlights.xpr` and `headlightsbusted.xpr` (lights);
- `tires\art\<wheel>\wheel.xpr` / `wheel_vol.xpr` (543 files).

Each car archive holds **one atlas** only: `cars\<car>\liveries\standard\livery0*.xpr`. The paint colour and the
shader come from `livery<n>.livery` (XML: `<override red green blue texture="livery0"/>`,
`<shaders name="ShaderSettings\nonmetallic.xml"/>`).

## 6. Open

1. Cube maps: face order and alignment (only 2 front-end files).
2. Which of `livery0` / `livery0nc` / `livery0hi` the game uses when, and how the car `.carbin` UVs map onto them
   (needed for an FM1 -> FM4 port: FM4 wants a 2D texture, so slice 0 of the volume or `livery0hi`).
3. Writing XPR (only needed to change FM1 itself).
