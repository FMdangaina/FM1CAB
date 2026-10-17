// Forza Motorsport 1 (original Xbox) .cab / .ca2 reader, extractor and format checker.
//
//   fm1cab list    <file.cab|file.ca2>
//   fm1cab extract <file.cab|file.ca2> <out dir>
//   fm1cab verify  <cabs folder>              every format rule, on every .cab and .ca2
//   fm1cab hashhunt <file> [records]          string hashes x path spellings vs record field6
//
// Build: C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /platform:x86 fm1cab.cs
// Format: see forza_fm1_cab_reverse_engineering.md next to this file.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

class CabEntry
{
    public int Index;
    public uint DataOffset, CompressedSize, Size, PathOffset, Field5, Hash;
    public string Path;
    public bool Stored { get { return CompressedSize == Size; } }
}

class Fm1Cab
{
    public const uint Magic = 0xC0C0AAAA;
    public const int RecordTable = 0x132, RecordSize = 24, BasePathOffset = 0x32, BasePathSize = 0x100;

    public byte[] Data;
    public uint Version, Count, Flags0C, FileSize, UnpackedSize, PathTableSize, PathTableSize2;
    public ushort Flag30;
    public string BasePath;
    public int PathTableStart;
    public List<CabEntry> Entries = new List<CabEntry>();
    public List<string> Problems = new List<string>();   // rule violations found while parsing
    public int Ca2Chunks;                                  // 0 for a plain .cab

    public static Fm1Cab Load(string file)
    {
        byte[] d = File.ReadAllBytes(file);
        int chunks = 0;
        if (d.Length > 2 && d[0] == 0x78)
            d = Ca2.Inflate(d, out chunks);
        Fm1Cab cab = Parse(d);
        cab.Ca2Chunks = chunks;
        return cab;
    }

    static uint U32(byte[] d, int o) { return BitConverter.ToUInt32(d, o); }

