// fm1dump: walks an FM1 (Xbox) car.carbin with the chunk grammar found by fm1scan and prints
// every part: header bytes, vertex count, vertex bounds, damage records, pivots, attributes and
// subparts (fields + index stats). Research only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

static class Fm1Dump
{
    static byte[] d;
    static int p;
    static TextWriter o = Console.Out;

    static int I32() { int v = BitConverter.ToInt32(d, p); p += 4; return v; }
    static float F32() { float v = BitConverter.ToSingle(d, p); p += 4; return v; }
    static short I16(int at) { return BitConverter.ToInt16(d, at); }
    static string F(float f) { return f.ToString("0.#####", CultureInfo.InvariantCulture); }

    // chunk header: size, version, name; returns end offset
    static int Chunk(string expect, out int ver)
    {
        int start = p, size = I32(); ver = I32();
        int n = 0; while (d[p + n] != 0) n++;
        string name = Encoding.ASCII.GetString(d, p, n); p += n + 1;
        if (expect != null && name != expect) throw new Exception(string.Format("{0:X}: expected {1}, found {2}", start, expect, name));
        return start + size;
    }

    static string Hex(int at, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len && at + i < d.Length; i++) sb.Append(d[at + i].ToString("x2")).Append(i % 4 == 3 ? " " : "");
        return sb.ToString().Trim();
    }

    static string Floats(int at, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++) sb.Append(F(BitConverter.ToSingle(d, at + i * 4))).Append(' ');
        return sb.ToString().Trim();
    }

    static void Main(string[] args)
    {
        d = File.ReadAllBytes(args[0]);
        bool verbose = args.Length > 1 && args[1] == "-v";
        int ver;
        int end = Chunk("Car", out ver);
        o.WriteLine("Car v{0} size {1} (file {2})", ver, end, d.Length);
        int e2 = Chunk("Attribs", out ver);
        o.WriteLine("Attribs v{0}: {1}\n   floats: {2}", ver, Hex(p, e2 - p), Floats(p + 8, (e2 - p - 8) / 4));
        p = e2;
        e2 = Chunk("Driver", out ver);
        o.WriteLine("Driver v{0}: {1}\n   floats: {2}", ver, Hex(p, e2 - p), Floats(p, (e2 - p) / 4));
        p = e2;
        int parts = I32();
        o.WriteLine("parts: {0}", parts);
        var subStats = new Dictionary<string, int>();
        for (int i = 0; i < parts; i++)
        {
            int pstart = p;
            int pend = Chunk("Part", out ver);
            string head = Hex(p, 4); p += 4;
            int nlen = I32(); string pname = Encoding.ASCII.GetString(d, p, nlen); p += nlen;
            // Verts
            int vend = Chunk("Verts", out ver);
            int vc = I32(); int vstart = p;
            int vsize = vc > 0 ? (vend - p) / vc : 0;
            int[] mn = { 99999, 99999, 99999 }, mx = { -99999, -99999, -99999 };
            for (int v = 0; v < vc; v++)
                for (int k = 0; k < 3; k++) { int s = I16(vstart + v * vsize + k * 2); mn[k] = Math.Min(mn[k], s); mx[k] = Math.Max(mx[k], s); }
            p = vend;
            // Damage
            int dend = Chunk("Damage", out ver);
            int dc = I32(); int dsize = dc > 0 ? (dend - p) / dc : 0; int dstart = p;
            p = dend;
            int pvend = Chunk("Pivots", out ver);
            string piv = Hex(p, pvend - p); string pivf = Floats(p + 4, Math.Max(0, (pvend - p - 4) / 4));
            p = pvend;
            int aend = Chunk("Attributes", out ver);
            string attr = Floats(p, (aend - p) / 4);
            p = aend;
            int subs = I32();
            o.WriteLine("\n[{0}] {1:X6} '{2}' head {3}  verts {4} x{5}  damage {6} x{7}  subparts {8}  size {9}", i, pstart, pname, head, vc, vsize, dc, dsize, subs, pend - pstart);
            o.WriteLine("   vert s16 bounds x {0}..{1} y {2}..{3} z {4}..{5}", mn[0], mx[0], mn[1], mx[1], mn[2], mx[2]);
            if (verbose && vc > 0) for (int v = 0; v < Math.Min(vc, 4); v++) o.WriteLine("   v{0}: {1}", v, Hex(vstart + v * vsize, vsize));
            if (verbose && dc > 0) for (int v = 0; v < Math.Min(dc, 3); v++) o.WriteLine("   d{0}: {1}", v, Hex(dstart + v * dsize, dsize));
            o.WriteLine("   pivots: {0}{1}", piv.Length > 40 ? piv.Substring(0, 40) + "..." : piv, pivf.Length > 0 ? "\n      f: " + pivf : "");
            o.WriteLine("   attributes: {0}", attr);
            for (int s = 0; s < subs; s++)
            {
                int sstart = p;
                int send = Chunk("Subpart", out ver);
                int a = I32(), b = I32(), prim = I32(), ic = I32();
                int q0 = I32(), q1 = I32(), q2 = I32(), q3 = I32();
                int istart = p;
                int imin = 65535, imax = -1, restarts = 0;
                for (int k = 0; k < ic; k++) { int ix = BitConverter.ToUInt16(d, istart + k * 2); if (ix == 0xFFFF) { restarts++; continue; } imin = Math.Min(imin, ix); imax = Math.Max(imax, ix); }
                int rest = send - (istart + ic * 2);
                o.WriteLine("   sub{0}: a {1} b {2} prim {3} idx {4} [{5}..{6}] restarts {7}  q {8} {9} {10} {11}{12}", s, a, b, prim, ic, imin, imax, restarts, q0, q1, q2, q3, rest != 0 ? "  extra " + rest : "");
                string key = "a" + a + " b" + b + " prim" + prim; int c; subStats.TryGetValue(key, out c); subStats[key] = c + 1;
                p = send;
            }
            if (p != pend) o.WriteLine("   !! part ends at {0:X}, parsed to {1:X}", pend, p);
            p = pend;
        }
        // tail
        o.WriteLine("\ntail at {0:X}:", p);
        while (p < end)
        {
            int cs = p; int ce = Chunk(null, out ver);
            int n = 0; while (d[cs + 8 + n] != 0) n++;
            string name = Encoding.ASCII.GetString(d, cs + 8, n);
            o.WriteLine("  {0:X6} {1} v{2} size {3}: {4}", cs, name, ver, ce - cs, Hex(p, Math.Min(48, ce - p)));
            if (name == "Flare") o.WriteLine("     f: {0}", Floats(p + 10, (ce - p - 10) / 4));
            p = ce;
        }
        o.WriteLine("end {0:X} file {1:X}", p, d.Length);
        o.WriteLine("\nsubpart a/b/prim:");
        foreach (var kv in subStats) o.WriteLine("  {0,4} {1}", kv.Value, kv.Key);
    }
}
