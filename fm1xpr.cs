// fm1xpr: FM1 (original Xbox) XPR0 texture -> PNG (top mip; volume slices side by side).
//
//   fm1xpr <file.xpr> <out.png>
//   fm1xpr <archive.cab|.ca2> <entry path> <out.png>
//   fm1xpr info <file.xpr | archive entry>
//
// Build (with the CAB reader): csc /nologo /platform:x86 /optimize /main:Fm1Xpr /r:System.Drawing.dll fm1xpr.cs fm1cab.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

class XprTexture
{
    public int Format, Levels, Width, Height, Depth, Dims, Pitch;
    public bool Cube, Linear;
    public int DataStart;
    public byte[] D;

    public static XprTexture Parse(byte[] d)
    {
        if (d.Length < 32 || Encoding.ASCII.GetString(d, 0, 4) != "XPR0") throw new InvalidDataException("not XPR0");
        uint header = BitConverter.ToUInt32(d, 8), common = BitConverter.ToUInt32(d, 12);
        if (((common >> 16) & 7) != 4) throw new InvalidDataException("first resource is not a texture");
        uint fmt = BitConverter.ToUInt32(d, 24), size = BitConverter.ToUInt32(d, 28);
        var t = new XprTexture { D = d };
        t.DataStart = (int)(header + BitConverter.ToUInt32(d, 16));
        t.Cube = (fmt & 4) != 0;
        t.Dims = (int)(fmt >> 4) & 15;
        t.Format = (int)(fmt >> 8) & 0xFF;
        t.Levels = (int)(fmt >> 16) & 15;
        t.Linear = size != 0;
        if (t.Linear)
        {
            t.Width = (int)(size & 0xFFF) + 1;
            t.Height = (int)((size >> 12) & 0xFFF) + 1;
            t.Pitch = (int)((size >> 24) + 1) * 64;
        }
        else
        {
            t.Width = 1 << (int)((fmt >> 20) & 15);
            t.Height = 1 << (int)((fmt >> 24) & 15);
        }
        t.Depth = t.Dims == 3 ? 1 << (int)((fmt >> 28) & 15) : 1;
        return t;
    }

    public string Describe()
    {
        return string.Format("format 0x{0:X2} {1}x{2}{3} levels {4}{5}{6}", Format, Width, Height, Depth > 1 ? "x" + Depth : "", Levels,
            Linear ? " linear pitch " + Pitch : " swizzled", Cube ? " cube" : "");
    }

    // Morton address on the NV2A: bits of x, y (and z) interleaved in that order while each axis still has bits.
    public static int Swizzle(int x, int y, int z, int w, int h, int d)
    {
        int addr = 0, bit = 0;
        for (int i = 1; i < w || i < h || i < d; i <<= 1)
        {
            if (i < w) { if ((x & i) != 0) addr |= 1 << bit; bit++; }
            if (i < h) { if ((y & i) != 0) addr |= 1 << bit; bit++; }
            if (i < d) { if ((z & i) != 0) addr |= 1 << bit; bit++; }
        }
        return addr;
    }