    public static Fm1Cab Parse(byte[] d)
    {
        if (d.Length < RecordTable || U32(d, 0) != Magic)
            throw new InvalidDataException("not an FM1 CAB (magic)");
        var c = new Fm1Cab { Data = d };
        c.Version = U32(d, 4);
        c.Count = U32(d, 8);
        c.Flags0C = U32(d, 0x0C);
        c.FileSize = U32(d, 0x10);
        c.UnpackedSize = U32(d, 0x14);
        c.PathTableSize = U32(d, 0x18);
        c.PathTableSize2 = U32(d, 0x1C);
        c.Flag30 = BitConverter.ToUInt16(d, 0x30);
        int z = Array.IndexOf(d, (byte)0, BasePathOffset, BasePathSize);
        c.BasePath = Encoding.ASCII.GetString(d, BasePathOffset, (z < 0 ? BasePathOffset + BasePathSize : z) - BasePathOffset);
        if (c.Count == 0 || RecordTable + (long)c.Count * RecordSize > d.Length)
            throw new InvalidDataException("record table out of file");
        c.PathTableStart = RecordTable + (int)c.Count * RecordSize;
        if (c.PathTableStart + (long)c.PathTableSize > d.Length)
            throw new InvalidDataException("path table out of file");

        // Header rules
        if (c.Version != 15) c.Problems.Add("version " + c.Version);
        if (c.FileSize != d.Length) c.Problems.Add("0x10 file size " + c.FileSize + " != " + d.Length);
        if (c.PathTableSize != c.PathTableSize2) c.Problems.Add("0x18 != 0x1C");
        if (c.Flag30 != 1) c.Problems.Add("0x30 = " + c.Flag30);
        for (int i = 0x20; i < 0x30; i++)
            if (d[i] != 0) { c.Problems.Add("0x20-0x2F not zero"); break; }
        for (int i = BasePathOffset + c.BasePath.Length; i < RecordTable; i++)
            if (d[i] != 0) { c.Problems.Add("base path field not zero-padded"); break; }

        // Path table: NUL-terminated ASCII, as many as records, filling exactly PathTableSize
        var paths = new Dictionary<uint, string>();
        var tableOrder = new List<uint>();
        int pos = c.PathTableStart, end = c.PathTableStart + (int)c.PathTableSize;
        for (int i = 0; i < c.Count; i++)
        {
            int e = Array.IndexOf(d, (byte)0, pos, end - pos);
            if (e < 0) throw new InvalidDataException("path table ends early at entry " + i);
            uint rel = (uint)(pos - c.PathTableStart);
            paths[rel] = Encoding.ASCII.GetString(d, pos, e - pos);
            tableOrder.Add(rel);
            pos = e + 1;
        }
        if (pos != end) c.Problems.Add("path table size: strings end at +" + (pos - c.PathTableStart) + ", header says " + c.PathTableSize);
        // Path table order is the order files were added (folder by folder), not alphabetical: no rule.

        long unpacked = end;
        var used = new HashSet<uint>();
        for (int i = 0; i < c.Count; i++)
        {
            int r = RecordTable + i * RecordSize;
            var en = new CabEntry
            {
                Index = i, DataOffset = U32(d, r), CompressedSize = U32(d, r + 4), Size = U32(d, r + 8),
                PathOffset = U32(d, r + 12), Field5 = U32(d, r + 16), Hash = U32(d, r + 20)
            };
            string p;
            if (!paths.TryGetValue(en.PathOffset, out p))
                throw new InvalidDataException("record " + i + " path offset 0x" + en.PathOffset.ToString("X") + " is not a path start");
            if (!used.Add(en.PathOffset)) c.Problems.Add("path referenced twice: " + p);
            en.Path = p;
            if ((long)en.DataOffset + en.CompressedSize > d.Length) throw new InvalidDataException("record " + i + " payload out of file");
            if (en.Field5 != 0) c.Problems.Add("record " + i + " field5 = " + en.Field5);
            if (i > 0 && en.Hash <= c.Entries[i - 1].Hash) c.Problems.Add("records not sorted by strictly increasing hash at " + i);
            unpacked += en.Size;
            c.Entries.Add(en);
        }
        if (c.UnpackedSize != unpacked) c.Problems.Add("0x14 unpacked size " + c.UnpackedSize + " != " + unpacked);

        // Payloads: stored back to back in path-table order, from the path table end to EOF
        // (empty files: size 0, stored, same offset as the next payload)
        var byOffset = c.Entries.OrderBy(x => x.DataOffset).ThenBy(x => x.PathOffset).ToList();
        long at = end;
        int order = 0, gaps = 0;
        for (int i = 0; i < byOffset.Count; i++)
        {
            var b = byOffset[i];
            if (b.DataOffset != at && gaps++ < 3)
                c.Problems.Add(string.Format("payload of record {0} ({1}) at 0x{2:X}, previous ends at 0x{3:X} ({4:+#;-#} bytes)",
                    b.Index, b.Path, b.DataOffset, at, b.DataOffset - at));
            if (b.PathOffset != tableOrder[i]) order++;
            at = Math.Max(at, b.DataOffset + b.CompressedSize);
        }
        if (gaps > 3) c.Problems.Add(gaps + " payload gaps/overlaps in all");
        if (order > 0) c.Problems.Add(order + " payloads out of path-table order");
        if (at != d.Length) c.Problems.Add("payloads end at 0x" + at.ToString("X") + ", file ends at 0x" + d.Length.ToString("X"));
        return c;
    }

    public string FullPath(CabEntry e) { return BasePath + e.Path; }

    public byte[] Payload(CabEntry e)
    {
        if (e.Stored)
        {
            var raw = new byte[e.Size];
            Buffer.BlockCopy(Data, (int)e.DataOffset, raw, 0, (int)e.Size);
            return raw;
        }
        int consumed;
        return Zlib.Inflate(Data, (int)e.DataOffset, (int)e.CompressedSize, (int)e.Size, out consumed);
    }
}

