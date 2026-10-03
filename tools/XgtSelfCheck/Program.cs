using System.Net;
using System.Net.Sockets;
using System.Text;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Plc.Drivers;
using Microsoft.Extensions.Logging.Abstractions;

// Self-consistency check ONLY: a fake XGT server written from the same spec as the driver.
// It does NOT prove the real XBM-DN32HP behaves identically.

var memory = new Dictionary<string, byte[]>();   // key = byte-address variable (e.g. %DB200) or bit/word var
int failures = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); if (!ok) failures++; }

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
bool bccAllOk = true;

_ = Task.Run(async () =>
{
    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        _ = Task.Run(() => Serve(client));
    }
});

async Task Serve(TcpClient client)
{
    var s = client.GetStream();
    var hdr = new byte[20];
    while (true)
    {
        int got = 0;
        while (got < 20) { int r = await s.ReadAsync(hdr.AsMemory(got, 20 - got)); if (r == 0) return; got += r; }
        int sum = 0; for (int i = 0; i < 19; i++) sum += hdr[i];
        if ((byte)sum != hdr[19]) bccAllOk = false;
        int len = hdr[16] | (hdr[17] << 8);
        var p = new byte[len];
        got = 0;
        while (got < len) { int r = await s.ReadAsync(p.AsMemory(got, len - got)); if (r == 0) return; got += r; }

        ushort cmd = (ushort)(p[0] | (p[1] << 8));
        ushort type = (ushort)(p[2] | (p[3] << 8));
        int varLen = p[8] | (p[9] << 8);
        string v = Encoding.ASCII.GetString(p, 10, varLen);

        byte[] resp;
        if (v.StartsWith("%ZZ"))  // unknown variable -> NAK
            resp = Payload((ushort)(cmd + 1), type, 0xFFFF, [0x12, 0x00]);
        else if (cmd == 0x0054)
        {
            byte[] data;
            if (type == 0x0014)
            {
                int cnt = p[10 + varLen] | (p[11 + varLen] << 8);
                data = new byte[cnt];
                if (memory.TryGetValue(v, out var m)) Array.Copy(m, data, Math.Min(m.Length, cnt));
            }
            else
            {
                data = memory.TryGetValue(v, out var m) ? m : new byte[type == 0 ? 1 : 2];
            }
            var body = new List<byte> { 1, 0, (byte)data.Length, (byte)(data.Length >> 8) };
            body.AddRange(data);
            resp = Payload(0x0055, type, 0, body.ToArray());
        }
        else
        {
            int dl = p[10 + varLen] | (p[11 + varLen] << 8);
            memory[v] = p.AsSpan(12 + varLen, dl).ToArray();
            resp = Payload(0x0059, type, 0, [1, 0]);
        }

        var frame = new byte[20 + resp.Length];
        Array.Copy(hdr, frame, 20);
        frame[13] = 0x11; // PLC -> client
        frame[16] = (byte)resp.Length; frame[17] = (byte)(resp.Length >> 8);
        await s.WriteAsync(frame.AsMemory(0, 20));
        await s.WriteAsync(resp);
    }
}

static byte[] Payload(ushort cmd, ushort type, ushort status, byte[] tail)
{
    var l = new List<byte> { (byte)cmd, (byte)(cmd >> 8), (byte)type, (byte)(type >> 8), 0, 0, (byte)status, (byte)(status >> 8) };
    l.AddRange(tail);
    return l.ToArray();
}

var cfg = new PlcConfiguration { IpAddress = "127.0.0.1", Port = port, ConnectionTimeoutMs = 2000, ReadTimeoutMs = 2000, WriteTimeoutMs = 2000 };
using var drv = new LsXgtDedicatedDriver(cfg, NullLogger<LsXgtDedicatedDriver>.Instance);

await drv.ConnectAsync();
Check("connect", await drv.IsConnectedAsync());

await drv.WriteWordAsync("%MW10", 0xBEEF);
Check("word roundtrip 0xBEEF", await drv.ReadWordAsync("%MW10") == 0xBEEF);

await drv.WriteBitAsync("%MX5", true);
Check("bit roundtrip true", await drv.ReadBitAsync("%MX5"));
await drv.WriteBitAsync("%MX5", false);
Check("bit roundtrip false", !await drv.ReadBitAsync("%MX5"));

await drv.WriteStringAsync("%DB200", "ABC123");
Check("string roundtrip (byte addr)", await drv.ReadStringAsync("%DB200", 16) == "ABC123");

await drv.WriteStringAsync("%DW100", "HELLO");   // word 100 -> byte 200 -> server key %DB200
Check("string word->byte address mapping", memory.ContainsKey("%DB200") && await drv.ReadStringAsync("%DB200", 8) == "HELLO");

try { await drv.ReadWordAsync("%ZZ1"); Check("NAK raises PlcRequestException", false); }
catch (PlcRequestException ex) { Console.WriteLine("   NAK message: " + ex.Message); Check("NAK raises PlcRequestException", ex.Message.Contains("0x1200") || ex.Message.Contains("FFFF")); }

try { await drv.ReadWordAsync("M100"); Check("short address rejected", false); }
catch (PlcRequestException) { Check("short address rejected", true); }

Check("connection still alive after NAK/invalid", await drv.IsConnectedAsync() && await drv.ReadWordAsync("%MW10") == 0xBEEF);
Check("BCC valid on every request", bccAllOk);

var emptyCfg = new PlcConfiguration { IpAddress = "NEEDS_CONFIGURATION", Port = 2004 };
using var drv2 = new LsXgtDedicatedDriver(emptyCfg, NullLogger<LsXgtDedicatedDriver>.Instance);
try { await drv2.ConnectAsync(); Check("no IP -> config error (no hardcoded IP)", false); }
catch (PlcException ex) { Check("no IP -> config error (no hardcoded IP)", ex.Message.Contains("PLC_CONFIG_ERROR")); }

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures;