    // Top mip as BGRA rows; volume slices are placed side by side (width * depth).
    public int[] DecodeTop(out int outW, out int outH)
    {
        int w = Width, h = Height, dep = Depth;
        outW = w * dep;
        outH = h;
        var px = new int[outW * outH];
        int f = Format;
        if (f == 0x0C || f == 0x0E || f == 0x0F)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4), bs = f == 0x0C ? 8 : 16;
            var block = new int[16];
            for (int z = 0; z < dep; z++)
                for (int by = 0; by < bh; by++)
                    for (int bx = 0; bx < bw; bx++)
                    {
                        // 2D DXT: blocks row by row. Volume DXT: 4x4x4 groups, i.e. for each block position the
                        // (up to) 4 slices of the group follow each other; groups row by row, then by z.
                        int g = Math.Min(4, dep);
                        int src = DataStart + ((((z / 4) * bh + by) * bw + bx) * g + z % 4) * bs;
                        Dxt.Block(D, src, f, block);
                        for (int py = 0; py < 4; py++)
                            for (int pxi = 0; pxi < 4; pxi++)
                            {
                                int x = bx * 4 + pxi, y = by * 4 + py;
                                if (x < w && y < h) px[y * outW + z * w + x] = block[py * 4 + pxi];
                            }
                    }
            return px;
        }
        int bpp = f == 0x00 || f == 0x13 || f == 0x19 ? 1 : f == 0x05 || f == 0x04 || f == 0x02 || f == 0x03 || f == 0x11 ? 2 : 4;
        for (int z = 0; z < dep; z++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int src = DataStart + (Linear ? y * Pitch + x * bpp : Swizzle(x, y, z, w, h, dep) * bpp);
                    px[y * outW + z * w + x] = Pixel(D, src, f);
                }
        return px;
    }

    static int Pixel(byte[] d, int o, int f)
    {
        switch (f)
        {
            case 0x06: case 0x12: return BitConverter.ToInt32(d, o);                         // A8R8G8B8 (BGRA in memory)
            case 0x07: case 0x1E: return BitConverter.ToInt32(d, o) | unchecked((int)0xFF000000);
            case 0x3A: case 0x3F: { int v = BitConverter.ToInt32(d, o); return (v & unchecked((int)0xFF00FF00)) | ((v >> 16) & 0xFF) | ((v & 0xFF) << 16); }
            case 0x00: case 0x13: { int l = d[o]; return unchecked((int)0xFF000000) | l << 16 | l << 8 | l; }
            case 0x19: return d[o] << 24 | 0xFFFFFF;
            case 0x05: case 0x11: return Dxt.Rgb565(BitConverter.ToUInt16(d, o)) | unchecked((int)0xFF000000);
        }
        throw new NotSupportedException("format 0x" + f.ToString("X2"));
    }
}

static class Dxt
{
    public static int Rgb565(int c)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        return (r << 3 | r >> 2) << 16 | (g << 2 | g >> 4) << 8 | (b << 3 | b >> 2);
    }

    static int Mix(int a, int b, int wa, int wb, int div)
    {
        int r = (((a >> 16) & 255) * wa + ((b >> 16) & 255) * wb) / div;
        int g = (((a >> 8) & 255) * wa + ((b >> 8) & 255) * wb) / div;
        int bl = ((a & 255) * wa + (b & 255) * wb) / div;
        return r << 16 | g << 8 | bl;
    }

    public static void Block(byte[] d, int o, int f, int[] outp)
    {
        int co = f == 0x0C ? o : o + 8;
        int c0 = BitConverter.ToUInt16(d, co), c1 = BitConverter.ToUInt16(d, co + 2);
        uint bits = BitConverter.ToUInt32(d, co + 4);
        int p0 = Rgb565(c0), p1 = Rgb565(c1);
        var pal = new int[4];
        var palA = new int[] { 255, 255, 255, 255 };
        pal[0] = p0; pal[1] = p1;
        if (c0 > c1 || f != 0x0C) { pal[2] = Mix(p0, p1, 2, 1, 3); pal[3] = Mix(p0, p1, 1, 2, 3); }
        else { pal[2] = Mix(p0, p1, 1, 1, 2); pal[3] = 0; palA[3] = 0; }
        var alpha = new int[16];
        if (f == 0x0E)
        {
            ulong ab = BitConverter.ToUInt64(d, o);
            for (int i = 0; i < 16; i++) alpha[i] = (int)((ab >> (4 * i)) & 15) * 17;
        }
        else if (f == 0x0F)
        {
            int a0 = d[o], a1 = d[o + 1];
            ulong ab = 0;
            for (int i = 0; i < 6; i++) ab |= (ulong)d[o + 2 + i] << (8 * i);
            var ap = new int[8];
            ap[0] = a0; ap[1] = a1;
            if (a0 > a1) for (int i = 1; i < 7; i++) ap[i + 1] = ((7 - i) * a0 + i * a1) / 7;
            else { for (int i = 1; i < 5; i++) ap[i + 1] = ((5 - i) * a0 + i * a1) / 5; ap[6] = 0; ap[7] = 255; }
            for (int i = 0; i < 16; i++) alpha[i] = ap[(int)((ab >> (3 * i)) & 7)];
        }
        for (int i = 0; i < 16; i++)
        {
            int idx = (int)((bits >> (2 * i)) & 3);
            int a = f == 0x0C ? palA[idx] : alpha[i];
            outp[i] = a << 24 | pal[idx];
        }
    }
}