static class Zlib
{
    public static uint Adler32(byte[] d, int off, int len)
    {
        uint a = 1, b = 0;
        for (int i = 0; i < len; i++) { a = (a + d[off + i]) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    // Inflates one zlib stream (2-byte header, deflate, big-endian Adler-32). expected < 0: read until the end.
    // consumed = bytes of the zlib stream, found from the Adler-32 trailer (DeflateStream does not report it).
    public static byte[] Inflate(byte[] d, int off, int len, int expected, out int consumed)
    {
        if (len < 6 || (d[off] & 0x0F) != 8 || ((d[off] << 8) | d[off + 1]) % 31 != 0 || (d[off + 1] & 0x20) != 0)
            throw new InvalidDataException("bad zlib header at 0x" + off.ToString("X"));
        byte[] outp;
        using (var ds = new DeflateStream(new MemoryStream(d, off + 2, len - 2), CompressionMode.Decompress))
        using (var ms = new MemoryStream(expected > 0 ? expected : 1 << 16))
        {
            ds.CopyTo(ms);
            outp = ms.ToArray();
        }
        if (expected >= 0 && outp.Length != expected)
            throw new InvalidDataException("inflated " + outp.Length + " bytes, expected " + expected);
        uint adler = Adler32(outp, 0, outp.Length);
        consumed = -1;
        for (int t = off + 2; t + 4 <= off + len; t++)
            if (d[t] == (byte)(adler >> 24) && d[t + 1] == (byte)(adler >> 16) && d[t + 2] == (byte)(adler >> 8) && d[t + 3] == (byte)adler)
            {
                consumed = t + 4 - off;
                if (expected >= 0 && consumed != len) continue;   // CAB entry: trailer must be the last 4 bytes
                break;
            }
        if (consumed < 0 || (expected >= 0 && consumed != len))
            throw new InvalidDataException("Adler-32 trailer not found / not at stream end");
        return outp;
    }
}

static class Ca2
{
    public const int ChunkSize = 0x400000, Align = 0x800;

    // .ca2 = a whole CAB split into 4 MiB pieces, each a zlib stream starting on a 2048-byte boundary,
    // zero padding between streams and at the end.
    public static byte[] Inflate(byte[] d, out int chunks)
    {
        var all = new MemoryStream();
        int pos = 0;
        chunks = 0;
        while (pos < d.Length && d[pos] != 0)
        {
            if (pos % Align != 0) throw new InvalidDataException("ca2 chunk not 2048-aligned at 0x" + pos.ToString("X"));
            byte[] part = InflateChunk(d, pos, out pos);
            if (part.Length != ChunkSize && pos < d.Length && d[pos] != 0)
                throw new InvalidDataException("ca2 chunk " + chunks + " is " + part.Length + " bytes but is not the last");
            all.Write(part, 0, part.Length);
            chunks++;
        }
        for (int i = pos; i < d.Length; i++)
            if (d[i] != 0) throw new InvalidDataException("ca2 non-zero data after last chunk at 0x" + i.ToString("X"));
        return all.ToArray();
    }

    static byte[] InflateChunk(byte[] d, int pos, out int next)
    {
        // The stream length is unknown: try each candidate end = trailer followed by zero padding up to the next boundary.
        int consumed;
        byte[] part = Zlib.Inflate(d, pos, d.Length - pos, -1, out consumed);
        uint adler = Zlib.Adler32(part, 0, part.Length);
        for (int t = pos + 2; t + 4 <= d.Length; t++)
        {
            if (d[t] != (byte)(adler >> 24) || d[t + 1] != (byte)(adler >> 16) || d[t + 2] != (byte)(adler >> 8) || d[t + 3] != (byte)adler) continue;
            int e = t + 4, b = (e + Align - 1) / Align * Align;
            bool zero = true;
            for (int i = e; i < Math.Min(b, d.Length) && zero; i++) zero = d[i] == 0;
            if (zero && (b >= d.Length || d[b] == 0x78 || d[b] == 0)) { next = Math.Min(b, d.Length); return part; }
        }
        throw new InvalidDataException("ca2 chunk end not found at 0x" + pos.ToString("X"));
    }
}

static class Program
{
    static int Main(string[] a)
    {
        try
        {
            if (a.Length == 2 && a[0] == "list") return List(a[1]);
            if (a.Length == 3 && a[0] == "extract") return Extract(a[1], a[2]);
            if (a.Length == 2 && a[0] == "verify") return Verify(a[1]);
            if (a.Length >= 2 && a[0] == "hashhunt") return HashHunt.Run(a[1], a.Length > 2 ? int.Parse(a[2]) : 8);
        }
        catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
        Console.WriteLine("fm1cab list|extract|verify|hashhunt ... (see source header)");
        return 1;
    }

    static int List(string file)
    {
        var c = Fm1Cab.Load(file);
        Console.WriteLine("version {0}, {1} entries, base '{2}', 0x0C={3}, unpacked {4}{5}", c.Version, c.Count, c.BasePath, c.Flags0C,
            c.UnpackedSize, c.Ca2Chunks > 0 ? ", ca2 chunks " + c.Ca2Chunks : "");
        foreach (var e in c.Entries.OrderBy(x => x.PathOffset))
            Console.WriteLine("{0:X8} {1,10} {2,10} {3} {4:X8} {5}", e.DataOffset, e.Size, e.CompressedSize, e.Stored ? "S" : "z", e.Hash, c.FullPath(e));
        foreach (var p in c.Problems) Console.WriteLine("PROBLEM: " + p);
        return c.Problems.Count == 0 ? 0 : 3;
    }

    static string SafeRelative(string p)
    {
        var parts = p.Replace('/', '\\').Split('\\');
        if (parts.Length == 0 || parts.Any(s => s.Length == 0 || s == "." || s == ".." || s.IndexOf(':') >= 0 || s.Any(ch => ch < 32)))
            throw new InvalidDataException("unsafe path in archive: " + p);
        return string.Join("\\", parts);
    }

    static int Extract(string file, string outDir)
    {
        var c = Fm1Cab.Load(file);
        var csv = new StringBuilder("index,hash,stored,compressed,size,sha256,path\r\n");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        using (var sha = SHA256.Create())
            foreach (var e in c.Entries.OrderBy(x => x.PathOffset))
            {
                string rel = SafeRelative(c.FullPath(e));
                if (!seen.Add(rel)) throw new InvalidDataException("duplicate path " + rel);
                byte[] p = c.Payload(e);
                string dest = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, p);
                total += p.Length;
                csv.AppendFormat("{0},{1:X8},{2},{3},{4},{5},{6}\r\n", e.Index, e.Hash, e.Stored ? 1 : 0, e.CompressedSize, e.Size,
                    BitConverter.ToString(sha.ComputeHash(p)).Replace("-", "").ToLowerInvariant(), rel);
            }
        File.WriteAllText(Path.Combine(outDir, Path.GetFileName(file) + ".manifest.csv"), csv.ToString());
        Console.WriteLine("OK: {0} files, {1:N0} bytes -> {2}", c.Count, total, outDir);
        foreach (var p in c.Problems) Console.WriteLine("PROBLEM: " + p);
        return 0;
    }

    static int Verify(string folder)
    {
        var files = Directory.GetFiles(folder, "*.cab").Concat(Directory.GetFiles(folder, "*.ca2")).OrderBy(x => x).ToList();
        int bad = 0;
        long entries = 0, stored = 0, zlibbed = 0;
        var f0c = new Dictionary<string, int>();
        var bases = new List<string>();
        var ext = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var problems = new List<string>();
            try
            {
                var c = Fm1Cab.Load(f);
                problems.AddRange(c.Problems);
                foreach (var e in c.Entries)
                {
                    try { c.Payload(e); }
                    catch (Exception ex) { problems.Add("record " + e.Index + " " + e.Path + ": " + ex.Message); }
                    if (e.Stored) stored++; else zlibbed++;
                    string x = Path.GetExtension(e.Path);
                    int n; ext.TryGetValue(x, out n); ext[x] = n + 1;
                }
                entries += c.Count;
                string k = "0x0C=" + c.Flags0C + (c.Entries.All(e => e.Stored) ? " (all stored)" : "");
                int m; f0c.TryGetValue(k, out m); f0c[k] = m + 1;
                if (c.BasePath.Length > 0) bases.Add(Path.GetFileName(f) + "='" + c.BasePath + "'");
            }
            catch (Exception ex) { problems.Add("FATAL " + ex.Message); }
            if (problems.Count > 0)
            {
                bad++;
                Console.WriteLine(Path.GetFileName(f) + ": " + string.Join(" | ", problems.Take(4)));
            }
        }
        Console.WriteLine();
        Console.WriteLine("{0} archives, {1} with problems; {2} entries: {3} zlib, {4} stored", files.Count, bad, entries, zlibbed, stored);
        Console.WriteLine("header 0x0C: " + string.Join(", ", f0c.Select(kv => kv.Key + " x" + kv.Value)));
        Console.WriteLine("base paths: " + string.Join(", ", bases));
        Console.WriteLine("extensions: " + string.Join(", ", ext.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key == "" ? "(none)" : kv.Key) + " " + kv.Value)));
        return bad == 0 ? 0 : 3;
    }
}

