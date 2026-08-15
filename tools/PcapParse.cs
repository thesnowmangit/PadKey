// Minimal pcapng + USBPcap pseudo-header reader.
// Prints the host -> device transfers so the vendor tool's framing becomes visible.
// Build: csc /target:exe /out:pcapparse.exe PcapParse.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class PcapParse
{
    private class Group
    {
        public int Count;
        public string Hex;
        public byte Endpoint;
        public byte Transfer;
        public bool FromDevice;
        public int DataLen;
    }

    private static string Hex(byte[] b, int off, int len)
    {
        var sb = new StringBuilder(len * 3);
        for (int i = 0; i < len && off + i < b.Length; i++) sb.Append(b[off + i].ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    private static string TransferName(byte t)
    {
        switch (t)
        {
            case 0: return "ISOCH";
            case 1: return "INTR ";
            case 2: return "CTRL ";
            case 3: return "BULK ";
            default: return "?    ";
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: pcapparse <dosya> [maxbayt] [--seq N]"); return 1; }
        int show = args.Length > 1 ? int.Parse(args[1]) : 16;
        int seqLimit = 0;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--seq") seqLimit = int.Parse(args[i + 1]);
        int seqShown = 0;
        long t0 = 0;

        byte[] f = File.ReadAllBytes(args[0]);
        Console.WriteLine("dosya: {0} bayt", f.Length);

        var outGroups = new Dictionary<string, Group>();
        var inGroups = new Dictionary<string, Group>();
        int packets = 0, outCount = 0, inCount = 0;

        // USBPcapCMD writes classic libpcap: magic D4C3B2A1, DLT 249 (USBPCAP).
        uint magic = BitConverter.ToUInt32(f, 0);
        if (magic != 0xA1B2C3D4) { Console.WriteLine("beklenmeyen pcap magic: {0:X8}", magic); return 1; }
        Console.WriteLine("link tipi: {0}", BitConverter.ToUInt32(f, 20));

        int pos = 24;   // global header
        while (pos + 16 <= f.Length)
        {
            int capLen = BitConverter.ToInt32(f, pos + 8);
            int dataOff = pos + 16;
            if (capLen < 0 || dataOff + capLen > f.Length) break;

            {
                if (capLen >= 27)
                {
                    packets++;
                    int p = dataOff;
                    int headerLen = BitConverter.ToUInt16(f, p);
                    byte info = f[p + 16];
                    byte endpoint = f[p + 21];
                    byte transfer = f[p + 22];
                    int dataLength = BitConverter.ToInt32(f, p + 23);
                    bool fromDevice = (info & 0x01) != 0;

                    if (dataLength > 0 && headerLen > 0 && p + headerLen + dataLength <= f.Length)
                    {
                        int n = Math.Min(show, dataLength);

                        // Chronological view of the host -> device conversation, which is
                        // where the vendor tool's connect sequence becomes readable.
                        if (seqLimit > 0 && seqShown < seqLimit)
                        {
                            long ts = (long)BitConverter.ToUInt32(f, pos) * 1000000L + BitConverter.ToUInt32(f, pos + 4);
                            if (t0 == 0) t0 = ts;
                            if (!fromDevice || endpoint == 0x82)
                            {
                                seqShown++;
                                Console.WriteLine("{0,8:F3}s {1} ep 0x{2:X2} {3}", (ts - t0) / 1000000.0,
                                    fromDevice ? "<-" : "->", endpoint, Hex(f, p + headerLen, n));
                            }
                        }

                        string key = string.Format("{0:X2}|{1}|{2}", endpoint, transfer, Hex(f, p + headerLen, n));
                        var map = fromDevice ? inGroups : outGroups;
                        Group g;
                        if (!map.TryGetValue(key, out g))
                        {
                            g = new Group();
                            g.Hex = Hex(f, p + headerLen, n);
                            g.Endpoint = endpoint;
                            g.Transfer = transfer;
                            g.FromDevice = fromDevice;
                            g.DataLen = dataLength;
                            map[key] = g;
                        }
                        g.Count++;
                        if (fromDevice) inCount++; else outCount++;
                    }
                }
            }

            pos = dataOff + capLen;
        }

        Console.WriteLine("paket: {0}   host->cihaz: {1}   cihaz->host: {2}", packets, outCount, inCount);
        Console.WriteLine();
        Console.WriteLine("=== HOST -> CIHAZ (uygulamanin gonderdikleri) ===");
        Dump(outGroups);
        Console.WriteLine();
        Console.WriteLine("=== CIHAZ -> HOST (ilk 25 desen) ===");
        Dump(inGroups, 25);
        return 0;
    }

    private static void Dump(Dictionary<string, Group> map, int limit = 60)
    {
        var list = new List<Group>(map.Values);
        list.Sort(delegate (Group a, Group b) { return b.Count.CompareTo(a.Count); });
        int i = 0;
        foreach (var g in list)
        {
            if (i++ >= limit) { Console.WriteLine("  ... ({0} desen daha)", list.Count - limit); break; }
            Console.WriteLine("  ep 0x{0:X2} {1} len={2,-4} x{3,-6}  {4}",
                g.Endpoint, TransferName(g.Transfer), g.DataLen, g.Count, g.Hex);
        }
    }
}
