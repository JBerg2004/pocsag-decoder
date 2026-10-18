using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Redundancy;

namespace PocsagDecoder.App.ViewModels;

/// <summary>
/// Master/Slave-Betrieb mit zwei PCs, jeder mit eigenem Stick. Beide empfangen und protokollieren,
/// weitergeleitet wird nur vom Master. Der Slave fragt den Master alle paar Sekunden ab und übernimmt,
/// wenn der Master ausfällt; Alarme, die der Master verpasst hat, sendet er auf Wunsch selbst.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MissedCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SlaveLostAfter = TimeSpan.FromMinutes(1);

    private readonly object _seenLock = new();
    private readonly List<SeenMessage> _seen = new();
    private DateTime? _lastSeenAt;
    private MasterServer? _masterServer;
    private DispatcherTimer? _redundancyTimer;
    private FailoverMonitor _failover = new(TimeSpan.FromSeconds(20));
    private bool _polling;
    private string _slavePeer = string.Empty;
    private DateTime? _slaveSeenAt;
    private bool _slaveLostReported;
    private string _redundancyStatus = string.Empty;

    private void InitRedundancy()
    {
        TestRedundancyCommand = new RelayCommand(TestRedundancyAsync);
        _redundancyTimer = new DispatcherTimer { Interval = PollInterval };
        _redundancyTimer.Tick += async (_, _) => await RedundancyTickAsync();
        ApplyRedundancy();
    }

    public RelayCommand TestRedundancyCommand { get; private set; } = null!;

    public bool RedundancyOff { get => Mode == RedundancyModes.Off; set { if (value) SetMode(RedundancyModes.Off); } }
    public bool IsMaster { get => Mode == RedundancyModes.Master; set { if (value) SetMode(RedundancyModes.Master); } }
    public bool IsSlave { get => Mode == RedundancyModes.Slave; set { if (value) SetMode(RedundancyModes.Slave); } }

    private string Mode => Settings.RedundancyMode is RedundancyModes.Master or RedundancyModes.Slave ? Settings.RedundancyMode : RedundancyModes.Off;

    /// <summary>Slave, der gerade nicht selbst weiterleitet.</summary>
    private bool SlaveStandby => IsSlave && !_failover.Active;

    public int RedundancyPort
    {
        get => Settings.RedundancyPort;
        set
        {
            var port = Math.Clamp(value, 1, 65535);
            if (Settings.RedundancyPort == port) return;
            Settings.RedundancyPort = port;
            OnPropertyChanged();
            RequestSave();
            ApplyRedundancy();
        }
    }

    public string RedundancyMaster
    {
        get => Settings.RedundancyMaster;
        set
        {
            value = value?.Trim() ?? string.Empty;
            if (Settings.RedundancyMaster == value) return;
            Settings.RedundancyMaster = value;
            OnPropertyChanged();
            RequestSave();
            ApplyRedundancy();
        }
    }

    public string RedundancyKey
    {
        get => Settings.RedundancyKey;
        set
        {
            value ??= string.Empty;
            if (Settings.RedundancyKey == value) return;
            Settings.RedundancyKey = value;
            OnPropertyChanged();
            RequestSave();
            ApplyRedundancy();
        }
    }

    public int FailoverSeconds
    {
        get => Settings.FailoverSeconds;
        set
        {
            var seconds = Math.Clamp(value, 5, 600);
            if (Settings.FailoverSeconds == seconds) return;
            Settings.FailoverSeconds = seconds;
            _failover.FailoverAfter = TimeSpan.FromSeconds(seconds);
            OnPropertyChanged();
            RequestSave();
        }
    }

    public bool FailoverSendMissed
    {
        get => Settings.FailoverSendMissed;
        set { if (Settings.FailoverSendMissed == value) return; Settings.FailoverSendMissed = value; OnPropertyChanged(); RequestSave(); }
    }

    /// <summary>Ausführlicher Zustand für die Einstellungsseite.</summary>
    public string RedundancyStatus { get => _redundancyStatus; private set => Set(ref _redundancyStatus, value); }

    /// <summary>Kurzer Zustand für die Statusleiste.</summary>
    public string RedundancyBarText => Mode switch
    {
        RedundancyModes.Master => _slaveSeenAt is { } seen && DateTime.Now - seen < SlaveLostAfter
            ? "Master · Slave verbunden"
            : _slaveSeenAt is null ? "Master · wartet auf Slave" : "Master · Slave meldet sich nicht",
        RedundancyModes.Slave => _failover.Active ? "Slave · ÜBERNOMMEN" : _failover.LastContact is null ? "Slave · Master wird gesucht" : "Slave · Master OK",
        _ => string.Empty,
    };

    /// <summary>0 = aus, 1 = in Ordnung, 2 = Warnung, 3 = Slave hat übernommen.</summary>
    public int RedundancyLevel => Mode switch
    {
        RedundancyModes.Master => _slaveSeenAt is { } seen && DateTime.Now - seen < SlaveLostAfter ? 1 : 2,
        RedundancyModes.Slave => _failover.Active ? 3 : _failover.LastContact is null || _failover.Problem.Length > 0 ? 2 : 1,
        _ => 0,
    };

    public string RedundancyToolTip => RedundancyStatus;

    /// <summary>IPv4-Adressen dieses PCs, zum Eintragen am Slave.</summary>
    public string OwnAddresses
    {
        get
        {
            try
            {
                var addresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                    .Select(a => a.Address.ToString())
                    .Distinct();
                return $"Dieser PC: {Environment.MachineName} ({string.Join(", ", addresses)})";
            }
            catch (NetworkInformationException) { return $"Dieser PC: {Environment.MachineName}"; }
        }
    }

    private void SetMode(string mode)
    {
        if (Settings.RedundancyMode == mode) return;
        Settings.RedundancyMode = mode;
        OnPropertyChanged(nameof(RedundancyOff));
        OnPropertyChanged(nameof(IsMaster));
        OnPropertyChanged(nameof(IsSlave));
        RequestSave();
        ApplyRedundancy();
    }

    /// <summary>Server bzw. Abfrage passend zur Einstellung (neu) starten.</summary>
    private void ApplyRedundancy()
    {
        _masterServer?.Dispose();
        _masterServer = null;
        _redundancyTimer?.Stop();
        _failover = new FailoverMonitor(TimeSpan.FromSeconds(Settings.FailoverSeconds));
        _slaveSeenAt = null;
        _slavePeer = string.Empty;
        _slaveLostReported = false;

        if (IsMaster)
        {
            try
            {
                _masterServer = new MasterServer(Settings.RedundancyPort, Settings.RedundancyKey, BuildMasterStatus);
                _masterServer.SlaveSeen += (peer, time) => Application.Current?.Dispatcher.BeginInvoke(() => OnSlaveSeen(peer, time));
                _masterServer.Rejected += peer => Application.Current?.Dispatcher.BeginInvoke(() =>
                    AddLog($"Master/Slave: Anfrage von {peer} abgelehnt, Kennwort falsch."));
                _masterServer.Start();
                RedundancyStatus = $"Master: wartet auf Port {Settings.RedundancyPort} auf den Slave."
                    + (Settings.RedundancyKey.Length == 0 ? " Ohne Kennwort ist die Verbindung unverschlüsselt, über das Internet bitte unbedingt eins eintragen." : "");
                AddLog($"Master/Slave: Master gestartet, Port {Settings.RedundancyPort}.");
            }
            catch (SocketException ex)
            {
                _masterServer = null;
                RedundancyStatus = $"Master konnte Port {Settings.RedundancyPort} nicht öffnen: {ex.Message}";
                ReportError("redundancy", RedundancyStatus);
            }
            _redundancyTimer?.Start();
        }
        else if (IsSlave)
        {
            RedundancyStatus = Settings.RedundancyMaster.Length == 0
                ? "Slave: bitte Name oder IP-Adresse des Master-PCs eintragen. Bis dahin leitet dieser PC selbst weiter."
                : $"Slave: verbinde mit {Settings.RedundancyMaster}:{Settings.RedundancyPort} …";
            AddLog("Master/Slave: Slave gestartet" + (Settings.RedundancyMaster.Length > 0 ? $", Master {Settings.RedundancyMaster}." : "."));
            _redundancyTimer?.Start();
            _ = RedundancyTickAsync();
        }
        else
        {
            RedundancyStatus = "Aus: dieser PC leitet alle Treffer selbst weiter.";
        }
        RaiseRedundancyChanged();
    }

    private void RaiseRedundancyChanged()
    {
        OnPropertyChanged(nameof(RedundancyBarText));
        OnPropertyChanged(nameof(RedundancyLevel));
        OnPropertyChanged(nameof(RedundancyToolTip));
    }

    /// <summary>Wird vom Socket-Thread aufgerufen, daher nur gesperrt auf die Liste zugreifen.</summary>
    private MasterStatus BuildMasterStatus()
    {
        lock (_seenLock)
        {
            return new MasterStatus
            {
                Version = VersionText,
                Running = _isRunning,
                LastReceived = _lastSeenAt,
                Time = DateTime.Now,
                Recent = _seen.ToList(),
            };
        }
    }

    /// <summary>Jede empfangene Meldung: für den Master merken bzw. beim Slave für die Stille-Prüfung zählen.</summary>
    private void NoteRedundancy(PocsagMessage message)
    {
        if (message.Raw == "(Testnachricht)") return;
        var now = DateTime.Now;
        lock (_seenLock)
        {
            _lastSeenAt = now;
            _seen.Add(new SeenMessage(now, message.Ric, message.Function, message.Text));
            _seen.RemoveAll(m => now - m.Time > RedundancyProtocol.RecentWindow);
        }
        _failover.NoteLocalMessage(now);
    }

    private void OnSlaveSeen(string peer, DateTime time)
    {
        var wasMissing = _slaveSeenAt is null || _slaveLostReported;
        _slaveSeenAt = time;
        _slavePeer = peer;
        if (wasMissing) AddLog($"Master/Slave: Slave {peer} verbunden.");
        _slaveLostReported = false;
        RedundancyStatus = $"Master: Slave {peer} fragt regelmäßig nach (zuletzt {time:HH:mm:ss}).";
        RaiseRedundancyChanged();
    }

    private async Task RedundancyTickAsync()
    {
        if (IsMaster)
        {
            if (_slaveSeenAt is { } seen && DateTime.Now - seen > SlaveLostAfter && !_slaveLostReported)
            {
                _slaveLostReported = true;
                RedundancyStatus = $"Master: Slave {_slavePeer} meldet sich seit {seen:HH:mm:ss} nicht mehr.";
                ReportError("redundancy", $"Der Slave {_slavePeer} meldet sich nicht mehr. Ohne ihn gibt es keinen Ersatz, falls dieser PC ausfällt.");
            }
            RaiseRedundancyChanged();
            return;
        }
        if (!IsSlave || Settings.RedundancyMaster.Length == 0 || _polling) return;
        _polling = true;
        try
        {
            var (status, error) = await QueryMasterAsync();
            if (!IsSlave) return;
            var changed = _failover.Report(DateTime.Now, status, error);
            UpdateSlaveStatus(status);
            if (changed && _failover.Active)
                ReportError("failover", $"Master ausgefallen: {_failover.Problem}. Dieser PC (Slave) leitet jetzt selbst weiter.");
            else if (changed)
            {
                AddLog("Master/Slave: Master wieder in Ordnung, Slave gibt die Weiterleitung zurück.");
                if (Settings.ErrorTray) SystemError?.Invoke(this, ("POCSAG Decoder", "Master wieder in Ordnung, der Slave leitet nicht mehr weiter."));
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task<(MasterStatus? Status, string? Error)> QueryMasterAsync()
    {
        try
        {
            return (await RedundancyProtocol.QueryAsync(Settings.RedundancyMaster, Settings.RedundancyPort, Settings.RedundancyKey, TimeSpan.FromSeconds(4)), null);
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return (null, ex.Message);
        }
    }

    private void UpdateSlaveStatus(MasterStatus? status)
    {
        var master = $"{Settings.RedundancyMaster}:{Settings.RedundancyPort}";
        RedundancyStatus = _failover.Active
            ? $"Slave HAT ÜBERNOMMEN: {_failover.Problem}. Dieser PC leitet alle Treffer selbst weiter, bis der Master wieder läuft."
            : _failover.Problem.Length > 0
                ? $"Slave: {_failover.Problem}. Übernahme nach {Settings.FailoverSeconds} Sekunden ohne Besserung."
                : $"Slave: Master {master} ist in Ordnung ({status?.Version}, Empfang läuft"
                  + (status?.LastReceived is { } last ? $", zuletzt empfangen {last:HH:mm:ss}" : "") + $"). Geprüft {DateTime.Now:HH:mm:ss}.";
        RaiseRedundancyChanged();
    }

    private async Task TestRedundancyAsync()
    {
        if (IsMaster)
        {
            RedundancyStatus = _masterServer is null
                ? $"Master läuft nicht: Port {Settings.RedundancyPort} ist belegt oder gesperrt."
                : _slaveSeenAt is { } seen
                    ? $"Master läuft. Slave {_slavePeer} hat zuletzt um {seen:HH:mm:ss} nachgefragt."
                    : $"Master läuft auf Port {Settings.RedundancyPort}, bisher hat noch kein Slave nachgefragt. Am Slave diese Adresse eintragen: {OwnAddresses.Replace("Dieser PC: ", "")}";
            return;
        }
        if (!IsSlave) { RedundancyStatus = "Erst „Master“ oder „Slave“ auswählen."; return; }
        if (Settings.RedundancyMaster.Length == 0) { RedundancyStatus = "Bitte Name oder IP-Adresse des Master-PCs eintragen."; return; }
        RedundancyStatus = "Verbindung wird getestet …";
        var (status, error) = await QueryMasterAsync();
        RedundancyStatus = status is null
            ? $"Keine Verbindung zum Master: {error} Prüfen: Läuft das Programm dort als Master, stimmen Adresse und Port, lässt die Windows-Firewall am Master den Port zu?"
            : $"Verbindung in Ordnung: Master {Settings.RedundancyMaster} ({status.Version}), Empfang {(status.Running ? "läuft" : "gestoppt")}"
              + (status.LastReceived is { } last ? $", zuletzt empfangen {last:HH:mm:ss}." : ", noch nichts empfangen.");
    }

    // --- Weiterleitung am Slave -----------------------------------------------------

    /// <summary>
    /// Slave in Bereitschaft: nicht sofort senden. Mit „verpasste Alarme senden“ kurz warten und beim Master nachfragen,
    /// ob er die Meldung auch hat; sonst nur protokollieren. Liefert true, wenn die Meldung hier fertig behandelt ist.
    /// </summary>
    private bool SlaveHolds(MessageItem item, IReadOnlyList<FilterRule> hits)
    {
        if (!SlaveStandby || Settings.RedundancyMaster.Length == 0 || item.Message.Raw == "(Testnachricht)") return false;
        if (hits.All(r => r.TargetNames.Count == 0)) return false; // nur markieren, nichts zu senden
        if (!Settings.FailoverSendMissed)
        {
            item.AppendForwarding("Slave: nicht gesendet, der Master ist aktiv");
            WriteCsv(item);
            return true;
        }
        item.AppendForwarding("Slave: Abgleich mit dem Master …");
        _ = CheckWithMasterAsync(item, hits);
        return true;
    }

    private async Task CheckWithMasterAsync(MessageItem item, IReadOnlyList<FilterRule> hits)
    {
        await Task.Delay(MissedCheckDelay);
        var (status, error) = await QueryMasterAsync();
        var received = DateTime.Now;
        if (_failover.Report(received, status, error) && _failover.Active)
            ReportError("failover", $"Master ausgefallen: {_failover.Problem}. Dieser PC (Slave) leitet jetzt selbst weiter.");
        UpdateSlaveStatus(status);

        var m = item.Message;
        var waiting = "Slave: Abgleich mit dem Master …";
        if (status is not null && FailoverMonitor.MasterHas(status, received, m.Ric, m.Function, m.Text, m.Timestamp))
        {
            item.Forwarding = item.Forwarding.Replace(waiting, "Slave: der Master hat die Meldung, nicht gesendet");
            WriteCsv(item);
            return;
        }
        var reason = status is null ? "Master nicht erreichbar" : "Master hat die Meldung nicht empfangen";
        item.Forwarding = item.Forwarding.Replace(waiting, $"Slave: {reason}, sende selbst");
        AddLog($"Master/Slave: RIC {item.Ric}{item.SubRic} – {reason}, der Slave sendet selbst.");
        ForwardToTargets(item, hits);
    }
}