static class Fm1Xpr
{
    static int Main(string[] a)
    {
        try
        {
            if (a.Length == 2 && a[0] == "check") return CheckAll(a[1]);
            if (a.Length >= 2 && a[0] == "info")
            {
                Console.WriteLine(XprTexture.Parse(Load(a.Skip(1).ToArray())).Describe());
                return 0;
            }
            if (a.Length == 2 || a.Length == 3)
            {
                var t = XprTexture.Parse(Load(a.Take(a.Length - 1).ToArray()));
                int w, h;
                int[] px = t.DecodeTop(out w, out h);
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, bd.Scan0 + y * bd.Stride, w);
                    bmp.UnlockBits(bd);
                    bmp.Save(a[a.Length - 1], ImageFormat.Png);
                }
                Console.WriteLine("{0} -> {1} ({2}x{3})", t.Describe(), a[a.Length - 1], w, h);
                return 0;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
        Console.WriteLine("fm1xpr <file.xpr> <out.png> | fm1xpr <archive> <entry> <out.png> | fm1xpr info ...");
        return 1;
    }

    // Decodes every .xpr of every archive in memory; lists the kinds and any failure, plus one example path per kind.
    static int CheckAll(string folder)
    {
        int n = 0, bad = 0;
        var kinds = new System.Collections.Generic.SortedDictionary<string, string>();
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var arc in Directory.GetFiles(folder, "*.cab").Concat(Directory.GetFiles(folder, "*.ca2")).OrderBy(x => x))
        {
            var cab = Fm1Cab.Load(arc);
            foreach (var e in cab.Entries.Where(x => x.Path.EndsWith(".xpr", StringComparison.OrdinalIgnoreCase)))
            {
                n++;
                string where = Path.GetFileName(arc) + " " + cab.FullPath(e);
                try
                {
                    var t = XprTexture.Parse(cab.Payload(e));
                    string k = string.Format("0x{0:X2} {1}{2}{3}", t.Format, t.Linear ? "linear" : "swizzled", t.Depth > 1 ? " volume" : "", t.Cube ? " cube" : "");
                    int w, h;
                    t.DecodeTop(out w, out h);
                    if (!kinds.ContainsKey(k)) kinds[k] = where;
                    int c; counts.TryGetValue(k, out c); counts[k] = c + 1;
                }
                catch (Exception ex) { bad++; Console.WriteLine("FAIL {0}: {1}", where, ex.Message); }
            }
        }
        Console.WriteLine("{0} xpr decoded, {1} failed", n, bad);
        foreach (var kv in kinds) Console.WriteLine("  {0,-26} x{1,-5} e.g. {2}", kv.Key, counts[kv.Key], kv.Value);
        return bad == 0 ? 0 : 3;
    }

    static byte[] Load(string[] args)
    {
        if (args.Length == 1) return File.ReadAllBytes(args[0]);
        var cab = Fm1Cab.Load(args[0]);
        var e = cab.Entries.FirstOrDefault(x => string.Equals(cab.FullPath(x), args[1], StringComparison.OrdinalIgnoreCase));
        if (e == null) throw new FileNotFoundException(args[1] + " not in " + args[0]);
        return cab.Payload(e);
    }
}
