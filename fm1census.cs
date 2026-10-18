// fm1census: checks the FM1 .carbin grammar and the XPR0 layout on every file inside the game archives,
// reading them straight from the .cab/.ca2 files (nothing is extracted).
//
//   fm1census carbin <cabs folder> [-v]
//   fm1census xpr    <cabs folder> [-v]
//
// Build (with the CAB reader): csc /nologo /platform:x86 /optimize /main:Fm1Census fm1census.cs fm1cab.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

class Tally
{
    readonly Dictionary<string, Dictionary<string, int>> d = new Dictionary<string, Dictionary<string, int>>();
    public void Add(string table, object key)
    {
        Dictionary<string, int> t;
        if (!d.TryGetValue(table, out t)) d[table] = t = new Dictionary<string, int>();
        string k = key.ToString();
        int n; t.TryGetValue(k, out n); t[k] = n + 1;
    }
    public void Print(int top)
    {
        foreach (var t in d.OrderBy(x => x.Key))
            Console.WriteLine("{0}: {1}{2}", t.Key, string.Join(", ", t.Value.OrderByDescending(x => x.Value).Take(top).Select(x => x.Key + " x" + x.Value)),
                t.Value.Count > top ? " ... (" + t.Value.Count + " keys)" : "");
    }
}

// Strict reader for the FM1 chunk tree: [u32 size incl. header][u32 version][name\0][data]
class Fm1Reader
{
    public byte[] D;
    public int P;
    public Tally T;
    public Fm1Reader(byte[] d, Tally t) { D = d; T = t; }

    public int I32() { int v = BitConverter.ToInt32(D, P); P += 4; return v; }
    public float F32() { float v = BitConverter.ToSingle(D, P); P += 4; return v; }

    public string PeekName(int at)
    {
        int n = 0;
        while (at + 8 + n < D.Length && D[at + 8 + n] != 0 && n < 40) n++;
        return Encoding.ASCII.GetString(D, at + 8, n);
    }

    // Opens a chunk, checks its name (null = any), returns its end.
    public int Open(string expect, out int ver, out string name)
    {
        int start = P;
        if (P + 9 > D.Length) throw new InvalidDataException("chunk header past end at 0x" + P.ToString("X"));
        int size = I32();
        ver = I32();
        name = PeekName(start);
        P = start + 8 + name.Length + 1;
        if (size < P - start || start + (long)size > D.Length) throw new InvalidDataException(string.Format("0x{0:X} {1}: bad size {2}", start, name, size));
        if (expect != null && name != expect) throw new InvalidDataException(string.Format("0x{0:X}: expected {1}, found '{2}'", start, expect, name));
        T.Add("chunk versions", name + " v" + ver);
        return start + size;
    }

    public void Close(int end, string what)
    {
        if (P != end) throw new InvalidDataException(string.Format("{0}: parsed to 0x{1:X}, chunk ends at 0x{2:X} ({3:+#;-#} bytes)", what, P, end, end - P));
    }
}

static class Fm1Census
{
    static int Main(string[] a)
    {
        if (a.Length < 2) { Console.WriteLine("fm1census carbin|xpr <cabs folder> [-v]"); return 1; }
        bool verbose = a.Length > 2 && a[2] == "-v";
        var t = new Tally();
        int files = 0, bad = 0;
        string ext = a[0] == "xpr" ? ".xpr" : ".carbin";
        var badByError = new Dictionary<string, List<string>>();
        foreach (var arc in Directory.GetFiles(a[1], "*.cab").Concat(Directory.GetFiles(a[1], "*.ca2")).OrderBy(x => x))
        {
            Fm1Cab cab = Fm1Cab.Load(arc);
            foreach (var e in cab.Entries.Where(x => x.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.PathOffset))
            {
                files++;
                string where = Path.GetFileName(arc) + ":" + cab.FullPath(e);
                byte[] d = cab.Payload(e);
                try
                {
                    if (ext == ".xpr") Xpr(d, t, where, verbose); else Carbin(d, t, where, verbose);
                }
                catch (Exception ex)
                {
                    bad++;
                    string key = System.Text.RegularExpressions.Regex.Replace(ex.Message, "0x[0-9A-F]+|[+-]?\\d+", "#");
                    List<string> l;
                    if (!badByError.TryGetValue(key, out l)) badByError[key] = l = new List<string>();
                    l.Add(where + "  (" + ex.Message + ")");
                }
            }
        }
        Console.WriteLine("{0} {1} files, {2} failed", files, ext, bad);
        foreach (var kv in badByError.OrderByDescending(x => x.Value.Count))
        {
            Console.WriteLine("  {0} x {1}", kv.Value.Count, kv.Key);
            foreach (var w in kv.Value.Take(3)) Console.WriteLine("      " + w);
        }
        Console.WriteLine();
        t.Print(verbose ? 60 : 14);
        return bad == 0 ? 0 : 3;
    }

