using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Krp.Dns.WinDivertNative;

namespace Krp.Dns;

/// <summary>
/// Spoofs DNS for selected hostnames using WinDivert.
/// <para>
/// Opens a WinDivert handle for outbound UDP/53 queries, inspects each query’s
/// questions, and for any hostname matching a given endpoint returns
/// an immediate forged DNS response containing a single A/AAAA record pointing to
/// the mapped loopback IP. The forged packet is injected inbound so the OS resolver accepts it.
/// Non-matching queries are passed through unchanged.
/// </para>
/// <para>
/// <list type="bullet">
/// <item>Requires Administrator (loads the WinDivert driver).</item>
/// <item>IPv4 frame handling only (reply is IPv4 UDP; AAAA supported in payload when target IP is IPv6).</item>
/// <item>Skips reverse lookups (<c>*.in-addr.arpa</c>, <c>*.ip6.arpa</c>) and non-IN class questions.</item>
/// <item>Short TTL (5s) so changes propagate quickly.</item>
/// <item>Encrypted DNS (DoH/DoT) will bypass this (no UDP/53 traffic).</item>
/// </list>
/// </para>
/// </summary>
public class DnsWinDivertHandler : IDnsHandler, IDisposable
{
    private readonly ILogger<DnsWinDivertHandler> _logger;
    private readonly ConcurrentDictionary<string, IPAddress> _redirectMap = new(StringComparer.OrdinalIgnoreCase);
    private Thread _thread;
    private SafeWinDivertHandle _handle;

    public DnsWinDivertHandler(ILogger<DnsWinDivertHandler> logger)
    {
        _logger = logger;
    }

