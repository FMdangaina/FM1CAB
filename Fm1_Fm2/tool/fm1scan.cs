// fm1scan: finds every tagged chunk ([u32 size][u32 version][name\0]) in an FM1 .carbin
// and prints them as a tree by containment. Research only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

class Chunk { public int Off, Size, Ver, Hdr; public string Name; public List<Chunk> Kids = new List<Chunk>(); }

static class Fm1Scan
{
    static byte[] d;

    static int U32(int o) { return BitConverter.ToInt32(d, o); }

    static Chunk TryChunk(int o)
    {
        if (o + 12 > d.Length) return null;
        int size = U32(o), ver = U32(o + 4);
        if (size < 9 || o + size > d.Length || ver < 0 || ver > 64) return null;
        int p = o + 8, n = 0;
        while (p + n < d.Length && n < 32 && d[p + n] != 0)
        {
            byte c = d[p + n];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return null;
            n++;
        }
        if (n < 3 || n >= 32 || d[p + n] != 0) return null;
        if (!(d[p] >= 'A' && d[p] <= 'Z')) return null;
        return new Chunk { Off = o, Size = size, Ver = ver, Name = Encoding.ASCII.GetString(d, p, n), Hdr = 8 + n + 1 };
    }

    static void Main(string[] args)
    {
        d = File.ReadAllBytes(args[0]);
        int depthLimit = args.Length > 1 ? int.Parse(args[1]) : 99;
        // candidate chunks
        var all = new List<Chunk>();
        for (int o = 0; o < d.Length - 12; o++) { var c = TryChunk(o); if (c != null) all.Add(c); }
        // build tree: a chunk is a child of the smallest chunk that contains it
        var root = new Chunk { Off = -1, Size = int.MaxValue, Name = "<file>" };
        var stack = new List<Chunk> { root };
        all.Sort((a, b) => a.Off != b.Off ? a.Off.CompareTo(b.Off) : b.Size.CompareTo(a.Size));
        foreach (var c in all)
        {
            while (stack.Count > 1)
            {
                var t = stack[stack.Count - 1];
                if (c.Off >= t.Off + t.Hdr && c.Off + c.Size <= t.Off + t.Size) break;
                stack.RemoveAt(stack.Count - 1);
            }
            stack[stack.Count - 1].Kids.Add(c);
            stack.Add(c);
        }
        Print(root, 0, depthLimit);
        // name histogram
        var hist = new SortedDictionary<string, int>();
        foreach (var c in all) { string k = c.Name + " v" + c.Ver; int v; hist.TryGetValue(k, out v); hist[k] = v + 1; }
        Console.WriteLine("--- chunk names ---");
        foreach (var kv in hist) Console.WriteLine("{0,6} {1}", kv.Value, kv.Key);
    }

    static void Print(Chunk c, int depth, int limit)
    {
        if (c.Off >= 0)
        {
            // gaps: bytes in the parent's data not covered by children are reported by the caller
            Console.WriteLine("{0}{1:X7} {2} v{3} size {4}", new string(' ', depth * 2), c.Off, c.Name, c.Ver, c.Size);
        }
        if (depth >= limit) return;
        // collapse runs of same-name children
        int i = 0;
        while (i < c.Kids.Count)
        {
            int j = i;
            while (j + 1 < c.Kids.Count && c.Kids[j + 1].Name == c.Kids[i].Name && c.Kids[i].Kids.Count == 0 && c.Kids[j + 1].Kids.Count == 0) j++;
            if (j - i >= 3)
            {
                Console.WriteLine("{0}{1:X7} {2} v{3} x{4} (leaf run)", new string(' ', (depth + 1) * 2), c.Kids[i].Off, c.Kids[i].Name, c.Kids[i].Ver, j - i + 1);
                i = j + 1;
                continue;
            }
            Print(c.Kids[i], depth + 1, limit);
            i++;
        }
    }
}
