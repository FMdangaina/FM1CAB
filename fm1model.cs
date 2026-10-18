// FM1 (original Xbox) car.carbin / tire.carbin model, shared by fm1obj and fm1studio.
// Grammar: Fm1_Fm2\fm1_vs_fm2_carbin.md section 4 (503/503 game files parse to the last byte).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Turn10's material enum, read from default.xbe (name/value pairs at raw 0x50E650). The same names, prefixed with
// Day/Night, are the sections of media\cars\GlobalMaterialSettings.xml (Mega.ca2).
static class Fm1Materials
{
    static readonly string[] names =
    {
        "None", "Body", "Window", "TailLights", "HeadLights", "ReverseLights", "BlackGlass", "Chrome", "Tire", "Black",
        "Tire2", "Rim", "InnerRim", "BlurredRim", "Interior", "Frame", "RedGlass", "RimChrome", "Matte", "CarbonFiber",
        "Plastic", "RubberTrim", "InnerRimChrome", "BlurredRimChrome", "NamePlate", "Badge", "Number", "Misc",
        "HeadlightGlass", "WhiteTailLights", "TailLightsOn", "Reflectors", "Grille1", "Grille2", "Misc2", "Plastic2"
    };

    public static string Name(int m) { return m >= 0 && m < names.Length ? names[m] : "Material" + m; }

    // See-through materials (drawn after the opaque ones, alpha blended).
    public static bool Transparent(int m) { return m == 2 || m == 6 || m == 16 || m == 28; }
}

class Fm1Subpart
{
    public int Version, Lod, Material, Prim;
    public byte[] Block16;      // null on Subpart v1
    public int[] Strip;
}

class Fm1Part
{
    public string Name;
    public int Version;
    public byte[] Head;         // 4 bytes after the chunk name
    public int Count;
    public float[] Pos, Nrm, Uv;            // per vertex: 3, 3, 2 (FM1 axes, front +Z)
    public byte[] Light;                    // per vertex: 2 bytes (probable baked light)
    public float[] DamagePos, DamageNrm;    // null when the part has no Damage records
    public float[] Pivot;                   // null, or the 22 floats
    public float[] Attributes;
    public List<Fm1Subpart> Subparts = new List<Fm1Subpart>();
}

class Fm1Car
{
    public int Version;
    public int[] AttribsHead = new int[2];
    public float[] Attribs;     // 12 floats = 4 wheel centres (m, FM1 axes)
    public float[] Driver;      // 15 floats, null on Car v1/v2
    public List<Fm1Part> Parts = new List<Fm1Part>();
    public List<KeyValuePair<string, float[]>> Tail = new List<KeyValuePair<string, float[]>>();   // Flare, Line

    byte[] d;
    int p;

    int I32() { int v = BitConverter.ToInt32(d, p); p += 4; return v; }

    int Open(string expect, out int ver, out string name)
    {
        int start = p, size = I32();
        ver = I32();
        int n = 0;
        while (d[p + n] != 0) n++;
        name = Encoding.ASCII.GetString(d, p, n);
        p += n + 1;
        if (expect != null && name != expect) throw new InvalidDataException(string.Format("0x{0:X}: expected {1}, found {2}", start, expect, name));
        return start + size;
    }

    int Open(string expect, out int ver) { string n; return Open(expect, out ver, out n); }

    string Peek()
    {
        int n = 0;
        while (d[p + 8 + n] != 0) n++;
        return Encoding.ASCII.GetString(d, p + 8, n);
    }

    float[] Floats(int end)
    {
        var f = new float[Math.Max(0, (end - p) / 4)];
        for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToSingle(d, p + i * 4);
        p = end;
        return f;
    }

    public static Fm1Car Parse(byte[] data)
    {
        var c = new Fm1Car { d = data };
        int ver;
        int end = c.Open("Car", out ver);
        c.Version = ver;
        int e = c.Open("Attribs", out ver);
        c.AttribsHead[0] = c.I32();
        c.AttribsHead[1] = c.I32();
        c.Attribs = c.Floats(e);
        if (c.Peek() == "Driver") { e = c.Open("Driver", out ver); c.Driver = c.Floats(e); }
        int parts = c.I32();
        for (int i = 0; i < parts; i++) c.Parts.Add(c.ReadPart(i));
        while (c.p < end)
        {
            string name;
            e = c.Open(null, out ver, out name);
            c.Tail.Add(new KeyValuePair<string, float[]>(name, c.Floats(e)));
        }
        return c;
    }

