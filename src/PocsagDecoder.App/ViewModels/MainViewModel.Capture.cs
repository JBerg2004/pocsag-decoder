using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

/// <summary>
/// Netzwerk-Mitschnitt: nimmt TCP-Verbindungen an (z. B. vom BosMon-Netzwerkereignis) und schreibt
/// alles Empfangene als Text und Hex ins Protokoll und in logs\mitschnitt.txt. Antwortet nichts.
/// </summary>
public sealed partial class MainViewModel
{
    private TcpListener? _capture;
    private CancellationTokenSource? _captureCts;
    private string _capturePort = "5566";

    public string CapturePort { get => _capturePort; set => Set(ref _capturePort, value); }
    public bool IsCapturing => _capture is not null;

    public RelayCommand ToggleCaptureCommand => _toggleCapture ??= new RelayCommand(ToggleCapture);
    private RelayCommand? _toggleCapture;

    private string CaptureFile => Path.Combine(_csv.Directory.Length > 0 ? _csv.Directory : CsvMessageLog.DefaultDirectory, "mitschnitt.txt");

    private void ToggleCapture()
    {
        if (_capture is not null)
        {
            _captureCts?.Cancel();
            _capture.Stop();
            _capture = null;
            AddLog("Mitschnitt beendet.");
            OnPropertyChanged(nameof(IsCapturing));
            return;
        }
        if (!int.TryParse(CapturePort, out var port) || port is < 1 or > 65535)
        {
            AddLog("Mitschnitt: ungültiger Port.");
            return;
        }
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _capture = listener;
            _captureCts = new CancellationTokenSource();
            _ = AcceptCaptureAsync(listener, _captureCts.Token);
            AddLog($"Mitschnitt lauscht auf Port {port}. Datei: {CaptureFile}");
            WriteCapture($"=== Mitschnitt gestartet {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Port {port}");
        }
        catch (SocketException ex)
        {
            AddLog($"Mitschnitt auf Port {port} nicht möglich: {ex.Message}");
        }
        OnPropertyChanged(nameof(IsCapturing));
    }

    private async Task AcceptCaptureAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = ReadCaptureAsync(client, ct);
        }
    }

    private async Task ReadCaptureAsync(TcpClient client, CancellationToken ct)
    {
        var from = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Note($"Verbindung von {from}");
        var buffer = new byte[8192];
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                while (!ct.IsCancellationRequested)
                {
                    var n = await stream.ReadAsync(buffer, ct);
                    if (n == 0) break;
                    var chunk = buffer.AsSpan(0, n).ToArray();
                    var text = Encoding.Latin1.GetString(chunk)
                        .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\0", "\\0");
                    Note($"{n} Bytes von {from}: {text}");
                    WriteCapture("HEX " + Convert.ToHexString(chunk));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        Note($"Verbindung von {from} geschlossen");

        void Note(string line)
        {
            WriteCapture($"{DateTime.Now:HH:mm:ss.fff} {line}");
            Application.Current.Dispatcher.BeginInvoke(() => AddLog("Mitschnitt: " + line));
        }
    }

    private readonly object _captureLock = new();

    private void WriteCapture(string line)
    {
        try
        {
            lock (_captureLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CaptureFile)!);
                File.AppendAllText(CaptureFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException) { }
    }
}