    public Task UpdateAsync(List<string> hostnames)
    {
        _redirectMap.Clear();
        foreach (var line in hostnames)
        {
            // Host names input like: "127.0.0.1 myapp.local"
            var parts = line.Split(' ');
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var ip))
            {
                _redirectMap[parts[1]] = ip;
            }
        }

        if (_thread is null || !_thread.IsAlive)
        {
            _thread = new Thread(HandleQueries) { IsBackground = true, Name = "DNS-Spoofer" };
            _thread.Start();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    private void HandleQueries()
    {
        const string filter = "outbound and ip and udp.DstPort == 53";
        _handle = WinDivertOpen(filter, WINDIVERT_LAYER.Network, 0, WINDIVERT_OPEN_FLAGS.None);

        var buffer = new byte[4096];
        var address = new byte[WINDIVERT_ADDRESS_SIZE];

        while (true)
        {
            if (!WinDivertRecv(_handle, buffer, (uint)buffer.Length, out var len, address))
            {
                continue;
            }

            var raw = new byte[len];
            for (var i = 0; i < len; i++)
            {
                raw[i] = buffer[i];
            }

            var packet = Packet.ParsePacket(LinkLayers.Raw, raw);
            var ip4 = packet.Extract<IPv4Packet>();
            var udp = packet.Extract<UdpPacket>();
            var payload = udp?.PayloadData;

            if (ip4 is null || udp is null || payload is null || payload.Length < 12)
            {
                WinDivertSend(_handle, buffer, len, out _, address);
                continue;
            }

            // DNS: QR=0 => query.
            var isQuery = (payload[2] & 0x80) == 0;
            if (!isQuery)
            {
                WinDivertSend(_handle, buffer, len, out _, address);
                continue;
            }

            // Parse first A/AAAA question we care about.
            if (!TryGetWantedQuestion(payload, out var qName, out _, out var qClass))
            {
                WinDivertSend(_handle, buffer, len, out _, address);
                continue;
            }

            // If endpoint exists with this host name.
            if (!_redirectMap.TryGetValue(qName, out var targetIp))
            {
                WinDivertSend(_handle, buffer, len, out _, address);
                continue;
            }

            // Craft DNS response with spoofed IP.
            var response = BuildDnsResponseBytes((ushort)((payload[0] << 8) | payload[1]), qName, qClass, targetIp, 5);

            var outUdp = new UdpPacket(udp.DestinationPort, udp.SourcePort)
            {
                PayloadData = response,
            };

            var outIp = new IPv4Packet(ip4.DestinationAddress, ip4.SourceAddress)
            {
                TimeToLive = 64, 
                PayloadPacket = outUdp,
            };

            outUdp.UpdateCalculatedValues();
            outIp.UpdateCalculatedValues();

            var outBytes = outIp.Bytes;
            var outBuf = new byte[outBytes.Length];

            for (var i = 0; i < outBytes.Length; i++)
            {
                outBuf[i] = outBytes[i];
            }

            var inboundAddr = (byte[])address.Clone();
            inboundAddr[0] = (byte)(inboundAddr[0] & ~0x01);

            WinDivertHelperCalcChecksums_NoAddr(outBuf, (uint)outBuf.Length, IntPtr.Zero, WINDIVERT_HELPER_CHECKSUM_FLAGS.All);
            WinDivertSend(_handle, outBuf, (uint)outBuf.Length, out _, inboundAddr);
        }
    }

    private static bool TryGetWantedQuestion(byte[] dns, out string qName, out ushort qType, out ushort qClass)
    {
        qName = "";
        qType = 0;
        qClass = 0;
        if (dns.Length < 12)
        {
            return false;
        }

        var qd = (dns[4] << 8) | dns[5];
        if (qd == 0)
        {
            return false;
        }

        var ptr = 12;
        for (var i = 0; i < qd; i++)
        {
            if (!TryReadQName(dns, 0, ref ptr, out var hostname))
            {
                return false;
            }

            if (ptr + 4 > dns.Length)
            {
                return false;
            }

            var t = Read16(dns, ptr + 0);
            var c = Read16(dns, ptr + 2);
            ptr += 4;

            // Skip reverse zones + non-IN.
            if (hostname.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase) ||
                hostname.EndsWith(".ip6.arpa", StringComparison.OrdinalIgnoreCase) ||
                c != 1)
            {
                continue;
            }

            if (t is 1 or 28)
            {
                qName = hostname;
                qType = t;
                qClass = c;
                return true;
            }
        }

        return false;
    }

    private static byte[] BuildDnsResponseBytes(ushort transactionId, string qNameWire, ushort qClass, IPAddress ip, int ttlSeconds)
    {
        var isAAAA = ip.AddressFamily == AddressFamily.InterNetworkV6;
        var rdata = ip.GetAddressBytes();
        var type = (ushort)(isAAAA ? 28 : 1);
        var b = new List<byte>(96);
        W16(b, transactionId);
        ushort flags = 0x8000 /*QR*/ | 0x0100 /*RD*/ | 0x0080 /*RA*/;
        W16(b, flags);
        W16(b, 1); // QD
        W16(b, 1); // AN
        W16(b, 0); // NS
        W16(b, 0); // AR

        // Question (echo, force type to match the IP family)
        WName(b, qNameWire);
        W16(b, type);
        W16(b, qClass == 0 ? (ushort)1 : qClass);

        // Answer
        WName(b, qNameWire);
        W16(b, type);
        W16(b, 1);
        W32(b, (uint)ttlSeconds);
        W16(b, (ushort)rdata.Length);
        b.AddRange(rdata);
        return b.ToArray();
    }

    private static bool TryReadQName(byte[] data, int dnsBase, ref int ptr, out string name)
    {
        var sb = new StringBuilder();
        var guard = 0;
        while (ptr < data.Length && guard++ < 128)
        {
            var len = data[ptr++];
            if (len == 0)
            {
                name = sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : "";
                return true;
            }

            if ((len & 0xC0) == 0xC0)
            {
                if (ptr >= data.Length)
                {
                    break;
                }

                var off = ((len & 0x3F) << 8) | data[ptr++];
                int saved = ptr, tmp = dnsBase + off;
                if (!TryReadQName(data, dnsBase, ref tmp, out var part))
                {
                    name = "";
                    return false;
                }

                sb.Append(part).Append('.');
                ptr = saved;
                name = sb.ToString(0, Math.Max(0, sb.Length - 1));
                return true;
            }

            if (ptr + len > data.Length)
            {
                break;
            }

            for (var i = 0; i < len; i++)
            {
                sb.Append((char)data[ptr + i]);
            }

            sb.Append('.');
            ptr += len;
        }

        name = "";
        return false;
    }

    private static ushort Read16(byte[] b, int off)
    {
        return (ushort)((b[off] << 8) | b[off + 1]);
    }

    private static void W16(List<byte> b, ushort v)
    {
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static void W32(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static void WName(List<byte> b, string fqdn)
    {
        if (string.IsNullOrEmpty(fqdn))
        {
            b.Add(0);
            return;
        }

        foreach (var label in fqdn.TrimEnd('.').Split('.'))
        {
            var bs = Encoding.ASCII.GetBytes(label);
            if (bs.Length > 63)
            {
                throw new ArgumentException("label too long");
            }

            b.Add((byte)bs.Length);
            b.AddRange(bs);
        }

        b.Add(0);
    }
}

internal static class WinDivertNative
{
    static WinDivertNative()
    {
        // Modify PATH var to include our WinDivert DLL's so that the LoadLibrary function will
        // find whatever WinDivert dll required for the current architecture.
        var path = new[]
        {
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
        };

        var dllSearchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win-x86", "native"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win-x64", "native"),
        };

        var newPath = string.Join(Path.PathSeparator.ToString(), path.Concat(dllSearchPaths));

        Environment.SetEnvironmentVariable("PATH", newPath);
    }

    // ===== Constants =====
    public const string Dll = "WinDivert.dll";
    public const int WINDIVERT_ADDRESS_SIZE = 64; // v2.x opaque address blob

    // ===== Enums =====
    public enum WINDIVERT_LAYER
    {
        Network = 0, // raw IP packets (in/out)
        NetworkForward = 1, // forwarded packets (router scenarios)
        Flow = 2,
        Socket = 3,
        Reflect = 4,
    }

    [Flags]
    public enum WINDIVERT_OPEN_FLAGS : ulong
    {
        None = 0,
        Sniff = 1UL << 0, // non-intrusive (doesn’t block/divert)
        Drop = 1UL << 1, // all matched packets dropped
        RecvOnly = 1UL << 2,
        SendOnly = 1UL << 3,
        NoInstall = 1UL << 4, // do not install service/driver
        Fragments = 1UL << 5, // receive fragments separately
        NoChecksum = 1UL << 6, // do not validate checksums on recv
        // (there are more flags in the SDK; add as you need)
    }

    public enum WINDIVERT_PARAM
    {
        QueueLen = 0, // UINT64
        QueueTime = 1, // UINT64 (microseconds)
        QueueSize = 2, // UINT64 (bytes)
        VersionMajor = 3, // UINT64
        VersionMinor = 4, // UINT64
        Timestamp = 5, // UINT64; 0=off, 1=on
    }

    [Flags]
    public enum WINDIVERT_HELPER_CHECKSUM_FLAGS : ulong
    {
        None = 0,
        Ip = 1UL << 0,
        Icmp = 1UL << 1,
        IcmpV6 = 1UL << 2,
        Tcp = 1UL << 3,
        Udp = 1UL << 4,
        All = Ip | Icmp | IcmpV6 | Tcp | Udp,
    }

    // ===== Safe handle =====
    public sealed class SafeWinDivertHandle : SafeHandle
    {
        private SafeWinDivertHandle() : base(IntPtr.Zero, true) { }
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle() => WinDivertClose(handle);
    }

    // ===== P/Invoke signatures (v2.x) =====

    // HANDLE WinDivertOpen(const char *filter, WINDIVERT_LAYER layer, INT16 priority, UINT64 flags);
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern SafeWinDivertHandle WinDivertOpen(
        string filter,
        WINDIVERT_LAYER layer,
        short priority,
        WINDIVERT_OPEN_FLAGS flags
    );

    // BOOL WinDivertClose(HANDLE handle);
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);

    // BOOL WinDivertRecv(HANDLE handle, PVOID pPacket, UINT packetLen, PWINDIVERT_ADDRESS pAddr, UINT *readLen);
    // Treat address as opaque 64-byte buffer.
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(
        SafeWinDivertHandle handle,
        [Out] byte[] packet,
        uint packetLen,
        out uint recvLen,
        [Out] byte[] address // size = WINDIVERT_ADDRESS_SIZE
    );

    // BOOL WinDivertSend(HANDLE handle, PVOID pPacket, UINT packetLen, PWINDIVERT_ADDRESS pAddr, UINT *writeLen);
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(
        SafeWinDivertHandle handle,
        [In] byte[] packet,
        uint packetLen,
        out uint sendLen,
        [In] byte[] address // same blob you got from Recv (possibly modified)
    );

    // BOOL WinDivertSetParam(HANDLE handle, WINDIVERT_PARAM param, UINT64 value);
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSetParam(
        SafeWinDivertHandle handle,
        WINDIVERT_PARAM param,
        ulong value
    );

    // BOOL WinDivertGetParam(HANDLE handle, WINDIVERT_PARAM param, UINT64 *value);
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertGetParam(
        SafeWinDivertHandle handle,
        WINDIVERT_PARAM param,
        out ulong value
    );

    // BOOL WinDivertHelperCalcChecksums(PVOID pPacket, UINT packetLen, PWINDIVERT_ADDRESS pAddr, UINT64 flags);
    // Pass address blob from Recv/Send if available; otherwise null. Flags can be 0 or All.
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums(
        [In] [Out] byte[] packet,
        uint packetLen,
        [In] byte[] address, // can be null; if you want, overload below
        WINDIVERT_HELPER_CHECKSUM_FLAGS flags
    );

    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, EntryPoint = "WinDivertHelperCalcChecksums", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums_NoAddr(
        [In] [Out] byte[] packet,
        uint packetLen,
        IntPtr address, // NULL
        WINDIVERT_HELPER_CHECKSUM_FLAGS flags
    );

    // Optional: parse helpers (if you want native parsing instead of PacketDotNet)
    // BOOL WinDivertHelperParsePacket(PVOID pPacket, UINT packetLen, PVOID *ppIpHdr, PVOID *ppIpv6Hdr,
    //                                 PVOID *ppIcmpHdr, PVOID *ppIcmpv6Hdr, PVOID *ppTcpHdr, PVOID *ppUdpHdr,
    //                                 PVOID *ppData, UINT *dataLen);
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperParsePacket(
        [In] byte[] packet, uint packetLen,
        out IntPtr ppIpHdr, out IntPtr ppIpv6Hdr,
        out byte protocol,
        out IntPtr ppIcmpHdr, out IntPtr ppIcmpv6Hdr,
        out IntPtr ppTcpHdr, out IntPtr ppUdpHdr,
        out IntPtr ppData, out uint dataLen,
        out IntPtr ppNext, out uint nextLen
    );
}