using Microsoft.Extensions.Logging;
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
/// Spoofs DNS for selected hostnames using WFP/WinDivert.
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
public class DnsWinDivertHandler : IDnsHandler
{
    private readonly ILogger<DnsWinDivertHandler> _logger;
    private readonly ConcurrentDictionary<string, IPAddress> _redirectMap = new(StringComparer.OrdinalIgnoreCase);
    
    public DnsWinDivertHandler(ILogger<DnsWinDivertHandler> logger)
    {
        _logger = logger;
    }

    public Task UpdateAsync(List<string> hostnames)
    {
        _redirectMap.Clear();

        // Host names format: "127.0.0.1 myapp.local"
        foreach (var parts in hostnames.Select(line => line.Split(' ')))
        {
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var ip))
            {
                _redirectMap[parts[1]] = ip;
            }
        }

        _logger.LogInformation("Successfully updated DNS ({count} entries)", hostnames.Count);
        
        return Task.CompletedTask;
    }

    public Task RunAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => HandleQueries(stoppingToken), stoppingToken);
    }

    private void HandleQueries(CancellationToken ct)
    {
        const string filter = "outbound and ip and udp.DstPort == 53";
        using var handle = WinDivertOpen(filter, WINDIVERT_LAYER.Network, 0, WINDIVERT_OPEN_FLAGS.None);

        var buffer = new byte[4096];
        var address = new byte[WINDIVERT_ADDRESS_SIZE];
        var inboundAddr = new byte[WINDIVERT_ADDRESS_SIZE];

        while (!ct.IsCancellationRequested)
        {
            if (!WinDivertRecv(handle, buffer, (uint)buffer.Length, out var len, address))
            {
                continue;
            }

            int total = (int)len;
            if (total < 28)
            {
                WinDivertSend(handle, buffer, len, out _, address);
                continue;
            }

            var verIhl = buffer[0];
            if ((verIhl >> 4) != 4)
            {
                WinDivertSend(handle, buffer, len, out _, address);
                continue;
            }

            var ihl = (verIhl & 0x0F) * 4;
            if (total < ihl + 8 || buffer[9] != 17)
            {
                WinDivertSend(handle, buffer, len, out _, address);
                continue;
            }

            var udpOff = ihl;
            var srcPort = Read16(buffer, udpOff + 0);
            var dstPort = Read16(buffer, udpOff + 2);

            var dnsOff = udpOff + 8;
            var dnsLen = total - dnsOff;
            if (dnsLen < 12)
            {
                WinDivertSend(handle, buffer, len, out _, address);
                continue;
            }

            var pool = System.Buffers.ArrayPool<byte>.Shared;
            var dns = pool.Rent(dnsLen);

            try
            {
                Buffer.BlockCopy(buffer, dnsOff, dns, 0, dnsLen);

                if ((dns[2] & 0x80) != 0) // QR=1 => response
                {
                    WinDivertSend(handle, buffer, len, out _, address);
                    continue;
                }

                // Parse first A/AAAA question we care about.
                if (!TryGetWantedQuestion(dns, out var qName, out var qType, out var qClass))
                {
                    WinDivertSend(handle, buffer, len, out _, address);
                    continue;
                }

                // If endpoint exists with this host name.
                if (!_redirectMap.TryGetValue(qName, out var targetIp))
                {
                    WinDivertSend(handle, buffer, len, out _, address);
                    continue;
                }

                // Ignore DNS question is for an AAAA record (IPv6).
                if (qType == 28)
                {
                    WinDivertSend(handle, buffer, len, out _, address);
                    continue;
                }

                var txId = (ushort)((dns[0] << 8) | dns[1]);

                // Build DNS response into a pooled buffer to avoid extra allocations.
                var nameLen = GetQNameEncodedLength(qName);
                var respCap = 12 + nameLen + 4 + 32; // header + qname + qtype/qclass + possible answer
                var respBuf = pool.Rent(respCap);

                // Spoof DNS response with loopback IP.
                var respLen = BuildDnsResponse(respBuf.AsSpan(0, respCap), txId, qName, qType, qClass, targetIp, 30);

                var outLen = 20 + 8 + respLen;
                var outBuf = pool.Rent(outLen);
                try
                {
                    // IPv4 header
                    outBuf[0] = 0x45; // v4, ihl=5
                    outBuf[1] = 0;
                    outBuf[2] = (byte)(outLen >> 8);
                    outBuf[3] = (byte)(outLen);
                    outBuf[4] = 0; outBuf[5] = 0;
                    outBuf[6] = 0; outBuf[7] = 0;
                    outBuf[8] = 128;
                    outBuf[9] = 17; // UDP
                    outBuf[10] = 0; outBuf[11] = 0; // checksum later

                    // Src = original dst (bytes 16..19); Dst = original src (12..15)
                    outBuf[12] = buffer[16]; outBuf[13] = buffer[17]; outBuf[14] = buffer[18]; outBuf[15] = buffer[19];
                    outBuf[16] = buffer[12]; outBuf[17] = buffer[13]; outBuf[18] = buffer[14]; outBuf[19] = buffer[15];

                    // UDP header
                    var udpOut = 20;
                    outBuf[udpOut + 0] = (byte)(dstPort >> 8);
                    outBuf[udpOut + 1] = (byte)(dstPort);
                    outBuf[udpOut + 2] = (byte)(srcPort >> 8);
                    outBuf[udpOut + 3] = (byte)(srcPort);
                    var udpLen = 8 + respLen;
                    outBuf[udpOut + 4] = (byte)(udpLen >> 8);
                    outBuf[udpOut + 5] = (byte)(udpLen);
                    outBuf[udpOut + 6] = 0; outBuf[udpOut + 7] = 0;

                    // Payload
                    Buffer.BlockCopy(respBuf, 0, outBuf, udpOut + 8, respLen);
                    Buffer.BlockCopy(address, 0, inboundAddr, 0, WINDIVERT_ADDRESS_SIZE);

                    inboundAddr[0] = (byte)(inboundAddr[0] & ~0x01);

                    WinDivertHelperCalcChecksums_NoAddr(outBuf, (uint)outLen, IntPtr.Zero, WINDIVERT_HELPER_CHECKSUM_FLAGS.All);
                    WinDivertSend(handle, outBuf, (uint)outLen, out _, inboundAddr);
                    
                    _logger.LogTrace("Sent DNS response {ip} for {hostname} with TTL 30s", qName, targetIp);
                }
                finally
                {
                    pool.Return(outBuf);
                }
            }
            finally
            {
                pool.Return(dns);
            }
        }
    }

    private static int GetQNameEncodedLength(string fqdn)
    {
        if (string.IsNullOrEmpty(fqdn))
        {
            return 1; // just root label
        }

        var len = 1; // trailing zero

        foreach (var label in fqdn.TrimEnd('.').Split('.'))
        {
            len += 1 + label.Length;
        }

        return len;
    }

    private static int WriteQName(Span<byte> dst, string fqdn, int offset)
    {
        if (string.IsNullOrEmpty(fqdn)) { dst[offset++] = 0; return offset; }
        foreach (var label in fqdn.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            dst[offset++] = (byte)bytes.Length;
            bytes.CopyTo(dst.Slice(offset));
            offset += bytes.Length;
        }
        dst[offset++] = 0;
        return offset;
    }

    private static void W16(Span<byte> b, int offset, ushort v)
    {
        b[offset + 0] = (byte)(v >> 8);
        b[offset + 1] = (byte)v;
    }

    private static void W32(Span<byte> b, int offset, uint v)
    {
        b[offset + 0] = (byte)(v >> 24);
        b[offset + 1] = (byte)(v >> 16);
        b[offset + 2] = (byte)(v >> 8);
        b[offset + 3] = (byte)v;
    }

    private static int BuildDnsResponse(Span<byte> dst, ushort transactionId, string qName, ushort qType, ushort qClass, IPAddress ip, int ttlSeconds)
    {
        // Decide answer type from IP
        var answerType = (ushort)(ip.AddressFamily == AddressFamily.InterNetworkV6 ? 28 : 1);
        var typeMatches = qType == answerType;

        // Header
        W16(dst, 0, transactionId);
        ushort flags = 0x8000 /*QR*/ | 0x0100 /*RD*/ | 0x0080 /*RA*/;
        W16(dst, 2, flags);
        W16(dst, 4, 1); // QDCOUNT
        W16(dst, 6, (ushort)(typeMatches ? 1 : 0)); // ANCOUNT
        W16(dst, 8, 0); // NSCOUNT
        W16(dst, 10, 0); // ARCOUNT

        var pos = 12;
        pos = WriteQName(dst, qName, pos);
        W16(dst, pos, qType); pos += 2;
        W16(dst, pos, qClass == 0 ? (ushort)1 : qClass); pos += 2;

        if (typeMatches)
        {
            // Name pointer to 0x000C (start of QNAME)
            dst[pos++] = 0xC0; dst[pos++] = 0x0C;
            W16(dst, pos, answerType); pos += 2;
            W16(dst, pos, 1); pos += 2; // CLASS IN
            W32(dst, pos, (uint)ttlSeconds); pos += 4; // TTL
            var rdata = ip.GetAddressBytes();
            W16(dst, pos, (ushort)rdata.Length); pos += 2; // RDLENGTH
            rdata.CopyTo(dst.Slice(pos)); pos += rdata.Length;
        }

        return pos;
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
        [In][Out] byte[] packet,
        uint packetLen,
        [In] byte[] address, // can be null; if you want, overload below
        WINDIVERT_HELPER_CHECKSUM_FLAGS flags
    );

    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, EntryPoint = "WinDivertHelperCalcChecksums", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums_NoAddr(
        [In][Out] byte[] packet,
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
