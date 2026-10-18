#!/usr/bin/env python3
"""
Forza Motorsport 1 / original Xbox XPR0 -> JPEG converter.

Supports the Xbox XPR0 one-resource texture layout used by the supplied FM1
livery samples. Pillow is required: python -m pip install Pillow

Examples:
  python fm1_xpr_to_jpeg.py livery0.xpr
  python fm1_xpr_to_jpeg.py livery0hi.xpr -o livery0hi.jpg
  python fm1_xpr_to_jpeg.py texture.xpr --mip 2
  python fm1_xpr_to_jpeg.py texture.xpr --list-mips

The converter treats the XPR resource's GPU format word as:
  bits  8..15: pixel format
  bits 16..19: mip-level count (Xbox convention: levels stored, base level is 0)
  bits 20..23: log2(width)
  bits 24..27: log2(height)
  bits 28..31: log2(depth)

For the current FM1 samples, DXT5 is format 0x0F and A8R8G8B8 is 0x06.
When a resource contains extra surface/layer data after the declared mip chain,
only the selected mip is decoded; extra bytes are reported rather than guessed.
"""
from __future__ import annotations

import argparse
import math
import struct
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Errore: Pillow non installato. Esegui: python -m pip install Pillow", file=sys.stderr)
    raise SystemExit(2)

XPR_MAGIC = b"XPR0"
XPR_HEADER_MIN = 12
RESOURCE_HEADER_OFFSET = 12
TEXTURE_FORMAT_OFFSET = 24
TEXTURE_SIZE_OFFSET = 28

FORMAT_NAMES = {
    0x02: "A1R5G5B5",
    0x03: "X1R5G5B5",
    0x04: "A4R4G4B4",
    0x05: "R5G6B5",
    0x06: "A8R8G8B8",
    0x07: "X8R8G8B8",
    0x0B: "I8_A8R8G8B8",
    0x0C: "DXT1",
    0x0E: "DXT3",
    0x0F: "DXT5",
    0x11: "LU_R5G6B5",
    0x12: "LU_A8R8G8B8",
    0x19: "A8",
    0x1E: "LU_X8R8G8B8",
    0x3A: "A8B8G8R8",
    0x3F: "LU_A8B8G8R8",
}

def read_header(data: bytes) -> dict:
    if len(data) < 32 or data[:4] != XPR_MAGIC:
        raise ValueError("File non riconosciuto: attesa firma XPR0.")
    total_size, header_size = struct.unpack_from("<II", data, 4)
    common = struct.unpack_from("<I", data, 12)[0]
    count = common & 0xFFFF
    resource_type = (common >> 16) & 0x7
    fmt_word, size_word = struct.unpack_from("<II", data, TEXTURE_FORMAT_OFFSET)
    if total_size > len(data) or total_size < 32:
        raise ValueError(f"Dimensione XPR non valida: header={total_size}, file={len(data)}")
    if header_size < 32 or header_size > total_size or header_size % 4:
        raise ValueError(f"Dimensione header non valida: {header_size}")
    if count != 1:
        raise ValueError(f"Questa versione richiede un solo resource (trovati {count}).")
    if resource_type != 4:
        raise ValueError(f"Il resource non è una texture (type={resource_type}).")
    dimensions = (fmt_word >> 4) & 0xF
    fmt = (fmt_word >> 8) & 0xFF
    levels = (fmt_word >> 16) & 0xF
    width_exp = (fmt_word >> 20) & 0xF
    height_exp = (fmt_word >> 24) & 0xF
    depth_exp = (fmt_word >> 28) & 0xF
    # If width/height exponent fields are zero, Xbox uses the second word's
    # explicit width/height fields (stored as value minus one).
    if width_exp == 0 or height_exp == 0:
        width = (size_word & 0xFFF) + 1
        height = ((size_word >> 12) & 0xFFF) + 1
    else:
        width = 1 << width_exp
        height = 1 << height_exp
    return {
        "total_size": total_size,
        "header_size": header_size,
        "resource_count": count,
        "resource_type": resource_type,
        "dimensions": dimensions,
        "format_word": fmt_word,
        "size_word": size_word,
        "format": fmt,
        "format_name": FORMAT_NAMES.get(fmt, f"UNKNOWN_0x{fmt:02X}"),
        "levels": max(1, levels),
        "width": width,
        "height": height,
        "depth": 1 << depth_exp if depth_exp else 1,
    }