// Brute-force search for the field6 hash: many string hashes x path spellings.
static class HashHunt
{
    const uint M = 0xFFFFFFFF;
    static uint[] tR = Table(0xEDB88320, false), tM = Table(0x04C11DB7, true);

    static uint[] Table(uint poly, bool msb)
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = msb ? i << 24 : i;
            for (int k = 0; k < 8; k++)
                c = msb ? ((c & 0x80000000) != 0 ? (c << 1) ^ poly : c << 1) : ((c & 1) != 0 ? (c >> 1) ^ poly : c >> 1);
            t[i] = c;
        }
        return t;
    }

    static IEnumerable<KeyValuePair<string, uint>> Algos(byte[] s)
    {
        foreach (uint mul in new uint[] { 31, 33, 37, 101, 131, 65599, 0x01000193 })
            foreach (uint init in new uint[] { 0, 5381, 0x811C9DC5 })
            {
                uint h = init, hx = init, ha = init;
                foreach (byte b in s) { h = h * mul + b; hx = (hx * mul) ^ b; ha = (ha ^ b) * mul; }
                yield return new KeyValuePair<string, uint>("mul" + mul + "_i" + init.ToString("X") + "_add", h);
                yield return new KeyValuePair<string, uint>("mul" + mul + "_i" + init.ToString("X") + "_xor", hx);
                yield return new KeyValuePair<string, uint>("mul" + mul + "_i" + init.ToString("X") + "_xorfirst", ha);
            }
        uint sd = 0, oa = 0, elf = 0, rot = 0, rot7 = 0;
        foreach (byte b in s)
        {
            sd = b + (sd << 6) + (sd << 16) - sd;
            oa += b; oa += oa << 10; oa ^= oa >> 6;
            elf = (elf << 4) + b; uint g = elf & 0xF0000000; if (g != 0) elf ^= g >> 24; elf &= ~g;
            rot = ((rot << 5) | (rot >> 27)) ^ b;
            rot7 = ((rot7 << 7) | (rot7 >> 25)) + b;
        }
        oa += oa << 3; oa ^= oa >> 11; oa += oa << 15;
        yield return new KeyValuePair<string, uint>("sdbm", sd);
        yield return new KeyValuePair<string, uint>("oaat", oa);
        yield return new KeyValuePair<string, uint>("elf", elf);
        yield return new KeyValuePair<string, uint>("rot5xor", rot);
        yield return new KeyValuePair<string, uint>("rot7add", rot7);
        foreach (uint init in new uint[] { 0, M })
        {
            uint r = init, m = init;
            foreach (byte b in s) { r = tR[(r ^ b) & 0xFF] ^ (r >> 8); m = (m << 8) ^ tM[((m >> 24) ^ b) & 0xFF]; }
            yield return new KeyValuePair<string, uint>("crcR_i" + (init & 1), r);
            yield return new KeyValuePair<string, uint>("crcR_i" + (init & 1) + "_x", r ^ M);
            yield return new KeyValuePair<string, uint>("crcM_i" + (init & 1), m);
            yield return new KeyValuePair<string, uint>("crcM_i" + (init & 1) + "_x", m ^ M);
        }
        yield return new KeyValuePair<string, uint>("adler", Zlib.Adler32(s, 0, s.Length));
    }

    static IEnumerable<KeyValuePair<string, string>> Spellings(string basePath, string p)
    {
        string name = p.Substring(p.LastIndexOf('\\') + 1);
        var prefixes = new[] { "", basePath, "d:\\media\\", "media\\", "d:\\media\\cabs\\", "d:\\", "\\", "game:\\media\\" };
        foreach (var pre in prefixes.Distinct())
            foreach (var full in new[] { pre + p, pre + name })
                foreach (var cs in new[] { full, full.ToLowerInvariant(), full.ToUpperInvariant() })
                {
                    yield return new KeyValuePair<string, string>("[" + pre + "]", cs);
                    yield return new KeyValuePair<string, string>("[" + pre + "]/", cs.Replace('\\', '/'));
                }
    }

    public static int Run(string file, int records)
    {
        var c = Fm1Cab.Load(file);
        var hits = new Dictionary<string, int>();
        foreach (var e in c.Entries.Take(records))
            foreach (var sp in Spellings(c.BasePath, e.Path))
                foreach (var enc in new[] { Encoding.ASCII, Encoding.Unicode, Encoding.BigEndianUnicode })
                    foreach (var withNul in new[] { false, true })
                    {
                        byte[] s = enc.GetBytes(sp.Value + (withNul ? "\0" : ""));
                        foreach (var al in Algos(s))
                        {
                            uint v = al.Value, sw = (v >> 24) | ((v >> 8) & 0xFF00) | ((v << 8) & 0xFF0000) | (v << 24);
                            if (v != e.Hash && sw != e.Hash) continue;
                            string k = sp.Key + " " + enc.WebName + (withNul ? "+nul " : " ") + al.Key + (v == e.Hash ? "" : " bswap")
                                + (sp.Value == sp.Value.ToLowerInvariant() ? " lower" : sp.Value == sp.Value.ToUpperInvariant() ? " upper" : "");
                            int n; hits.TryGetValue(k, out n); hits[k] = n + 1;
                        }
                    }
        foreach (var kv in hits.OrderByDescending(x => x.Value)) Console.WriteLine("{0}/{1} {2}", kv.Value, records, kv.Key);
        if (hits.Count == 0) Console.WriteLine("no hit on " + records + " records");
        return 0;
    }
}
