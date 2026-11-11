
using System.Net;
using System.Net.Sockets;
using Veyron.Core.Models;
using Veyron.Network.Configuration;

namespace Veyron.Network.Proxy;

public sealed class LocalProxyEndpoint : IDisposable
{
    private TcpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? acceptTask;

    public ProxyEndpointOptions Options { get; }

    public bool IsRunning => listener != null;

    public LocalProxyEndpoint(ProxyEndpointOptions? options = null)
    {
        Options = options ?? new ProxyEndpointOptions();
    }

    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsRunning)
            return Task.CompletedTask;

        if (!IPAddress.TryParse(Options.Host, out var address) ||
            !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException(
                "Local endpoint must bind to a loopback IP address.");
        }

        if (Options.Port < 1 || Options.Port > 65535)
            throw new ArgumentOutOfRangeException(
                nameof(Options.Port));

        var newListener = new TcpListener(address, Options.Port);

        newListener.Start();

        listener = newListener;
        cancellation = new CancellationTokenSource();

        acceptTask = AcceptLoopAsync(
            newListener,
            cancellation.Token);

        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(
        TcpListener tcpListener,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using TcpClient client = await tcpListener.AcceptTcpClientAsync(
                    cancellationToken);

                // No SOCKS5 protocol handling is implemented yet.
                // Close unsupported connections immediately.
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task StopAsync()
    {
        if (listener == null)
            return;

        cancellation?.Cancel();
        listener.Stop();

        if (acceptTask != null)
        {
            try
            {
                await acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        listener = null;

        cancellation?.Dispose();
        cancellation = null;

        acceptTask = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
