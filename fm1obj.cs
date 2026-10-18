// fm1obj: FM1 (Xbox) car.carbin -> Wavefront OBJ (one file per LOD, one group per part, Turn10 part and material
// names), plus the car's livery atlas as PNG and a UV check image.
//
//   fm1obj <car_x.cab> <out dir>       (reads cars\<x>\car.carbin and its livery0hi.xpr from the archive)
//   fm1obj <car.carbin> <out dir>      (geometry only)
//
// Build: csc /nologo /platform:x86 /optimize /main:Fm1Obj /r:System.Drawing.dll fm1obj.cs fm1model.cs fm1xpr.cs fm1cab.cs
//
// Axes: FM1 is left-handed with the front at +Z. OBJ is right-handed: z is negated (front at -Z, like FM2/FM4),
// which also flips the triangle winding; the winding is chosen to agree with the stored vertex normals.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

static class Fm1Obj
{
    static string F(float f) { return f.ToString("0.######", CultureInfo.InvariantCulture); }

    static int Main(string[] a)
    {
        if (a.Length != 2) { Console.WriteLine("fm1obj <car_x.cab | car.carbin> <out dir>"); return 1; }
        try { return Run(a[0], a[1]); }
        catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
    }

    static int Run(string input, string outDir)
    {
        Directory.CreateDirectory(outDir);
        byte[] carbin;
        XprTexture livery = null;
        if (input.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase)) carbin = File.ReadAllBytes(input);
        else
        {
            var cab = Fm1Cab.Load(input);
            var ce = cab.Entries.FirstOrDefault(x => cab.FullPath(x).EndsWith("\\car.carbin", StringComparison.OrdinalIgnoreCase));
            if (ce == null) throw new FileNotFoundException("no car.carbin in " + input);
            carbin = cab.Payload(ce);
            var le = cab.Entries.FirstOrDefault(x => cab.FullPath(x).EndsWith("\\liveries\\standard\\livery0hi.xpr", StringComparison.OrdinalIgnoreCase));
            if (le != null) livery = XprTexture.Parse(cab.Payload(le));
        }
        var car = Fm1Car.Parse(carbin);

        // Winding: OBJ counter-clockwise front faces. Pick the strip parity that agrees with the stored normals
        // (z negated for both positions and normals).
        long agree = 0, disagree = 0;
        foreach (var part in car.Parts)
            foreach (var sp in part.Subparts.Where(x => x.Lod == 1))
                foreach (var t in Fm1Car.Triangles(sp.Strip, false))
                {
                    double[] p0 = P(part, t[0]), p1 = P(part, t[1]), p2 = P(part, t[2]);
                    double nx = (p1[1] - p0[1]) * (p2[2] - p0[2]) - (p1[2] - p0[2]) * (p2[1] - p0[1]);
                    double ny = (p1[2] - p0[2]) * (p2[0] - p0[0]) - (p1[0] - p0[0]) * (p2[2] - p0[2]);
                    double nz = (p1[0] - p0[0]) * (p2[1] - p0[1]) - (p1[1] - p0[1]) * (p2[0] - p0[0]);
                    double dot = 0;
                    foreach (int v in t) dot += nx * part.Nrm[v * 3] + ny * part.Nrm[v * 3 + 1] - nz * part.Nrm[v * 3 + 2];
                    if (dot > 0) agree++; else if (dot < 0) disagree++;
                }
        bool flip = disagree > agree;
        Console.WriteLine("winding vs stored normals: {0:P1} agree with (a,b,c) on even triangles{1}", (double)agree / Math.Max(1, agree + disagree),
            flip ? " -> using the other parity" : "");

        var mats = new SortedSet<int>();
        foreach (int lod in car.Parts.SelectMany(x => x.Subparts).Select(x => x.Lod).Distinct().OrderBy(x => x))
        {
            var sb = new StringBuilder();
            sb.AppendLine("# FM1 car.carbin LOD " + lod + " (fm1obj). Units: metres, front = -Z.");
            sb.AppendLine("mtllib car.mtl");
            int baseIndex = 1, tris = 0;
            foreach (var part in car.Parts)
            {
                var subs = part.Subparts.Where(x => x.Lod == lod).ToList();
                if (subs.Count == 0) continue;
                sb.AppendLine("g " + (part.Name ?? "Part"));   // Part v1 stores no name: the chunk name
                for (int v = 0; v < part.Count; v++)
                    sb.AppendFormat("v {0} {1} {2}\n", F(part.Pos[v * 3]), F(part.Pos[v * 3 + 1]), F(-part.Pos[v * 3 + 2]));
                for (int v = 0; v < part.Count; v++)
                    sb.AppendFormat("vt {0} {1}\n", F(part.Uv[v * 2]), F(1 - part.Uv[v * 2 + 1]));
                for (int v = 0; v < part.Count; v++)
                    sb.AppendFormat("vn {0} {1} {2}\n", F(part.Nrm[v * 3]), F(part.Nrm[v * 3 + 1]), F(-part.Nrm[v * 3 + 2]));
                foreach (var sp in subs)
                {
                    mats.Add(sp.Material);
                    sb.AppendLine("usemtl " + Fm1Materials.Name(sp.Material));
                    foreach (var t in Fm1Car.Triangles(sp.Strip, flip))
                    {
                        sb.Append('f');
                        foreach (int v in t) { int i = baseIndex + v; sb.AppendFormat(" {0}/{0}/{0}", i); }
                        sb.Append('\n');
                        tris++;
                    }
                }
                baseIndex += part.Count;
            }
            File.WriteAllText(Path.Combine(outDir, "car_lod" + lod + ".obj"), sb.ToString());
            Console.WriteLine("car_lod{0}.obj: {1} triangles", lod, tris);
        }

