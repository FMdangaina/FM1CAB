// fm12cmp: vertex-level comparison of the same car in FM1 (car.carbin, LE chunks) and FM2 (type 2, BE).
// FM1 position = s16 / 6553.6 with z flipped; FM2 position = half xyz * w + section offset.
//   fm12cmp <fm1 car.carbin> <fm2 .carbin> [part filter]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

class V { public double X, Y, Z, NX, NY, NZ, U, Vv; public int Raw; public byte[] Bytes; public double DX, DY, DZ; public bool HasDmg; }
class Sub { public int Lod, Mat; public string MatName; public int[] Idx; }
class Part { public string Name; public List<V> Verts = new List<V>(); public List<Sub> Subs = new List<Sub>(); public int Vb = -1; public double OX, OY, OZ; }

static class Cmp
{
    static string F(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

    // ---------------- FM1 ----------------
    static List<Part> ReadFm1(byte[] d)
    {
        int p = 0;
        Func<int> I32 = () => { int v = BitConverter.ToInt32(d, p); p += 4; return v; };
        Func<int> Chunk = () => { int s = p, size = I32(); I32(); while (d[p] != 0) p++; p++; return s + size; };
        Chunk(); p = Chunk(); p = Chunk();
        int n = I32();
        var parts = new List<Part>();
        for (int i = 0; i < n; i++)
        {
            var part = new Part();
            int pend = Chunk(); p += 4; int nl = I32(); part.Name = Encoding.ASCII.GetString(d, p, nl); p += nl;
            if (part.Name.EndsWith("_LOD1")) part.Name = part.Name.Substring(0, part.Name.Length - 5);
            int vend = Chunk(); int vc = I32();
            for (int v = 0; v < vc; v++)
            {
                int b = p + v * 20;
                var x = new V();
                x.X = BitConverter.ToInt16(d, b) / 6553.6; x.Y = BitConverter.ToInt16(d, b + 2) / 6553.6; x.Z = -BitConverter.ToInt16(d, b + 4) / 6553.6;
                x.NX = BitConverter.ToInt16(d, b + 6) / 32767.0; x.NY = BitConverter.ToInt16(d, b + 8) / 32767.0; x.NZ = -BitConverter.ToInt16(d, b + 10) / 32767.0;
                x.Raw = BitConverter.ToInt32(d, b + 12);
                x.U = BitConverter.ToUInt16(d, b + 16); x.Vv = BitConverter.ToUInt16(d, b + 18);
                x.Bytes = new byte[20]; Array.Copy(d, b, x.Bytes, 0, 20);
                part.Verts.Add(x);
            }
            p = vend;
            int dend = Chunk(); int dc = I32();
            for (int v = 0; v < dc && v < vc; v++)
            {
                int b = p + v * 12; var x = part.Verts[v];
                x.HasDmg = true;
                x.DX = BitConverter.ToInt16(d, b) / 6553.6 - x.X; x.DY = BitConverter.ToInt16(d, b + 2) / 6553.6 - x.Y; x.DZ = -BitConverter.ToInt16(d, b + 4) / 6553.6 - x.Z;
            }
            p = dend; p = Chunk(); p = Chunk();
            int ns = I32();
            for (int s = 0; s < ns; s++)
            {
                int send = Chunk();
                var sub = new Sub { Lod = I32(), Mat = I32() }; I32(); int ic = I32(); p += 16;
                sub.Idx = new int[ic];
                for (int k = 0; k < ic; k++) sub.Idx[k] = BitConverter.ToUInt16(d, p + k * 2);
                part.Subs.Add(sub);
                p = send;
            }
            parts.Add(part);
            p = pend;
        }
        return parts;
    }

    // ---------------- FM2 ----------------
    static byte[] e;
    static uint U32(int at) { return (uint)(e[at] << 24 | e[at + 1] << 16 | e[at + 2] << 8 | e[at + 3]); }
    static float F32(int at) { return BitConverter.ToSingle(BitConverter.GetBytes(U32(at)), 0); }
    static double Half(int at)
    {
        int h = e[at] << 8 | e[at + 1];
        int s = (h >> 15) & 1, ex = (h >> 10) & 31, m = h & 1023;
        double v = ex == 0 ? m / 1024.0 * Math.Pow(2, -14) : (1 + m / 1024.0) * Math.Pow(2, ex - 15);
        return s == 1 ? -v : v;
    }
    static void Dec3n(uint u, out double x, out double y, out double z)
    {
        int a = (int)(u & 0x7ff); if (a >= 1024) a -= 2048;
        int b = (int)((u >> 11) & 0x7ff); if (b >= 1024) b -= 2048;
        int c = (int)((u >> 22) & 0x3ff); if (c >= 512) c -= 1024;
        x = a / 1023.0; y = b / 1023.0; z = c / 511.0;
    }

    static List<V>[] vbs = new List<V>[2];

    static List<Part> ReadFm2(byte[] d)
    {
        e = d;
        int p = 0x10C;
        int nrec = (int)U32(p); p += 4 + nrec * 140 + 0x6c;
        int lods = (int)U32(p); p += 4;
        for (int i = 0; i < lods * 2; i++) { int c = (int)U32(p); p += 4 + c * 4; }
        p += 4;
        int nsec = (int)U32(p); p += 4;
        var parts = new List<Part>();
        for (int s = 0; s < nsec; s++)
        {
            var part = new Part();
            uint marker = U32(p); if (marker == 0x2c9 || marker == 2 || marker == 5) p += 4;
            part.OX = F32(p); part.OY = F32(p + 4); part.OZ = F32(p + 8); p += 36;
            p += 0x1c;
            int na = (int)U32(p); p += 4 + na * 16 + 4;
            int nb = (int)U32(p); p += 4 + nb * 2 + 4;
            uint vb1 = U32(p), vb2 = U32(p + 4); p += 8;
            int nl = d[p++]; part.Name = Encoding.ASCII.GetString(d, p, nl); p += nl;
            p += 8;
            int lvc = (int)U32(p), lvs = (int)U32(p + 4); p += 8 + lvc * lvs + 4;
            int nsub = (int)U32(p); p += 4;
            for (int k = 0; k < nsub; k++)
            {
                p += 8;
                int snl = (int)U32(p); p += 4; string mat = Encoding.ASCII.GetString(d, p, snl); p += snl;
                p += 4; int lod = (int)U32(p); p += 8;
                int ic = (int)U32(p), isz = (int)U32(p + 4); p += 8;
                var sub = new Sub { Lod = lod, MatName = mat, Idx = new int[ic] };
                for (int q = 0; q < ic; q++) sub.Idx[q] = isz == 2 ? (d[p + q * 2] << 8 | d[p + q * 2 + 1]) : (int)U32(p + q * 4);
                p += ic * isz;
                part.Subs.Add(sub);
            }
            if (vb1 == vb2 || vb1 == 0) part.Vb = vb1 == vb2 ? 0 : 1;
            else { p += 4; int vc = (int)U32(p), vs = (int)U32(p + 4); p += 8 + vc * vs; }
            parts.Add(part);
        }
        p += 8;
        for (int b = 0; b < 2; b++)
        {
            int c = (int)U32(p), sz = (int)U32(p + 4); p += 12;
            var list = new List<V>();
            for (int v = 0; v < c; v++)
            {
                int q = p + v * sz; var x = new V();
                double w = Half(q + 6);
                x.X = Half(q) * w; x.Y = Half(q + 2) * w; x.Z = Half(q + 4) * w;
                // raw 16-bit words (the legacy reader treats them as halves, which gives tiny values)
                // as sign + magnitude: the sign bit is set and the magnitude is the coordinate
                int ru = e[q + 8] << 8 | e[q + 9], rv = e[q + 10] << 8 | e[q + 11];
                x.U = ((ru & 0x8000) != 0 ? 1 : -1) * (ru & 0x7FFF); x.Vv = ((rv & 0x8000) != 0 ? 1 : -1) * (rv & 0x7FFF);
                Dec3n(U32(q + 16), out x.NX, out x.NY, out x.NZ);
                x.Bytes = new byte[sz]; Array.Copy(d, q, x.Bytes, 0, sz);
                list.Add(x);
            }
            vbs[b] = list;
            p += c * sz;
            if (b == 0) p += 12; // 000decaf 00000002 00000001 between the two VBs
        }
        return parts;
    }

    static int MaxIdx(Part p, int lodFilter)
    {
        int m = -1;
        foreach (var s in p.Subs) if (lodFilter < 0 || s.Lod == lodFilter) foreach (int i in s.Idx) if (i != 0xFFFF && i < 0xFFFFFF) m = Math.Max(m, i);
        return m;
    }

    static List<V> Fm2Verts(Part p, int bas)
    {
        var used = new SortedSet<int>();
        foreach (var s in p.Subs) foreach (int i in s.Idx) if (i != 0xFFFF) used.Add(i + bas);
        var list = new List<V>();
        foreach (int i in used)
        {
            if (i >= vbs[p.Vb].Count) continue;
            var src = vbs[p.Vb][i];
            list.Add(new V { X = src.X + p.OX, Y = src.Y + p.OY, Z = src.Z + p.OZ, NX = src.NX, NY = src.NY, NZ = src.NZ, U = src.U, Vv = src.Vv, Bytes = src.Bytes, Raw = i });
        }
        return list;
    }

    static V Nearest(List<V> set, V a, out double dist)
    {
        V best = null; double bd = 1e9;
        foreach (var b in set) { double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z, dd = dx * dx + dy * dy + dz * dz; if (dd < bd) { bd = dd; best = b; } }
        dist = Math.Sqrt(bd); return best;
    }

    static void Main(string[] args)
    {
        var f1 = ReadFm1(File.ReadAllBytes(args[0]));
        var f2 = ReadFm2(File.ReadAllBytes(args[1]));
        string filter = args.Length > 2 ? args[2] : null;
        Console.WriteLine("FM1 parts {0}, FM2 sections {1}, FM2 VB A {2}, VB B {3}", f1.Count, f2.Count, vbs[0].Count, vbs[1].Count);

        // index base hypotheses: absolute, or cumulative per VB in section order
        var cum = new Dictionary<Part, int>(); int[] run = { 0, 0 };
        foreach (var s in f2) { cum[s] = run[s.Vb]; run[s.Vb] += MaxIdx(s, -1) + 1; }
        Console.WriteLine("cumulative index end: VB A {0}, VB B {1}", run[0], run[1]);

        var uvA = new List<double[]>();
        Console.WriteLine("\npart              FM1v  FM2v(abs) med mm | FM2v(cum) med mm  p95 mm | normal dot | LOD idx FM1 / FM2");
        foreach (var a in f1)
        {
            if (filter != null && !a.Name.Contains(filter)) continue;
            var b = f2.FirstOrDefault(x => x.Name == a.Name);
            if (b == null) continue;
            var vAbs = Fm2Verts(b, 0); var vCum = Fm2Verts(b, cum[b]);
            Func<List<V>, double[]> med = set =>
            {
                if (set.Count == 0) return new double[] { -1, -1 };
                var ds = a.Verts.Select(v => { double dd; Nearest(set, v, out dd); return dd * 1000; }).OrderBy(x => x).ToArray();
                return new[] { ds[ds.Length / 2], ds[(int)(ds.Length * 0.95)] };
            };
            var mAbs = med(vAbs); var mCum = med(vCum);
            var best = mCum[0] >= 0 && (mAbs[0] < 0 || mCum[0] < mAbs[0]) ? vCum : vAbs;
            double dotSum = 0; int dotN = 0;
            foreach (var v in a.Verts)
            {
                double dd; var q = Nearest(best, v, out dd);
                if (dd > 0.0005) continue;
                // the same position can have several normals; take the best match among exact-position twins
                double bdot = -2;
                foreach (var t in best) if (Math.Abs(t.X - v.X) < 0.0005 && Math.Abs(t.Y - v.Y) < 0.0005 && Math.Abs(t.Z - v.Z) < 0.0005)
                    {
                        double ln = Math.Sqrt(t.NX * t.NX + t.NY * t.NY + t.NZ * t.NZ) + 1e-9;
                        double dot = (t.NX * v.NX + t.NY * v.NY + t.NZ * v.NZ) / ln;
                        if (dot > bdot) { bdot = dot; q = t; }
                    }
                dotSum += bdot; dotN++;
                if (bdot > 0.98) uvA.Add(new[] { v.U, v.Vv, q.U, q.Vv });
            }
            var lodsTxt = new StringBuilder();
            for (int l = 1; l <= 5; l++)
            {
                int c1 = a.Subs.Where(s => s.Lod == l).Sum(s => s.Idx.Length), c2 = b.Subs.Where(s => s.Lod == l).Sum(s => s.Idx.Length);
                if (c1 + c2 > 0) lodsTxt.AppendFormat(" {0}:{1}/{2}", l, c1, c2);
            }
            Console.WriteLine("{0,-16} {1,5} {2,6} {3,7:0.0} | {4,6} {5,7:0.0} {6,7:0.0} | {7,5:0.000} {8,5} |{9}",
                a.Name, a.Verts.Count, vAbs.Count, mAbs[0], vCum.Count, mCum[0], mCum[1], dotN > 0 ? dotSum / dotN : 0, dotN, lodsTxt);
        }

        // FM1 material number -> FM2 material name, by votes of matched vertices (FM1 LOD 1 vs FM2 LOD 2)
        var votes = new Dictionary<int, Dictionary<string, int>>();
        foreach (var a in f1)
        {
            var b = f2.FirstOrDefault(x => x.Name == a.Name);
            if (b == null) continue;
            var matOf = new Dictionary<int, string>();
            foreach (var s in b.Subs) if (s.Lod == 2) foreach (int i in s.Idx) if (i != 0xFFFF) matOf[i + cum[b]] = s.MatName;
            var cand = Fm2Verts(b, cum[b]).Where(v => matOf.ContainsKey(v.Raw)).ToList();
            if (cand.Count == 0) continue;
            foreach (var s in a.Subs)
            {
                if (s.Lod != 1) continue;
                foreach (int i in s.Idx.Distinct())
                {
                    double dd; var q = Nearest(cand, a.Verts[i], out dd);
                    if (dd > 0.001) continue;
                    Dictionary<string, int> m; if (!votes.TryGetValue(s.Mat, out m)) votes[s.Mat] = m = new Dictionary<string, int>();
                    int c; m.TryGetValue(matOf[q.Raw], out c); m[matOf[q.Raw]] = c + 1;
                }
            }
        }
        Console.WriteLine("\nFM1 material -> FM2 materials (vertex votes):");
        foreach (var kv in votes.OrderBy(x => x.Key))
        {
            int tot = kv.Value.Values.Sum();
            Console.WriteLine("  {0,3}: {1}", kv.Key, string.Join(", ", kv.Value.OrderByDescending(x => x.Value).Take(4).Select(x => string.Format("{0} {1:0}%", x.Key, 100.0 * x.Value / tot))) + "  (n " + tot + ")");
        }

        // UV relation on matched vertices: fit FM2 = k * FM1 + c for u and v
        if (uvA.Count > 10)
        {
            for (int c = 0; c < 2; c++)
            {
                double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = uvA.Count;
                foreach (var r in uvA) { sx += r[c]; sy += r[c + 2]; sxx += r[c] * r[c]; sxy += r[c] * r[c + 2]; }
                double k = (n * sxy - sx * sy) / (n * sxx - sx * sx), c0 = (sy - k * sx) / n;
                double err = 0; foreach (var r in uvA) err += Math.Abs(r[c + 2] - (k * r[c] + c0));
                Console.WriteLine("UV {0}: FM2 = {1:G6} * FM1raw + {2:G6}  (1/k = {3:G6}), mean abs err {4:G4}, n {5}", c == 0 ? "u" : "v", k, c0, 1 / k, err / n, n);
            }
            for (int c = 0; c < 2; c++)
            {
                var ratios = uvA.Where(r => r[c + 2] > 20).Select(r => r[c] / r[c + 2]).OrderBy(x => x).ToArray();
                int within = ratios.Count(x => Math.Abs(x - 25) < 0.5);
                Console.WriteLine("{0}: FM1/FM2 ratio median {1:0.000}, p10 {2:0.000}, p90 {3:0.000}; within 25+-0.5: {4} of {5}; FM2 sign bit clear: {6}",
                    c == 0 ? "u" : "v", ratios[ratios.Length / 2], ratios[ratios.Length / 10], ratios[ratios.Length * 9 / 10], within, ratios.Length, uvA.Count(r => r[c + 2] < 0));
            }
            Console.WriteLine("samples FM1 u v raw -> FM2 u v:");
            for (int i = 0; i < Math.Min(8, uvA.Count); i += 1) Console.WriteLine("   {0} {1} -> {2} {3}", uvA[i * (uvA.Count / 8)][0], uvA[i * (uvA.Count / 8)][1], F(uvA[i * (uvA.Count / 8)][2]), F(uvA[i * (uvA.Count / 8)][3]));
        }
    }
}
