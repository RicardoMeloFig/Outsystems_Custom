// KeyProbe - scans a running Service Studio process heap for the productKey /
// secretKey needed by the headless OML model loader (LoadESpace / LoadWithoutUpgrades).
//
// Single object pass. Uses only field metadata (Name/IsObjectReference/Type/Offset)
// + the ClrMD DataReader to read string values directly from process memory, so it
// is immune to the ClrField.ReadString / ClrHeap.EnumerateTypes API changes in
// ClrMD 3.0.442202.
//
// Usage: dotnet run -c Release -- <pid>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime;

class Program
{
    static ClrHeap heap;
    static IDataReader reader;
    static int _stringLengthOffset;
    static int _firstCharOffset;

    static Dictionary<string, byte> typeCache = new(); // 0=skip,1=interesting
    static Dictionary<string, int> dumpedInst = new();

    static string[] kw = { "ProductKey","Secret","Handshake","ModelService","License",
                           "PlatformService","PlatformConnection","ServerConnection",
                           "CanOpen","OmlLoader","ModelContext","ModelServices" };

    static void Main(string[] args)
    {
        int pid = args.Length > 0 ? int.Parse(args[0]) : 0;
        if (pid == 0) { Console.Error.WriteLine("usage: KeyProbe <pid>"); return; }
        Console.WriteLine($"Attaching (read-only) to PID {pid}...");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        heap = runtime.Heap;
        reader = runtime.DataTarget.DataReader;
        _stringLengthOffset = IntPtr.Size;
        _firstCharOffset = IntPtr.Size + 4;
        Console.WriteLine($"Heap ready. CanWalkHeap={heap.CanWalkHeap}");
        Scan();
    }

    static IEnumerable<ClrField> AllFields(ClrType t)
    {
        for (var tt = t; tt != null; tt = tt.BaseType)
            foreach (var f in tt.Fields) yield return f;
    }

    static bool IsKeyFieldName(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        var l = n.ToLowerInvariant();
        return l.Contains("productkey") || l.Contains("secretkey") ||
               (l.Contains("product") && (l.EndsWith("key") || l.EndsWith("_key")));
    }

    // 0=skip, 1=interesting (dump all string fields)
    static byte ClassifyType(string name, ClrType t)
    {
        if (name.EndsWith(".OmlHeader", StringComparison.Ordinal)) return 1;
        if (name.EndsWith(".ESpace", StringComparison.Ordinal)) return 1;
        if (name.EndsWith("+ESpace", StringComparison.Ordinal)) return 1;
        foreach (var k in kw) if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return 1;
        foreach (var f in AllFields(t))
            if (f.IsObjectReference && f.Type?.Name == "System.String" && IsKeyFieldName(f.Name)) return 1;
        return 0;
    }

    static void Scan()
    {
        Console.WriteLine("\n=== SCANNING ===");
        var re = new Regex(@"^[A-Za-z0-9+/=_\-:.]+$");
        var seenStrings = new HashSet<string>();
        int stringCandidates = 0, printedStrings = 0, totalObjs = 0;

        foreach (var obj in heap.EnumerateObjects())
        {
            totalObjs++;
            var t = obj.Type; if (t is null) continue;
            var name = t.Name ?? "";

            if (name == "System.String")
            {
                var s = ReadStringAt(obj.Address);
                if (string.IsNullOrEmpty(s)) continue;
                if (s.Length < 16 || s.Length > 1024) continue;
                if (s.Any(char.IsWhiteSpace)) continue;
                if (!re.IsMatch(s)) continue;
                if (!(s.Any(char.IsDigit) && s.Any(char.IsLetter))) continue;
                if (!seenStrings.Add(s)) continue;
                stringCandidates++;
                if (printedStrings < 400)
                {
                    Console.WriteLine($"  STR [{s.Length,4}] {Preview(s)}");
                    printedStrings++;
                }
                continue;
            }

            if (!typeCache.TryGetValue(name, out byte cls))
            { cls = ClassifyType(name, t); typeCache[name] = cls; }
            if (cls == 0) continue;

            if (!dumpedInst.TryGetValue(name, out var cnt)) cnt = 0;
            if (cnt >= 5) continue;
            dumpedInst[name] = cnt + 1;

            Console.WriteLine($"\n  --- {name} @ 0x{obj.Address:X} (inst {cnt + 1}) ---");
            foreach (var f in AllFields(t))
            {
                if (!f.IsObjectReference || f.Type?.Name != "System.String") continue;
                ulong fieldAddr = obj.Address + (ulong)f.Offset;
                if (!reader.Read<ulong>(fieldAddr, out ulong strPtr) || strPtr == 0) continue;
                var val = ReadStringAt(strPtr);
                if (string.IsNullOrEmpty(val)) continue;
                bool keyish = val.Length >= 12 && !val.Any(char.IsWhiteSpace) &&
                              val.Any(char.IsDigit) && val.Any(char.IsLetter);
                string tag = keyish || IsKeyFieldName(f.Name) ? "  <<<KEY?" : "";
                Console.WriteLine($"      {f.Name} ({val.Length}) = {Preview(val)}{tag}");
            }
        }

        Console.WriteLine($"\n  scanned {totalObjs} objects");
        Console.WriteLine($"  unique key-like strings: {stringCandidates} (printed {printedStrings})");
        Console.WriteLine($"  interesting types dumped: {dumpedInst.Count}");
        foreach (var n in dumpedInst.Keys.OrderBy(x => x)) Console.WriteLine($"    {n}");
    }

    static string ReadStringAt(ulong addr)
    {
        try
        {
            if (!reader.Read<int>(addr + (ulong)_stringLengthOffset, out int length)) return null;
            if (length <= 0 || length > 1_000_000) return null;
            byte[] buf = new byte[length * 2];
            int read = reader.Read(addr + (ulong)_firstCharOffset, buf.AsSpan());
            if (read < 2) return null;
            return Encoding.Unicode.GetString(buf, 0, read);
        }
        catch { return null; }
    }

    static string Preview(string s)
    {
        if (s.Length <= 120) return s;
        return s.Substring(0, 60) + "..." + s.Substring(s.Length - 40);
    }
}
