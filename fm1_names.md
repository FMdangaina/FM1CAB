# Forza Motorsport 1 (Xbox): Turn10 names for meshes, materials and textures

**Last update:** 18 October 2026\
**Sources:** `default.xbe` (strings and the material enum table, read with `xbeptr.cs`), `Mega.ca2`
(`cars\DefaultParts.xml`, `cars\GlobalMaterialSettings.xml`, `cars\ShaderSettings\*.xml`, `cars\WheelStyle.xml`),
the car archives.

## 1. Materials: the subpart number is an enum

The material number of a subpart is Turn10's material enum. `default.xbe` holds it as `{char *name, int value}` pairs
at raw offset 0x50E650 (a second copy at 0x566190). Read it with
`xbeptr <default.xbe> pairs 50E650 36`.

| # | Name | # | Name | # | Name |
|---|---|---|---|---|---|
| 0 | None | 12 | InnerRim | 24 | NamePlate |
| 1 | Body | 13 | BlurredRim | 25 | Badge |
| 2 | Window | 14 | Interior | 26 | Number |
| 3 | TailLights | 15 | Frame | 27 | Misc |
| 4 | HeadLights | 16 | RedGlass | 28 | HeadlightGlass |
| 5 | ReverseLights | 17 | RimChrome | 29 | WhiteTailLights |
| 6 | BlackGlass | 18 | Matte | 30 | TailLightsOn |
| 7 | Chrome | 19 | CarbonFiber | 31 | Reflectors |
| 8 | Tire | 20 | Plastic | 32 | Grille1 |
| 9 | Black | 21 | RubberTrim | 33 | Grille2 |
| 10 | Tire2 | 22 | InnerRimChrome | 34 | Misc2 |
| 11 | Rim | 23 | BlurredRimChrome | 35 | Plastic2 |

- `Rim` (11) has no string of its own: the linker shares the tail of `InnerRim` / `BlurredRim`. That is why a plain
  strings dump shows the list shifted by one after 10.
- Checks: the wheels (`tire.carbin`) use 8, 11, 12 and 13 = Tire, Rim, InnerRim, BlurredRim. On the Supra, the FM2
  names found by vertex votes (`Fm1_Fm2\fm1_vs_fm2_carbin.md` section 3) agree: 1 body, 2 window, 6 black_glass,
  7 chrome, 14 interior, 15 frame, 21 rubber_trim, 31 reflector, 35 plastic2.
- The same names are the sections of `cars\GlobalMaterialSettings.xml`: `Day<Name>` / `Night<Name>`, with shader
  values (SpecularColor/Power, Fresnel*, TwoTone*, Metal*, BaseColor, Top/BottomColor). There are extra sections
  `Ghost`, `Rotor` and `Name` (the night copy of NamePlate). `cars\ShaderSettings\*.xml` (BaseColor00-xx, Metallic,
  NonMetallic, tints, per-car files) override them. A livery picks one with `<shaders name="ShaderSettings\....xml"/>`.
- A second enum follows the materials: `AmbientOcclusion 1, ReflectionOcclusion 2, Diffuse 3, ShadowBuffer 4, White 5`.
  These are probably texture or pass kinds.

## 2. Meshes: part names

- The part name stored in the carbin is Turn10's mesh name: `bumperRb_LOD1`, `hood_LOD1`, `glassLF_LOD1`,
  `14_caliperB_LR`, `driver01`, `Left_forearm`... (564 distinct names in the game). These are the names the OBJ
  export and the viewer show.
- `_LOD1` is part of the stored name even though the part holds all its LOD subparts (LOD 1-4). The game refers to
  parts by the name without the digit: `cars\DefaultParts.xml` lists `body_LOD`, `hood_LOD`, `glass_left_front_LOD`,
  `wheelLF_LOD`... (the parts drawn when no upgrade replaces them). The xbe also has `headlightLup_LOD`,
  `headlightLdown_LOD` (pop-up lights), `wheel_LOD`, `bumperF_LOD`, `wing_LOD`, `glass_*_LOD`.
- Letter suffixes are body-kit variants chosen by the upgrades: `bumperFa..Fl`, `wingraceA/B/C`, `hooda..j`,
  `skirtLa..d`. One car file holds **all** of them, so a viewer shows every kit at once until the variants are switched
  off.
- Brakes: `11_caliperAblue` and `11_rotor_steel` are the xbe defaults. The car XML fields are `Fronts\CaliperPart`,
  `Fronts\RotorPart`, `Rears\...` and `CaliperAngle`.
- Driver: `Head`, `Chest`, `Abdomen`, `Left/Right_Shoulder`, `_upper_arm`, `_forearm`, `_Thigh`, `_Shin`, `driver01`
  (body from `drivers\driver.bin` + `drivers\driver.xpr`).

## 3. Textures

| Texture | Where | Use |
|---|---|---|
| `livery0hi.xpr` | car archive, `cars\<car>\liveries\standard\` | clean atlas, 2D 1024x1024 DXT5 with mips |
| `livery0.xpr` / `livery0nc.xpr` | same | volume, depth 2: clean + damaged (DXT5 / A8R8G8B8) |
| `livery<n>.livery` | same | XML: paint colour override, shader file, texture name (`livery0`) |
| `cars\carmedia\grille1.xpr`, `grille2.xpr` | MegaXPR.ca2 | Grille1/Grille2 materials |
| `cars\carmedia\glass_texture.xpr`, `glass_texture_d.xpr`, `carbonfibervolume.xpr` | named in the xbe, **not in the archives** | glass / carbon fibre (unused or removed) |
| `cars\cardropshadow.xpr`, `tiredropshadow.xpr` | MegaXPR.ca2 | shadows |
| `tires\art\<style>\wheel.xpr`, `wheel_vol.xpr` | MegaXPR.ca2 | rim, rim motion blur (4 slices) |
| `tires\art\tirea_vol.xpr`, `tireb_vol.xpr`, `tirec_vol.xpr` | MegaXPR.ca2 | tread, shared by all wheels (volume) |
| `tires\art\rotor0..3.xpr` + `tires\art\rotors.carbin` | MegaXPR.ca2 / Mega.ca2 | brake discs (`RotorGlowMin/Max`: hot discs) |
| `drivers\driver.xpr` | MegaXPR.ca2 | driver |

The paint is not in a texture: the body areas of the atlas are transparent and the game fills them with the livery
colour and the `Body` shader. The wheel style list (`cars\WheelStyle.xml`: name, `File` = `tires\art` folder) is the
upgrade catalogue. The stock wheel of each car is picked from the binary `forzadb\cardb\*.dat`, which is not decoded.

## 4. Open

1. The car -> stock wheel link (`forzadb\cardb\gamecars.dat` and related files).
2. Which body-kit variant is stock for each car (DefaultParts.xml + upgrade XML), so a viewer can show the stock car only.
3. `tirea/b/c`: which one each wheel uses.