        var mtl = new StringBuilder("# Turn10 FM1 material names (default.xbe enum; GlobalMaterialSettings.xml Day/Night sections)\n");
        foreach (int m in mats)
        {
            mtl.AppendFormat("newmtl {0}\nKd 0.8 0.8 0.8\n", Fm1Materials.Name(m));
            if (livery != null) mtl.AppendLine("map_Kd livery0hi.png");
            mtl.AppendLine();
        }
        File.WriteAllText(Path.Combine(outDir, "car.mtl"), mtl.ToString());

        if (livery != null)
        {
            int w, h;
            int[] px = livery.DecodeTop(out w, out h);
            Save(px, w, h, Path.Combine(outDir, "livery0hi.png"));
            UvCheck(car, px, w, h, Path.Combine(outDir, "uvcheck_lod1.png"));
        }
        return 0;
    }

    static double[] P(Fm1Part part, int v) { return new double[] { part.Pos[v * 3], part.Pos[v * 3 + 1], -part.Pos[v * 3 + 2] }; }

    static void Save(int[] px, int w, int h, string file)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, bd.Scan0 + y * bd.Stride, w);
            bmp.UnlockBits(bd);
            bmp.Save(file, ImageFormat.Png);
        }
    }

    // LOD1 UV wireframe over the atlas, one colour per material; prints the UV range per material.
    static void UvCheck(Fm1Car car, int[] px, int w, int h, string file)
    {
        var colours = new[] { Color.Red, Color.Lime, Color.Blue, Color.Magenta, Color.Cyan, Color.Orange, Color.Yellow, Color.DeepPink, Color.SpringGreen, Color.DodgerBlue };
        var ranges = new SortedDictionary<int, float[]>();
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var faded = px.Select(c => (c & unchecked((int)0xFF000000)) == 0 ? unchecked((int)0xFF202020) : unchecked((int)0xFF000000) | ((c >> 1) & 0x7F7F7F)).ToArray();
            for (int y = 0; y < h; y++) Marshal.Copy(faded, y * w, bd.Scan0 + y * bd.Stride, w);
            bmp.UnlockBits(bd);
            using (var g = Graphics.FromImage(bmp))
                foreach (var part in car.Parts)
                    foreach (var sp in part.Subparts.Where(x => x.Lod == 1))
                    {
                        float[] r;
                        if (!ranges.TryGetValue(sp.Material, out r)) ranges[sp.Material] = r = new[] { 9f, 9f, -9f, -9f, 0f };
                        var pen = new Pen(colours[sp.Material % colours.Length], 1);
                        foreach (var t in Fm1Car.Triangles(sp.Strip, false))
                        {
                            var pts = t.Select(v => new PointF(part.Uv[v * 2] * w, part.Uv[v * 2 + 1] * h)).ToArray();
                            foreach (int v in t)
                            {
                                r[0] = Math.Min(r[0], part.Uv[v * 2]); r[1] = Math.Min(r[1], part.Uv[v * 2 + 1]);
                                r[2] = Math.Max(r[2], part.Uv[v * 2]); r[3] = Math.Max(r[3], part.Uv[v * 2 + 1]);
                            }
                            r[4]++;
                            g.DrawPolygon(pen, pts);
                        }
                    }
            bmp.Save(file, ImageFormat.Png);
        }
        foreach (var kv in ranges)
            Console.WriteLine("  {0,2} {1,-16} ({2,-11}): {3,6} triangles, u {4:0.000}..{5:0.000}  v {6:0.000}..{7:0.000}", kv.Key, Fm1Materials.Name(kv.Key),
                colours[kv.Key % colours.Length].Name, kv.Value[4], kv.Value[0], kv.Value[2], kv.Value[1], kv.Value[3]);
    }
}
