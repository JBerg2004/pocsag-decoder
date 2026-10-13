using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>TCP-Server, der jede Zeile an alle verbundenen Programme schickt.</summary>
public sealed class TcpBroadcaster : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly CancellationTokenSource _cts = new();

    public string Address { get; }
    public int Port { get; }

    public TcpBroadcaster(string address, int port)
    {
        Address = address;
        Port = port;
        var ip = string.IsNullOrWhiteSpace(address) ? IPAddress.Loopback : IPAddress.Parse(address.Trim());
        _listener = new TcpListener(ip, port);
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public int ClientCount { get { lock (_clients) return _clients.Count; } }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                lock (_clients) _clients.Add(client);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { /* weiter lauschen */ }
        }
    }

    /// <summary>Sendet eine Zeile an alle Verbindungen und liefert, wie viele sie bekommen haben.</summary>
    public async Task<int> BroadcastAsync(string line, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(line.Replace("\r", " ").Replace("\n", " ") + "\r\n");
        List<TcpClient> clients;
        lock (_clients) clients = _clients.ToList();
        var ok = 0;
        foreach (var c in clients)
        {
            try
            {
                await c.GetStream().WriteAsync(data, ct).ConfigureAwait(false);
                ok++;
            }
            catch
            {
                lock (_clients) _clients.Remove(c);
                c.Dispose();
            }
        }
        return ok;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        lock (_clients)
        {
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
        }
    }
}