    Fm1Part ReadPart(int index)
    {
        int ver;
        int pend = Open("Part", out ver);
        var part = new Fm1Part { Name = null, Version = ver, Head = new byte[4] };    // Part v1 stores no name
        Buffer.BlockCopy(d, p, part.Head, 0, 4);
        p += 4;
        if (ver >= 2)
        {
            int nlen = I32();
            part.Name = Encoding.ASCII.GetString(d, p, nlen);
            p += nlen;
        }
        int ve = Open("Verts", out ver);
        int vc = I32();
        part.Count = vc;
        part.Pos = new float[vc * 3];
        part.Nrm = new float[vc * 3];
        part.Uv = new float[vc * 2];
        part.Light = new byte[vc * 2];
        for (int v = 0; v < vc; v++)
        {
            int o = p + v * 20;
            for (int k = 0; k < 3; k++)
            {
                part.Pos[v * 3 + k] = BitConverter.ToInt16(d, o + k * 2) / 6553.6f;
                part.Nrm[v * 3 + k] = BitConverter.ToInt16(d, o + 6 + k * 2) / 32767f;
            }
            part.Light[v * 2] = d[o + 12];
            part.Light[v * 2 + 1] = d[o + 13];
            part.Uv[v * 2] = BitConverter.ToInt16(d, o + 16) / 32768f;       // signed (verified)
            part.Uv[v * 2 + 1] = BitConverter.ToInt16(d, o + 18) / 32768f;
        }
        p = ve;
        int de = Open("Damage", out ver);
        int dc = I32();
        if (dc > 0)
        {
            part.DamagePos = new float[dc * 3];
            part.DamageNrm = new float[dc * 3];
            for (int v = 0; v < dc; v++)
                for (int k = 0; k < 3; k++)
                {
                    part.DamagePos[v * 3 + k] = BitConverter.ToInt16(d, p + v * 12 + k * 2) / 6553.6f;
                    part.DamageNrm[v * 3 + k] = BitConverter.ToInt16(d, p + v * 12 + 6 + k * 2) / 32767f;
                }
        }
        p = de;
        int pe = Open("Pivots", out ver);
        if (I32() != 0) part.Pivot = Floats(pe); else p = pe;
        int ae = Open("Attributes", out ver);
        part.Attributes = Floats(ae);
        int subs = I32();
        for (int s = 0; s < subs; s++)
        {
            int se = Open("Subpart", out ver);
            var sp = new Fm1Subpart { Version = ver, Lod = I32(), Material = I32(), Prim = I32() };
            int ic = I32();
            if (ver >= 2) { sp.Block16 = new byte[16]; Buffer.BlockCopy(d, p, sp.Block16, 0, 16); p += 16; }
            sp.Strip = new int[ic];
            for (int k = 0; k < ic; k++) sp.Strip[k] = BitConverter.ToUInt16(d, p + k * 2);
            part.Subparts.Add(sp);
            p = se;
        }
        if (p != pend) throw new InvalidDataException("part " + part.Name + " not fully read");
        return part;
    }

    // Strip -> triangles. No restart markers; degenerate triangles join the strips.
    // flip = false: even triangles (a, b, c), odd (b, a, c). After negating z (OBJ / viewer) these are
    // counter-clockwise seen from the stored normals on 99.9% of the Supra's LOD1 triangles.
    public static IEnumerable<int[]> Triangles(int[] s, bool flip)
    {
        for (int i = 0; i + 2 < s.Length; i++)
        {
            int a = s[i], b = s[i + 1], c = s[i + 2];
            if (a == b || b == c || a == c || a == 0xFFFF || b == 0xFFFF || c == 0xFFFF) continue;
            bool odd = (i & 1) != 0;
            if (odd ^ flip) yield return new[] { b, a, c }; else yield return new[] { a, b, c };
        }
    }
}