    // ---------------- carbin ----------------
    static void Carbin(byte[] d, Tally t, string where, bool verbose)
    {
        var r = new Fm1Reader(d, t);
        int ver; string name;
        string root = r.PeekName(0);
        t.Add("root chunk", root);
        if (root != "Car") { GenericTree(r, t, 0, d.Length, 0); return; }
        int end = r.Open("Car", out ver, out name);
        if (end != d.Length) throw new InvalidDataException("Car size != file size");
        int e2 = r.Open("Attribs", out ver, out name);
        int one = r.I32(), two = r.I32();
        t.Add("Attribs v" + ver + " u32 u32 / floats", one + " " + two + " / " + (e2 - r.P) / 4);
        r.P = e2;
        int carVer = BitConverter.ToInt32(d, 4);
        bool hasDriver = r.PeekName(r.P) == "Driver";
        t.Add("Car version / Driver chunk", "v" + carVer + (hasDriver ? " Driver" : " none"));
        if (hasDriver)
        {
            e2 = r.Open("Driver", out ver, out name);
            t.Add("Driver floats", (e2 - r.P) / 4);
            r.P = e2;
        }
        int parts = r.I32();
        t.Add("part count (bucket 10)", parts / 10 * 10);
        for (int i = 0; i < parts; i++)
        {
            int pend = r.Open("Part", out ver, out name);
            t.Add("part head byte0", d[r.P]);
            r.P += 4;
            string pname = "(no name)";
            t.Add("Car/Part version", "Car v" + carVer + " Part v" + ver);
            if (ver >= 2)
            {
                int nlen = r.I32();
                if (nlen <= 0 || nlen > 64) throw new InvalidDataException("part name length " + nlen);
                pname = Encoding.ASCII.GetString(d, r.P, nlen);
                r.P += nlen;
            }
            int ve = r.Open("Verts", out ver, out name);
            int vc = r.I32();
            if (vc < 0 || r.P + (long)vc * 20 != ve) throw new InvalidDataException(string.Format("Verts: {0} vertices, {1} bytes (not 20 each)", vc, ve - r.P));
            int vstart = r.P;
            r.P = ve;
            int de = r.Open("Damage", out ver, out name);
            int dc = r.I32();
            if (dc != 0 && dc != vc) throw new InvalidDataException(string.Format("Damage count {0} != vertex count {1}", dc, vc));
            if (r.P + (long)dc * 12 != de) throw new InvalidDataException("Damage records not 12 bytes");
            t.Add("damage", dc == 0 ? "none" : "per vertex");
            r.P = de;
            int pe = r.Open("Pivots", out ver, out name);
            int pv = r.I32();
            t.Add("pivots flag / floats", pv + " / " + (pe - r.P) / 4);
            r.P = pe;
            int ae = r.Open("Attributes", out ver, out name);
            t.Add("Attributes floats", (ae - r.P) / 4);
            r.P = ae;
            int subs = r.I32();
            for (int s = 0; s < subs; s++)
            {
                int se = r.Open("Subpart", out ver, out name);
                int lod = r.I32(), mat = r.I32(), prim = r.I32(), ic = r.I32();
                t.Add("Subpart version / 16-byte block", "v" + ver + (ver >= 2 ? " yes" : " no"));
                if (ver >= 2) r.P += 16;
                if (r.P + (long)ic * 2 != se) throw new InvalidDataException(string.Format("Subpart: {0} indices, {1} bytes left", ic, se - r.P));
                int max = -1;
                bool restart = false;
                for (int k = 0; k < ic; k++)
                {
                    int ix = BitConverter.ToUInt16(d, r.P + k * 2);
                    if (ix == 0xFFFF) restart = true; else if (ix > max) max = ix;
                }
                if (max >= vc) throw new InvalidDataException(string.Format("{0}: index {1} >= vertex count {2}", pname, max, vc));
                t.Add("subpart prim", prim);
                t.Add("subpart lod", lod);
                t.Add("subpart material", mat);
                if (restart) t.Add("subpart with 0xFFFF", "yes");
                r.P = se;
            }
            r.Close(pend, "Part " + pname);
            string baseName = System.Text.RegularExpressions.Regex.Replace(pname, "_LOD\\d$", "");
            t.Add("part names", baseName);
            t.Add("part _LODn suffix", pname.Length > baseName.Length ? pname.Substring(baseName.Length) : "(none)");
        }
        while (r.P < end)
        {
            int ce = r.Open(null, out ver, out name);
            t.Add("tail chunks", name);
            r.P = ce;
        }
        r.Close(end, "Car");
        if (verbose) Console.WriteLine("OK " + where + " parts " + parts);
    }

