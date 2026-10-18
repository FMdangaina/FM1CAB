// stats: quick checks for the FM1/FM2 comparison.
//   stats fm2vb <fm2 carbin> <offset hex> <count> <size>   nonzero bytes per vertex column
//   stats fm1dmg <fm1 carbin>                              damage record vs vertex (pos/normal deltas)
using System;
using System.Globalization;
using System.IO;
using System.Text;

static class Stats
{
    static void Main(string[] a)
    {
        byte[] d = File.ReadAllBytes(a[1]);
        if (a[0] == "fm2vb")
        {
            int off = int.Parse(a[2], NumberStyles.HexNumber), n = int.Parse(a[3]), sz = int.Parse(a[4]);
            var nz = new int[sz];
            for (int v = 0; v < n; v++) for (int k = 0; k < sz; k++) if (d[off + v * sz + k] != 0) nz[k]++;
            var sb = new StringBuilder();
            for (int k = 0; k < sz; k += 4) sb.AppendFormat("+{0}: {1:0}% ", k, 100.0 * (nz[k] + nz[k + 1] + nz[k + 2] + nz[k + 3]) / (4.0 * n));
            Console.WriteLine(sb);
            return;
        }
        // fm1dmg: walk parts like fm1dump
        int p = 0;
        Func<int> I32 = () => { int v = BitConverter.ToInt32(d, p); p += 4; return v; };
        Func<int> Chunk = () => { int s = p, size = I32(); I32(); while (d[p] != 0) p++; p++; return s + size; };
        Chunk(); p = Chunk(); p = Chunk();
        int parts = I32();
        long tot = 0, same = 0; double sumd = 0, maxd = 0; long nsame = 0;
        for (int i = 0; i < parts; i++)
        {
            int pend = Chunk(); p += 4; int nl = I32(); string name = Encoding.ASCII.GetString(d, p, nl); p += nl;
            int vend = Chunk(); int vc = I32(); int vs = p; p = vend;
            int dend = Chunk(); int dc = I32(); int ds = p; p = dend;
            if (dc == vc && dc > 0)
            {
                long ps = 0; double pmax = 0, psum = 0;
                for (int v = 0; v < vc; v++)
                {
                    double dd = 0; bool eq = true;
                    for (int k = 0; k < 3; k++)
                    {
                        int x0 = BitConverter.ToInt16(d, vs + v * 20 + k * 2), x1 = BitConverter.ToInt16(d, ds + v * 12 + k * 2);
                        dd += (double)(x1 - x0) * (x1 - x0); if (x0 != x1) eq = false;
                    }
                    for (int k = 3; k < 6; k++) if (BitConverter.ToInt16(d, vs + v * 20 + k * 2) != BitConverter.ToInt16(d, ds + v * 12 + k * 2)) { nsame += 0; }
                    dd = Math.Sqrt(dd) / 6553.6;
                    if (eq) ps++;
                    psum += dd; pmax = Math.Max(pmax, dd);
                }
                Console.WriteLine("{0,-22} verts {1,5}  same pos {2,5:0.0}%  mean move {3:0.000} m  max {4:0.000} m", name, vc, 100.0 * ps / vc, psum / vc, pmax);
                tot += vc; same += ps; sumd += psum; maxd = Math.Max(maxd, pmax);
            }
            p = pend;
        }
        Console.WriteLine("ALL verts {0} same pos {1:0.0}% mean move {2:0.000} m max {3:0.000} m (at 1/6553.6 m per unit)", tot, 100.0 * same / tot, sumd / tot, maxd);
    }
}
