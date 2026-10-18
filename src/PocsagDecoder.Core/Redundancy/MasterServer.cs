using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PocsagDecoder.Core.Redundancy;

/// <summary>Master: beantwortet Anfragen des Slaves mit dem aktuellen Zustand.</summary>
public sealed class MasterServer : IDisposable
{
    private readonly Func<MasterStatus> _status;
    private readonly string _key;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Ein Slave hat nachgefragt (Rechnername, Zeitpunkt).</summary>
    public event Action<string, DateTime>? SlaveSeen;

    /// <summary>Eine Anfrage mit falschem Kennwort.</summary>
    public event Action<string>? Rejected;

    public MasterServer(int port, string key, Func<MasterStatus> status)
    {
        _status = status;
        _key = key ?? string.Empty;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception) when (_cts.IsCancellationRequested) { return; }
            catch (SocketException) { continue; }
            _ = Handle(client);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var request = RedundancyProtocol.ParseRequest(await reader.ReadLineAsync(timeout.Token));
                if (request is null) return;
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
                var peer = request.Value.Peer.Length > 0 ? $"{request.Value.Peer} ({remote})" : remote;
                MasterStatus answer;
                if (_key.Length > 0 && request.Value.Key != _key)
                {
                    answer = new MasterStatus { Error = "Kennwort stimmt nicht mit dem Master überein." };
                    Rejected?.Invoke(peer);
                }
                else
                {
                    answer = _status();
                    SlaveSeen?.Invoke(peer, DateTime.Now);
                }
                var bytes = Encoding.UTF8.GetBytes(RedundancyProtocol.Serialize(answer) + "\n");
                await stream.WriteAsync(bytes, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { /* Slave weg */ }
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        _listener.Stop();
    }
}