    // Non-car carbins: walk any chunk tree whose children fill the parent exactly; record the shape.
    static void GenericTree(Fm1Reader r, Tally t, int start, int end, int depth)
    {
        r.P = start;
        int ver; string name;
        int ce = r.Open(null, out ver, out name);
        t.Add("non-Car chunk", new string('.', depth) + name + " v" + ver);
        r.P = ce;
        if (ce != end && depth == 0) throw new InvalidDataException("root size " + ce + " != file size " + end);
    }

    // ---------------- xpr ----------------
    static readonly Dictionary<int, string> FormatNames = new Dictionary<int, string>
    {
        {0x00,"L8"},{0x01,"AL8"},{0x02,"A1R5G5B5"},{0x03,"X1R5G5B5"},{0x04,"A4R4G4B4"},{0x05,"R5G6B5"},{0x06,"A8R8G8B8"},
        {0x07,"X8R8G8B8"},{0x0B,"P8"},{0x0C,"DXT1"},{0x0E,"DXT3"},{0x0F,"DXT5"},{0x10,"LIN_A1R5G5B5"},{0x11,"LIN_R5G6B5"},
        {0x12,"LIN_A8R8G8B8"},{0x13,"LIN_L8"},{0x19,"A8"},{0x1A,"A8L8"},{0x1E,"LIN_X8R8G8B8"},{0x1D,"LIN_A4R4G4B4"},
        {0x3A,"A8B8G8R8"},{0x3F,"LIN_A8B8G8R8"},{0x40,"LIN_A8"},{0x20,"LIN_A8L8"},
    };

    static int Bpp(int f)
    {
        switch (f)
        {
            case 0x0C: return 4;                       // bits per pixel
            case 0x0E: case 0x0F: return 8;
            case 0x00: case 0x01: case 0x0B: case 0x13: case 0x19: case 0x40: return 8;
            case 0x02: case 0x03: case 0x04: case 0x05: case 0x10: case 0x11: case 0x1A: case 0x1D: case 0x20: return 16;
            case 0x06: case 0x07: case 0x12: case 0x1E: case 0x3A: case 0x3F: return 32;
        }
        return -1;
    }

    static bool Dxt(int f) { return f == 0x0C || f == 0x0E || f == 0x0F; }

