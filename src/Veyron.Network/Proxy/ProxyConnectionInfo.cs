
using System;
using System.Threading;

namespace Veyron.Network.Proxy;

public sealed class ProxyConnectionInfo
{
    private long bytesSent;
    private long bytesReceived;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Destination { get; init; } = "";

    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    public long BytesSent
    {
        get => Interlocked.Read(ref bytesSent);
        internal set => Interlocked.Exchange(ref bytesSent, value);
    }

    public long BytesReceived
    {
        get => Interlocked.Read(ref bytesReceived);
        internal set => Interlocked.Exchange(ref bytesReceived, value);
    }

    public string Status { get; internal set; } = "Connecting";

    public TimeSpan Duration =>
        DateTime.UtcNow - StartedAt;
}
