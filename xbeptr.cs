// xbeptr: finds arrays of pointers to strings in an original Xbox .xbe and prints them in pointer order.
//
//   xbeptr <default.xbe> <string> [string...]   arrays that contain a pointer to any of these strings
//
// Build: csc /nologo /platform:x86 /optimize xbeptr.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

static class XbePtr
{
    static byte[] d;
    static uint baseAddr;
    static List<uint[]> sections = new List<uint[]>();   // va, vsize, raw, rawsize

    static uint U32(long o) { return BitConverter.ToUInt32(d, (int)o); }

    static long VaToRaw(uint va)
    {
        foreach (var s in sections)
            if (va >= s[0] && va < s[0] + Math.Min(s[1], s[3])) return s[2] + (va - s[0]);
        if (va >= baseAddr && va < baseAddr + U32(0x108)) return va - baseAddr;   // headers
        return -1;
    }

    static uint RawToVa(long raw)
    {
        foreach (var s in sections)
            if (raw >= s[2] && raw < s[2] + s[3]) return (uint)(s[0] + (raw - s[2]));
        return 0;
    }

    static string Str(uint va)
    {
        long r = VaToRaw(va);
        if (r < 0) return null;
        int n = 0;
        while (r + n < d.Length && d[r + n] >= 0x20 && d[r + n] < 0x7F && n < 80) n++;
        if (n < 2 || r + n >= d.Length || d[r + n] != 0) return null;
        return Encoding.ASCII.GetString(d, (int)r, n);
    }

    static int Main(string[] a)
    {
        d = File.ReadAllBytes(a[0]);
        if (Encoding.ASCII.GetString(d, 0, 4) != "XBEH") { Console.WriteLine("not an XBE"); return 1; }
        baseAddr = U32(0x104);
        uint count = U32(0x11C), hdrs = U32(0x120);
        for (uint i = 0; i < count; i++)
        {
            long o = hdrs - baseAddr + i * 56;
            sections.Add(new[] { U32(o + 4), U32(o + 8), U32(o + 12), U32(o + 16) });
        }
        if (a.Length == 4 && a[1] == "pairs")
        {
            // pairs <raw hex> <count>: (string pointer, u32) entries, 8 bytes each
            long o = Convert.ToInt64(a[2], 16);
            for (int i = 0; i < int.Parse(a[3]); i++, o += 8)
                Console.WriteLine("  raw 0x{0:X} [{1,3}] value {2,-6} {3}", o, i, U32(o + 4), Str(U32(o)) ?? "(0x" + U32(o).ToString("X") + ")");
            return 0;
        }
        var wanted = new HashSet<string>(a.Skip(1));
        var done = new HashSet<long>();
        for (long o = 0; o + 4 <= d.Length; o += 4)
        {
            string s = Str(U32(o));
            if (s == null || !wanted.Contains(s) || done.Contains(o)) continue;
            // grow the run of string pointers both ways
            long start = o, end = o;
            while (start - 4 >= 0 && Str(U32(start - 4)) != null) start -= 4;
            while (end + 4 + 4 <= d.Length && Str(U32(end + 4)) != null) end += 4;
            for (long k = start; k <= end; k += 4) done.Add(k);
            Console.WriteLine("array at raw 0x{0:X} (va 0x{1:X8}), {2} entries:", start, RawToVa(start), (end - start) / 4 + 1);
            for (long k = start; k <= end; k += 4) Console.WriteLine("  [{0,3}] {1}", (k - start) / 4, Str(U32(k)));
        }
        return 0;
    }
}
