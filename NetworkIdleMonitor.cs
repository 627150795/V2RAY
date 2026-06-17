using System.Net.NetworkInformation;

namespace V2rayNMonitor;

public sealed class NetworkIdleMonitor
{
    private long? _lastBytes;
    private DateTime _lastSample;
    private int _idleSamples;

    public bool IsIdle(MonitorSettings settings)
    {
        var now = DateTime.Now;
        var bytes = TotalNetworkBytes();
        if (_lastBytes is null || _lastSample == default)
        {
            _lastBytes = bytes;
            _lastSample = now;
            _idleSamples = 0;
            return false;
        }

        var seconds = Math.Max(1, (now - _lastSample).TotalSeconds);
        var kbps = Math.Max(0, bytes - _lastBytes.Value) / 1024d / seconds;
        _lastBytes = bytes;
        _lastSample = now;
        _idleSamples = kbps <= settings.IdleNetworkThresholdKBps ? _idleSamples + 1 : 0;
        return _idleSamples >= Math.Max(1, settings.IdleRequiredMinutes);
    }

    private static long TotalNetworkBytes()
    {
        long total = 0;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up) continue;
            if (network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var stats = network.GetIPv4Statistics();
            total += stats.BytesReceived + stats.BytesSent;
        }
        return total;
    }
}