    static long LevelBytes(int f, int w, int h)
    {
        if (Dxt(f)) return (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * (f == 0x0C ? 8 : 16);
        return (long)w * h * Bpp(f) / 8;
    }

    static void Xpr(byte[] d, Tally t, string where, bool verbose)
    {
        if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "XPR0") throw new InvalidDataException("magic " + Encoding.ASCII.GetString(d, 0, 4));
        uint total = BitConverter.ToUInt32(d, 4), header = BitConverter.ToUInt32(d, 8);
        if (total != d.Length) throw new InvalidDataException(string.Format("total size {0} != file {1}", total, d.Length));
        if (header > total || header < 12) throw new InvalidDataException("header size " + header);
        t.Add("header size % 2048", header % 2048 == 0 ? "0" : (header % 2048).ToString());
        int p = 12, n = 0;
        var res = new List<Tuple<uint, long, string>>();   // data offset, expected bytes, label
        while (true)
        {
            if (p + 4 > header) throw new InvalidDataException("no 0xFFFFFFFF terminator before header end");
            uint common = BitConverter.ToUInt32(d, p);
            if (common == 0xFFFFFFFF) { p += 4; break; }
            int type = (int)(common >> 16) & 7;
            t.Add("resource type", type == 4 ? "texture" : type == 0 ? "vertexbuffer" : type == 1 ? "indexbuffer" : type == 3 ? "palette" : type.ToString());
            uint data = BitConverter.ToUInt32(d, p + 4);
            if (type != 4) { res.Add(Tuple.Create(data, -1L, "type" + type)); p += 12; n++; continue; }
            uint fmt = BitConverter.ToUInt32(d, p + 12), size = BitConverter.ToUInt32(d, p + 16);
            p += 20;
            n++;
            bool cube = (fmt & 4) != 0;
            int dims = (int)(fmt >> 4) & 15, f = (int)(fmt >> 8) & 0xFF, levels = (int)(fmt >> 16) & 15;
            int lw = (int)(fmt >> 20) & 15, lh = (int)(fmt >> 24) & 15, ld = (int)(fmt >> 28) & 15;
            int w, h;
            if (size != 0) { w = (int)(size & 0xFFF) + 1; h = (int)((size >> 12) & 0xFFF) + 1; t.Add("explicit size word (linear)", FormatNames.ContainsKey(f) ? FormatNames[f] : f.ToString("X2")); }
            else { w = 1 << lw; h = 1 << lh; }
            int depth = dims == 3 ? 1 << ld : 1;
            string fname = FormatNames.ContainsKey(f) ? FormatNames[f] : "0x" + f.ToString("X2");
            t.Add("format", fname);
            t.Add("dimensions/cube/depth", dims + (cube ? " cube" : "") + (depth > 1 ? " depth " + depth : ""));
            t.Add("size", w + "x" + h);
            t.Add("levels", levels);
            if (Bpp(f) < 0) { res.Add(Tuple.Create(data, -1L, fname)); continue; }
            long bytes = 0;
            if (size != 0)
            {
                // Linear texture: size word = width-1 (bits 0-11), height-1 (12-23), pitch/64-1 (24-31)
                int pitch = (int)((size >> 24) + 1) * 64;
                if (pitch < w * Bpp(f) / 8) throw new InvalidDataException("pitch " + pitch + " < row bytes " + w * Bpp(f) / 8);
                t.Add("linear pitch", pitch == (w * Bpp(f) / 8 + 63) / 64 * 64 ? "row bytes rounded up to 64" : "other");
                if (levels != 1) t.Add("linear with mips", levels);
                res.Add(Tuple.Create(data, (long)pitch * h, fname + " " + w + "x" + h + " pitch " + pitch));
                continue;
            }
            for (int l = 0; l < Math.Max(1, levels); l++)
                bytes += LevelBytes(f, Math.Max(1, w >> l), Math.Max(1, h >> l)) * Math.Max(1, depth >> l);
            if (cube) bytes = ((bytes + 127) / 128 * 128) * 6;    // cube faces are 128-byte aligned (probable)
            res.Add(Tuple.Create(data, bytes, fname + " " + w + "x" + h + (depth > 1 ? "x" + depth : "") + " L" + levels));
        }
        t.Add("resources per file", n);
        // Each resource's data must fit before the next one (or the end); measure the slack.
        var sorted = res.OrderBy(x => x.Item1).ToList();
        long dataLen = total - header;
        for (int i = 0; i < sorted.Count; i++)
        {
            long next = i + 1 < sorted.Count ? sorted[i + 1].Item1 : dataLen;
            long room = next - sorted[i].Item1;
            if (sorted[i].Item2 < 0) continue;
            if (room < sorted[i].Item2)
                throw new InvalidDataException(string.Format("{0}: needs {1} bytes, {2} available", sorted[i].Item3, sorted[i].Item2, room));
            long slack = room - sorted[i].Item2;
            t.Add("slack after texture data", slack == 0 ? "0" : i + 1 < sorted.Count ? "<next:" + (slack < 128 ? "<128" : slack < 4096 ? "<4K" : ">=4K") : "<end:" + (slack < 2048 ? "<2K" : ">=2K"));
            if (verbose && slack >= 2048) Console.WriteLine("slack {0} {1} {2}", slack, where, sorted[i].Item3);
        }
        if (verbose) Console.WriteLine("OK {0}: {1}", where, string.Join(" | ", res.Select(x => x.Item3)));
    }
}
