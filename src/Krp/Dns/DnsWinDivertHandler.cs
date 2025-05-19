using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinDivertSharp;

namespace Krp.Dns;

public class DnsWinDivertHandler : IDnsHandler
{
    private readonly ILogger<DnsWindowsHostsHandler> _logger;
    private const ushort Port80 = 80;
    private const ushort Port443 = 443;
    private const ushort LocalPort = 8080;

    private static readonly ConcurrentIpMap _ipMap = new(1024);
    private static readonly HashSet<string> _redirectDomains = new();

    public DnsWinDivertHandler(ILogger<DnsWindowsHostsHandler> logger)
    {
        _logger = logger;
    }

    public Task UpdateAsync(List<string> hostnames)
    {
        _redirectDomains.Clear();
        foreach (var hostname in hostnames)
        {
            _redirectDomains.Add(hostname);
        }

        using var cts = new CancellationTokenSource();

        var dnsThread = new Thread(() => CaptureDns(cts.Token)) { IsBackground = true };
        var tcpThread = new Thread(() => RedirectTcp(cts.Token)) { IsBackground = true };

        dnsThread.Start();
        tcpThread.Start();

        dnsThread.Join();
        tcpThread.Join();

        return Task.CompletedTask;
    }

    private static void CaptureDns(CancellationToken token)
    {
        const string dnsFilter = "udp";
        var dnsDivert = WinDivert.WinDivertOpen(dnsFilter, WinDivertLayer.Network, 0, WinDivertOpenFlags.None);

        try
        {
            var packet = new WinDivertBuffer(65535);
            var addr = new WinDivertAddress();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    uint readLen = 0;
                    if (!WinDivert.WinDivertRecv(dnsDivert, packet, ref addr, ref readLen))
                        continue;

                    var payload = new byte[readLen];
                    for (int i = 0; i < readLen; i++)
                        payload[i] = packet[i];

                    ParseDnsResponse(payload, 0);
                }
                //catch (WinDivertException ex) when (ex.NativeErrorCode == WinDivertErrorCodes.NoMoreItems)
                //{
                //    break;
                //}
                catch (Exception ex)
                {
                    Console.WriteLine($"DNS Capture Error: {ex.Message}");
                }
            }
        }
        finally
        {
            WinDivert.WinDivertClose(dnsDivert);
        }
    }

    private static void ParseDnsResponse(byte[] data, int offset)
    {
        if (data == null || data.Length - offset < 12)
            return;

        // Check if this is a DNS response (QR bit set in flags field)
        if ((data[offset + 2] & 0x80) == 0)
            return;

        // Read the DNS counts
        ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 4));
        ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 6));
        if (qdCount == 0 || anCount == 0)
            return;

        int ptr = offset + 12; // Start after DNS header.

        // Parse the DNS Queries (Questions Section)
        for (int i = 0; i < qdCount; i++)
        {
            if (!TryParseDomainName(data, ref ptr, out _, offset))
                return; // Failed to parse the question's domain name, exit.

            ptr += 4; // Skip QTYPE (2 bytes) and QCLASS (2 bytes).
        }

        // Parse the DNS Answer Section
        for (int i = 0; i < anCount; i++)
        {
            if (!TryParseDomainName(data, ref ptr, out string domain, offset))
                return;

            if (ptr + 10 > data.Length)
                return;

            ushort type = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(ptr));
            ptr += 8; // Move past TYPE, CLASS, and TTL (8 bytes total).
            ushort rdLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(ptr));
            ptr += 2; // Move past RDLENGTH field.

            if (type == 1 && rdLen == 4 && ptr + 4 <= data.Length) // Type A (IPv4)
            {
                if (_redirectDomains.Contains(domain))
                {
                    var ip = new IPAddress(data.AsSpan(ptr, 4));
                    _ipMap.TryAdd(ip);
                }
            }

            ptr += rdLen; // Skip the RDATA.
        }
    }

    private static void RedirectTcp(CancellationToken token)
    {
        const string tcpFilter = "outbound and (tcp.DstPort == 80 or tcp.DstPort == 443)";
        var tcpDivert = WinDivert.WinDivertOpen(tcpFilter, WinDivertLayer.Network, 0, WinDivertOpenFlags.None);

        try
        {
            var packet = new WinDivertBuffer(65535);
            var addr = new WinDivertAddress();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    unsafe
                    {
                        uint readLen = 0;
                        if (!WinDivert.WinDivertRecv(tcpDivert, packet, ref addr, ref readLen))
                        {
                            continue;
                        }

                        // Parse the IP header from the packet
                        var parseResult = WinDivert.WinDivertHelperParsePacket(packet, readLen);

                        // Check if this is an IPv4 packet with TCP
                        if (parseResult.IPv4Header != null && parseResult.TcpHeader != null)
                        {
                            // Get the destination address from IPv4 header
                            var ipHeader = *parseResult.IPv4Header;
                            var tcpHeader = *parseResult.TcpHeader;

                            if (_ipMap.Contains(ipHeader.DstAddr))
                            {
                                // Modify the destination address and port
                                ipHeader.DstAddr = IPAddress.Loopback;
                                tcpHeader.DstPort = LocalPort;


                                // Update the buffer (if needed)
                                WinDivert.WinDivertHelperCalcChecksums(packet, readLen, 0);

                                // Send the modified packet
                                WinDivert.WinDivertSend(tcpDivert, packet, readLen, ref addr);
                            }
                        }

                        // Forward as is.
                        WinDivert.WinDivertSend(tcpDivert, packet, readLen, ref addr);
                    }
                }
                //catch (WinDivertException ex) when (ex.NativeErrorCode == WinDivertErrorCodes.NoMoreItems)
                //{
                //    break;
                //}
                catch (Exception ex)
                {
                    Console.WriteLine($"TCP Redirect Error: {ex.Message}");
                }
            }
        }
        finally
        {
            WinDivert.WinDivertClose(tcpDivert);
        }
    }

    private static bool TryParseDomainName(byte[] data, ref int offset, out string domain, int startOffset)
    {
        var labelBuilder = new StringBuilder();
        int jumps = 0; // To prevent infinite loops if compression pointers are malformed.
        const int maxJumps = 5;

        while (offset < data.Length)
        {
            byte len = data[offset++];

            if (len == 0) // End of domain name
            {
                domain = labelBuilder.Length > 0 ? labelBuilder.ToString(0, labelBuilder.Length - 1) : string.Empty;
                return true;
            }

            if ((len & 0xC0) == 0xC0) // Compressed label
            {
                if (jumps++ > maxJumps) break; // Prevent infinite loop.

                if (offset >= data.Length)
                    break;

                int pointer = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset - 1)) & 0x3FFF;
                offset++; // Move past second compression byte.

                int savedOffset = offset;
                offset = pointer; // Jump to the location pointed to by the compressed pointer.

                bool parseResult = TryParseDomainName(data, ref offset, out string compressedLabel, startOffset);
                labelBuilder.Append(compressedLabel);

                offset = savedOffset; // Restore the offset after parsing the compressed pointer.
                domain = labelBuilder.ToString(0, labelBuilder.Length - 1);
                return parseResult;
            }

            // Regular label
            if (offset + len > data.Length)
                break;

            labelBuilder.Append(Encoding.ASCII.GetString(data, offset, len));
            labelBuilder.Append('.'); // Add dot for the next label.
            offset += len;
        }

        // If parsing failed, clear out values.
        domain = string.Empty;
        return false;
    }

}

public sealed class ConcurrentIpMap
{
    private readonly IPAddress[] _entries;
    private readonly int _mask;
    private int _pos;

    public ConcurrentIpMap(int capacity)
    {
        if ((capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a power of 2", nameof(capacity));

        _entries = new IPAddress[capacity];
        _mask = capacity - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TryAdd(IPAddress ip)
    {
        int index = Interlocked.Increment(ref _pos) & _mask;
        Interlocked.Exchange(ref _entries[index], ip);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(IPAddress ip)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            var entry = Volatile.Read(ref _entries[i]);
            if (entry is not null && entry.Equals(ip))
                return true;
        }

        return false;
    }
}