def mip_dimensions(width: int, height: int, level: int) -> tuple[int, int]:
    return max(1, width >> level), max(1, height >> level)

def mip_size(fmt: int, width: int, height: int) -> int:
    if fmt in (0x0C, 0x0E, 0x0F):
        block_bytes = 8 if fmt == 0x0C else 16
        return max(1, (width + 3) // 4) * max(1, (height + 3) // 4) * block_bytes
    bpp = {
        0x02: 2, 0x03: 2, 0x04: 2, 0x05: 2,
        0x06: 4, 0x07: 4, 0x11: 2, 0x12: 4,
        0x19: 1, 0x1E: 4, 0x3A: 4, 0x3F: 4,
    }.get(fmt)
    if bpp is None:
        raise ValueError(f"Formato non ancora supportato: 0x{fmt:02X}")
    return width * height * bpp

def expand_565(v: int) -> tuple[int, int, int]:
    r = (v >> 11) & 31
    g = (v >> 5) & 63
    b = v & 31
    return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2))

def decode_dxt1(data: bytes, width: int, height: int) -> Image.Image:
    img = Image.new("RGBA", (width, height))
    p = 0
    for by in range((height + 3) // 4):
        for bx in range((width + 3) // 4):
            if p + 8 > len(data):
                raise ValueError("Dati DXT1 insufficienti.")
            c0, c1, bits = struct.unpack_from("<HHI", data, p); p += 8
            rgb0, rgb1 = expand_565(c0), expand_565(c1)
            if c0 > c1:
                palette = [(*rgb0,255), (*rgb1,255),
                           tuple((2*rgb0[i]+rgb1[i])//3 for i in range(3))+(255,),
                           tuple((rgb0[i]+2*rgb1[i])//3 for i in range(3))+(255,)]
            else:
                palette = [(*rgb0,255), (*rgb1,255),
                           tuple((rgb0[i]+rgb1[i])//2 for i in range(3))+(255,),
                           (0,0,0,0)]
            for py in range(4):
                for px in range(4):
                    x,y = bx*4+px, by*4+py
                    if x < width and y < height:
                        idx = (bits >> (2*(py*4+px))) & 3
                        img.putpixel((x,y), palette[idx])
    return img

def decode_dxt3(data: bytes, width: int, height: int) -> Image.Image:
    img = Image.new("RGBA", (width, height))
    p = 0
    for by in range((height + 3)//4):
        for bx in range((width + 3)//4):
            if p + 16 > len(data): raise ValueError("Dati DXT3 insufficienti.")
            alpha_bits = int.from_bytes(data[p:p+8], "little")
            c0,c1,bits = struct.unpack_from("<HHI", data, p+8); p += 16
            rgb0,rgb1=expand_565(c0),expand_565(c1)
            palette=[(*rgb0,255),(*rgb1,255),
                     tuple((2*rgb0[i]+rgb1[i])//3 for i in range(3))+(255,),
                     tuple((rgb0[i]+2*rgb1[i])//3 for i in range(3))+(255,)]
            for py in range(4):
                for px in range(4):
                    x,y=bx*4+px,by*4+py
                    if x<width and y<height:
                        i=py*4+px; c=palette[(bits>>(2*i))&3]
                        a=((alpha_bits>>(4*i))&15)*17
                        img.putpixel((x,y),(c[0],c[1],c[2],a))
    return img

def decode_dxt5(data: bytes, width: int, height: int) -> Image.Image:
    img = Image.new("RGBA", (width, height))
    p = 0
    for by in range((height + 3)//4):
        for bx in range((width + 3)//4):
            if p + 16 > len(data): raise ValueError("Dati DXT5 insufficienti.")
            a0,a1=data[p],data[p+1]
            alpha_bits=int.from_bytes(data[p+2:p+8],"little")
            c0,c1,bits=struct.unpack_from("<HHI",data,p+8); p += 16
            if a0 > a1:
                alphas=[a0,a1]+[((7-i)*a0+i*a1)//7 for i in range(1,7)]
            else:
                alphas=[a0,a1]+[((5-i)*a0+i*a1)//5 for i in range(1,5)]+[0,255]
            rgb0,rgb1=expand_565(c0),expand_565(c1)
            palette=[(*rgb0,255),(*rgb1,255),
                     tuple((2*rgb0[i]+rgb1[i])//3 for i in range(3))+(255,),
                     tuple((rgb0[i]+2*rgb1[i])//3 for i in range(3))+(255,)]
            for py in range(4):
                for px in range(4):
                    x,y=bx*4+px,by*4+py
                    if x<width and y<height:
                        i=py*4+px
                        c=palette[(bits>>(2*i))&3]
                        a=alphas[(alpha_bits>>(3*i))&7]
                        img.putpixel((x,y),(c[0],c[1],c[2],a))
    return img

def unswizzle(data: bytes, width: int, height: int, bpp: int) -> bytes:
    """Xbox-style unswizzle adapted from XboxToolkit's XprUtility.Unswizzle."""
    out = bytearray(width * height * bpp)
    for y in range(height):
        sy = 0
        if y < width:
            for bit in range(16):
                sy |= ((y >> bit) & 1) << (2 * bit)
            sy <<= 1
        else:
            y_mask = y % width
            for bit in range(16):
                sy |= ((y_mask >> bit) & 1) << (2 * bit)
            sy <<= 1
            sy += (y // width) * width * width
        d = y * width * bpp
        for x in range(width):
            sx = 0
            if x < height * 2:
                for bit in range(16):
                    sx |= ((x >> bit) & 1) << (2 * bit)
            else:
                x_mask = x % (2 * height)
                for bit in range(16):
                    sx |= ((x_mask >> bit) & 1) << (2 * bit)
                sx += (x // (2 * height)) * 2 * height * height
            src_off = (sx + sy) * bpp
            dst_off = d
            if src_off + bpp <= len(data):
                out[dst_off:dst_off+bpp] = data[src_off:src_off+bpp]
            d += bpp
    return bytes(out)

def decode_raw_linear(raw: bytes, width: int, height: int, fmt: int) -> Image.Image:
    """Decode uncompressed pixel bytes already in linear row-major order."""
    if fmt in (0x06, 0x07, 0x12, 0x1E, 0x3A, 0x3F):
        img=Image.new("RGBA",(width,height)); px=[]
        for i in range(0,width*height*4,4):
            b0,b1,b2,b3=raw[i:i+4]
            if fmt in (0x3A,0x3F): r,g,b,a=b0,b1,b2,b3
            else:
                b,g,r,a=b0,b1,b2,b3
                if fmt in (0x07,0x1E): a=255
            px.append((r,g,b,a))
        img.putdata(px); return img
    raise ValueError(f"Formato raw lineare 0x{fmt:02X} non supportato.")

def decode_raw(data: bytes, width: int, height: int, fmt: int) -> Image.Image:
    if fmt in (0x06, 0x07, 0x12, 0x1E, 0x3A, 0x3F):
        bpp=4
        raw=unswizzle(data, width, height, bpp)
        img=Image.new("RGBA",(width,height))
        px=[]
        for i in range(0,width*height*4,4):
            b0,b1,b2,b3=raw[i:i+4]
            if fmt in (0x3A,0x3F): # A8B8G8R8: little-endian bytes are R,G,B,A
                r,g,b,a=b0,b1,b2,b3
            elif fmt in (0x12,0x1E): # linear-image variants
                b,g,r,a=b0,b1,b2,b3
                if fmt == 0x1E: a=255
            else: # A8R8G8B8 / X8R8G8B8, little-endian memory BGRA
                b,g,r,a=b0,b1,b2,b3
                if fmt == 0x07: a=255
            px.append((r,g,b,a))
        img.putdata(px)
        return img
    if fmt in (0x04,0x05,0x02,0x03):
        raw=unswizzle(data,width,height,2)
        img=Image.new("RGBA",(width,height)); px=[]
        for i in range(0,width*height*2,2):
            v=raw[i] | (raw[i+1]<<8)
            if fmt==0x05:
                r,g,b=expand_565(v); a=255
            elif fmt in (0x02,0x03):
                r=((v>>10)&31)*255//31; g=((v>>5)&31)*255//31; b=(v&31)*255//31
                a=((v>>15)&1)*255 if fmt==0x02 else 255
            else:
                a=((v>>12)&15)*17; r=((v>>8)&15)*17; g=((v>>4)&15)*17; b=(v&15)*17
            px.append((r,g,b,a))
        img.putdata(px); return img
    raise ValueError(f"Formato raw 0x{fmt:02X} non supportato.")

def unswizzle_volume(data: bytes, width: int, height: int, depth: int, unit_bytes: int) -> bytes:
    """Unsizzle a power-of-two Xbox 3D texture (or compressed block volume).

    The address uses interleaved x/y/z bits (Morton order). For DXT, width and
    height are measured in 4x4 blocks and unit_bytes is the block size.
    """
    out = bytearray(width * height * depth * unit_bytes)
    def morton3(x: int, y: int, z: int) -> int:
        idx = 0
        bit = 0
        while (1 << bit) < max(width, height, depth):
            if (1 << bit) < width:
                idx |= ((x >> bit) & 1) << (3 * bit)
            if (1 << bit) < height:
                idx |= ((y >> bit) & 1) << (3 * bit + 1)
            if (1 << bit) < depth:
                idx |= ((z >> bit) & 1) << (3 * bit + 2)
            bit += 1
        return idx
    for z in range(depth):
        for y in range(height):
            for x in range(width):
                src = morton3(x, y, z) * unit_bytes
                dst = ((z * height + y) * width + x) * unit_bytes
                if src + unit_bytes <= len(data):
                    out[dst:dst+unit_bytes] = data[src:src+unit_bytes]
    return bytes(out)

def decode_xpr(data: bytes, mip: int = 0, slice_index: int = 0) -> tuple[Image.Image, dict]:
    h=read_header(data)
    if h["dimensions"] == 3 and h["depth"] > 1:
        raise ValueError(
            "Questa texture è un volume 3D (depth > 1). Il layout di swizzle 3D "
            "di FM1 non è ancora validato; conversione bloccata per evitare un JPEG corrotto. "
            "Usa per ora una texture 2D come livery0hi.xpr."
        )
    if mip < 0 or mip >= h["levels"]:
        raise ValueError(f"Mipmap {mip} fuori range (livelli dichiarati: {h['levels']}).")
    payload=data[h["header_size"]:h["total_size"]]
    width,height=h["width"],h["height"]
    if slice_index < 0 or slice_index >= h["depth"]:
        raise ValueError(f"Slice {slice_index} fuori range (depth dichiarata: {h['depth']}).")
    base_slice_size = sum(mip_size(h["format"], *mip_dimensions(width, height, level)) for level in range(h["levels"]))
    slice_base = slice_index * base_slice_size
    offset=slice_base
    for level in range(mip):
        w,hh=mip_dimensions(width,height,level)
        offset += mip_size(h["format"],w,hh)
    w,hh=mip_dimensions(width,height,mip)
    needed=mip_size(h["format"],w,hh)
    if offset+needed > len(payload):
        raise ValueError(
            f"Payload insufficiente per mip {mip}: servono {offset+needed} byte, "
            f"disponibili {len(payload)}. Formato/dimensioni o layout non standard."
        )
    chunk=payload[offset:offset+needed]
    fmt=h["format"]
    # FM1 livery0/livery0nc samples declare a 3D resource with depth=2.
    # Their data is interleaved in 3D swizzle order, unlike the 2D hi texture.
    if h["dimensions"] == 3 and h["depth"] > 1:
        if fmt in (0x0C, 0x0E, 0x0F):
            block_bytes = 8 if fmt == 0x0C else 16
            bw, bh = max(1, (w+3)//4), max(1, (hh+3)//4)
            volume_size = bw * bh * h["depth"] * block_bytes
            volume = payload[offset:offset+volume_size]
            if len(volume) < volume_size:
                raise ValueError("Dati insufficienti per texture DXT 3D.")
            linear = unswizzle_volume(volume, bw, bh, h["depth"], block_bytes)
            slice_size = bw * bh * block_bytes
            chunk = linear[slice_index*slice_size:(slice_index+1)*slice_size]
            # Block data is now linearized for the selected z slice.
        else:
            volume_size = w * hh * h["depth"] * (4 if fmt in (0x06,0x07,0x12,0x1E,0x3A,0x3F) else 2)
            bpp = 4 if fmt in (0x06,0x07,0x12,0x1E,0x3A,0x3F) else 2
            volume = payload[offset:offset+volume_size]
            if len(volume) < volume_size:
                raise ValueError("Dati insufficienti per texture raw 3D.")
            linear = unswizzle_volume(volume, w, hh, h["depth"], bpp)
            slice_size = w * hh * bpp
            chunk = linear[slice_index*slice_size:(slice_index+1)*slice_size]
            # Avoid a second 2D unswizzle for the already linearized slice.
            image = decode_raw_linear(chunk,w,hh,fmt)
    if not (h["dimensions"] == 3 and h["depth"] > 1 and fmt not in (0x0C,0x0E,0x0F)):
        if fmt==0x0C: image=decode_dxt1(chunk,w,hh)
        elif fmt==0x0E: image=decode_dxt3(chunk,w,hh)
        elif fmt==0x0F: image=decode_dxt5(chunk,w,hh)
        else: image=decode_raw(chunk,w,hh,fmt)
    h.update({"mip":mip,"slice":slice_index,"output_width":w,"output_height":hh,
              "payload_bytes":len(payload),"mip_offset":offset,
              "mip_bytes":needed,"extra_payload_bytes":len(payload)-offset-needed})
    return image,h

def main():
    ap=argparse.ArgumentParser(description="Convertitore XPR0 Xbox/FM1 -> JPEG")
    ap.add_argument("input", type=Path, help="file .xpr")
    ap.add_argument("-o","--output", type=Path, help="JPEG di output (default: stesso nome .jpg)")
    ap.add_argument("--mip", type=int, default=0, help="livello mipmap da esportare (default 0)")
    ap.add_argument("--slice", type=int, default=0, help="slice/profondità da esportare (default 0)")
    ap.add_argument("--list-mips", action="store_true", help="mostra dimensioni e byte previsti per ogni mipmap")
    ap.add_argument("--quality", type=int, default=95, help="qualità JPEG 1-100 (default 95)")
    args=ap.parse_args()
    data=args.input.read_bytes()
    h=read_header(data)
    print(f"Input: {args.input} ({len(data):,} byte)")
    print(f"XPR: total={h['total_size']:,}, header={h['header_size']:,}, resources={h['resource_count']}")
    print(f"Texture: {h['width']}x{h['height']}x{h['depth']}, dimensionType={h['dimensions']}, format={h['format_name']} (0x{h['format']:02X}), mips={h['levels']}")
    if args.list_mips:
        for i in range(h["levels"]):
            w,hh=mip_dimensions(h["width"],h["height"],i)
            print(f"  mip {i}: {w}x{hh}, {mip_size(h['format'],w,hh):,} byte")
        return
    image,info=decode_xpr(data,args.mip,args.slice)
    out=args.output or args.input.with_suffix(".jpg")
    out.parent.mkdir(parents=True,exist_ok=True)
    # JPEG has no alpha; composite transparent pixels on black for predictable output.
    if image.mode=="RGBA":
        bg=Image.new("RGB",image.size,(0,0,0))
        bg.paste(image,mask=image.getchannel("A"))
        image=bg
    else:
        image=image.convert("RGB")
    image.save(out, "JPEG", quality=max(1,min(100,args.quality)), optimize=True)
    print(f"Output: {out} ({info['output_width']}x{info['output_height']}, {out.stat().st_size:,} byte)")
    print(f"Slice: {info['slice']}; payload: {info['payload_bytes']:,}; mip selezionata: {info['mip_bytes']:,}; byte residui: {info['extra_payload_bytes']:,}")
    if info["extra_payload_bytes"]:
        print("Nota: il payload contiene dati aggiuntivi oltre al mip selezionato; non sono stati interpretati.")
if __name__=="__main__":
    main()
