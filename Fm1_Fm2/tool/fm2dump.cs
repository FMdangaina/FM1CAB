// fm2dump: walks an FM2 (Xbox 360) car .carbin (type 2, big-endian) along the layout of the
// legacy ForzaCarBin "Two" reader and prints every field it skips. Research only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

static class Fm2Dump
{
    static byte[] d;
    static int p;
    static TextWriter o = Console.Out;

    static uint U32(int at) { return (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]); }
    static int I32() { int v = (int)U32(p); p += 4; return v; }
    static float F32At(int at) { return BitConverter.ToSingle(BitConverter.GetBytes(U32(at)), 0); }
    static float F32() { float v = F32At(p); p += 4; return v; }
    static string F(float f) { return f.ToString("0.#####", CultureInfo.InvariantCulture); }
    static string V3() { return F(F32()) + " " + F(F32()) + " " + F(F32()); }
    static string Hex(int at, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len && at + i < d.Length; i++) sb.Append(d[at + i].ToString("x2")).Append(i % 4 == 3 ? " " : "");
        return sb.ToString().Trim();
    }
    static string Floats(int at, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++) sb.Append(F(F32At(at + i * 4))).Append(' ');
        return sb.ToString().Trim();
    }
    static float Half(int at)
    {
        int h = d[at] << 8 | d[at + 1];
        int s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023;
        float v = e == 0 ? m / 1024f * (float)Math.Pow(2, -14) : (1 + m / 1024f) * (float)Math.Pow(2, e - 15);
        return s == 1 ? -v : v;
    }

    static void Main(string[] args)
    {
        d = File.ReadAllBytes(args[0]);
        bool verbose = args.Length > 1 && args[1] == "-v";
        p = 0;
        int type = I32();
        o.WriteLine("type {0}  file {1}", type, d.Length);
        o.WriteLine("0x004 64 floats: {0}", Floats(4, 64));
        p = 0x104;
        o.WriteLine("0x104 {0:X}", I32());
        o.WriteLine("0x108 {0}", I32());
        int nrec = I32();
        o.WriteLine("0x10C records x140: {0}", nrec);
        for (int i = 0; i < nrec; i++) { o.WriteLine("   rec{0}: {1}", i, Floats(p, 35)); p += 140; }
        o.WriteLine("{0:X} 0x6c bytes: {1}", p, Hex(p, 0x6c));
        o.WriteLine("    floats: {0}", Floats(p, 27));
        p += 0x6c;
        int lods = I32();
        o.WriteLine("{0:X} draw lists: {1} x2", p - 4, lods);
        for (int i = 0; i < lods * 2; i++) { int c = I32(); o.WriteLine("   list{0}: {1} entries", i, c); p += c * 4; }
        o.WriteLine("{0:X} word {1}", p, I32());
        int nsec = I32();
        o.WriteLine("sections: {0}", nsec);
        var shared = new List<string>();
        for (int s = 0; s < nsec; s++)
        {
            int start = p;
            uint marker = U32(p);
            bool hasMarker = marker == 0x2c9 || marker == 2 || marker == 5;
            if (hasMarker) p += 4;
            string off = V3(), mn = V3(), mx = V3();
            string x1c = Hex(p, 0x1c); string x1cf = Floats(p, 7); p += 0x1c;
            int na = I32(); p += na * 16;
            string w1 = Hex(p, 4); p += 4;
            int nb = I32(); p += nb * 2;
            string w2 = Hex(p, 4); p += 4;
            uint vb1 = (uint)I32(), vb2 = (uint)I32();
            int nl = d[p++]; string name = Encoding.ASCII.GetString(d, p, nl); p += nl;
            string w3 = Hex(p, 8); p += 8;
            int lvc = I32(), lvs = I32();
            int lvstart = p;
            p += lvc * lvs;
            string w4 = Hex(p, 4); p += 4;
            int nsub = I32();
            o.WriteLine("\n[{0}] {1:X6} '{2}' marker {3}  offset {4}  min {5}  max {6}", s, start, name, hasMarker ? marker.ToString("X") : "-", off, mn, mx);
            o.WriteLine("   1c: {0}\n       f: {1}", x1c, x1cf);
            o.WriteLine("   arrayA {0} x16  w {1}  arrayB {2} x2  w {3}  vbTest {4:X8} {5:X8}  w3 {6}", na, w1, nb, w2, vb1, vb2, w3);
            o.WriteLine("   LOD VB {0} x{1}  w4 {2}  subsections {3}", lvc, lvs, w4, nsub);
            if (verbose && lvc > 0) for (int v = 0; v < Math.Min(2, lvc); v++) o.WriteLine("     lv{0}: {1}", v, Hex(lvstart + v * lvs, lvs));
            for (int k = 0; k < nsub; k++)
            {
                string pre = Hex(p, 8); p += 8;
                int snl = I32(); string sname = Encoding.ASCII.GetString(d, p, snl); p += snl;
                int itype = I32(), lod = I32(), three = I32();
                int ic = I32(), isz = I32();
                int istart = p; p += ic * isz;
                int imax = -1, restarts = 0;
                for (int q = 0; q < ic; q++)
                {
                    int ix = isz == 2 ? (d[istart + q * 2] << 8 | d[istart + q * 2 + 1]) : (int)U32(istart + q * 4);
                    if ((isz == 2 && ix == 0xFFFF) || (isz == 4 && ix >= 0xFFFFFF)) { restarts++; continue; }
                    imax = Math.Max(imax, ix);
                }
                o.WriteLine("   sub{0}: pre {1}  '{2}' itype {3} lod {4} ({5}) idx {6} x{7} max {8} restarts {9}", k, pre, sname, itype, lod, three, ic, isz, imax, restarts);
            }
            if (vb1 == vb2 || vb1 == 0)
            {
                o.WriteLine("   (uses shared {0} VB)", vb1 == vb2 ? "glass" : "body");
            }
            else
            {
                string w5 = Hex(p, 4); p += 4;
                int vc = I32(), vs = I32();
                int vstart = p;
                p += vc * vs;
                o.WriteLine("   lod0 VB {0} x{1}  w5 {2}", vc, vs, w5);
                if (verbose && vc > 0) for (int v = 0; v < Math.Min(2, vc); v++) o.WriteLine("     v{0}: {1}", v, Hex(vstart + v * vs, vs));
            }
        }
        o.WriteLine("\n{0:X} after sections: {1}", p, Hex(p, 8)); p += 8;
        int gc = I32(), gs = I32(); string gw = Hex(p, 4); p += 4;
        o.WriteLine("glass VB {0} x{1} w {2} at {3:X}", gc, gs, gw, p);
        DumpHalfVerts(p, gc, gs, verbose); p += gc * gs;
        o.WriteLine("{0:X} next 12: {1}", p, Hex(p, 12)); p += 12;
        int bc = I32(), bs = I32(); string bw = Hex(p, 4); p += 4;
        o.WriteLine("body VB {0} x{1} w {2} at {3:X}", bc, bs, bw, p);
        DumpHalfVerts(p, bc, bs, verbose); p += bc * bs;
        o.WriteLine("{0:X} remaining {1} bytes: {2}", p, d.Length - p, Hex(p, Math.Min(64, d.Length - p)));
    }

    static void DumpHalfVerts(int at, int count, int size, bool verbose)
    {
        if (count == 0) return;
        float[] mn = { 1e9f, 1e9f, 1e9f }, mx = { -1e9f, -1e9f, -1e9f };
        for (int v = 0; v < count; v++)
        {
            int b = at + v * size;
            float w = Half(b + 6);
            for (int k = 0; k < 3; k++) { float c = Half(b + k * 2) * w; mn[k] = Math.Min(mn[k], c); mx[k] = Math.Max(mx[k], c); }
        }
        o.WriteLine("   half pos*w bounds {0}..{1} {2}..{3} {4}..{5}", F(mn[0]), F(mx[0]), F(mn[1]), F(mx[1]), F(mn[2]), F(mx[2]));
        if (verbose) for (int v = 0; v < Math.Min(3, count); v++) o.WriteLine("     v{0}: {1}", v, Hex(at + v * size, size));
    }
}
