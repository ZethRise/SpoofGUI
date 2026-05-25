using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SpoofGUI.Core;

internal static class NetStats
{
    public static uint CountEstablishedOnLocalPort(int port)
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var conns = props.GetActiveTcpConnections();
            uint count = 0;
            foreach (var c in conns)
            {
                if (c.LocalEndPoint.Port == port && c.State == TcpState.Established)
                    count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    public sealed class BandwidthSampler
    {
        private long _lastBytesSent;
        private long _lastBytesRecv;
        private DateTime _lastTick;
        private long _totalBytesSent;
        private long _totalBytesRecv;
        private long _baselineSent;
        private long _baselineRecv;
        private bool _hasBaseline;

        public long TotalBytesSent => _totalBytesSent;
        public long TotalBytesRecv => _totalBytesRecv;
        public double SendBps { get; private set; }
        public double RecvBps { get; private set; }

        public void Tick()
        {
            var (sent, recv) = ReadAllInterfaceCounters();
            var now = DateTime.UtcNow;

            if (!_hasBaseline)
            {
                _baselineSent = sent;
                _baselineRecv = recv;
                _lastBytesSent = sent;
                _lastBytesRecv = recv;
                _lastTick = now;
                _hasBaseline = true;
                SendBps = 0;
                RecvBps = 0;
                return;
            }

            var elapsed = (now - _lastTick).TotalSeconds;
            if (elapsed > 0.05)
            {
                SendBps = Math.Max(0, (sent - _lastBytesSent) / elapsed);
                RecvBps = Math.Max(0, (recv - _lastBytesRecv) / elapsed);
            }

            _lastBytesSent = sent;
            _lastBytesRecv = recv;
            _lastTick = now;
            _totalBytesSent = Math.Max(0, sent - _baselineSent);
            _totalBytesRecv = Math.Max(0, recv - _baselineRecv);
        }

        public void Reset()
        {
            _hasBaseline = false;
            _totalBytesSent = 0;
            _totalBytesRecv = 0;
            SendBps = 0;
            RecvBps = 0;
        }

        private static (long sent, long recv) ReadAllInterfaceCounters()
        {
            long sent = 0, recv = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var s = nic.GetIPStatistics();
                    sent += s.BytesSent;
                    recv += s.BytesReceived;
                }
                catch { }
            }
            return (sent, recv);
        }
    }

    public static string FormatBytes(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        if (bytes < KB) return $"{bytes} B";
        if (bytes < MB) return $"{bytes / KB:F1} KB";
        if (bytes < GB) return $"{bytes / MB:F1} MB";
        return $"{bytes / GB:F2} GB";
    }

    public static string FormatRate(double bps)
    {
        if (bps < 1024) return $"{bps:F0} B/s";
        if (bps < 1024 * 1024) return $"{bps / 1024:F1} KB/s";
        return $"{bps / (1024.0 * 1024):F2} MB/s";
    }
}
