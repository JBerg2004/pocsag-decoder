using PocsagDecoder.Core.Decoding;

namespace PocsagDecoder.Core.Redundancy;

/// <summary>
/// Entscheidet auf dem Slave, ob er die Weiterleitung übernehmen muss.
/// Der Master gilt als ausgefallen, wenn er nicht antwortet, sein Empfang gestoppt ist oder er seit
/// <see cref="SilenceWindow"/> nichts empfangen hat, während der Slave mehrere Meldungen bekommen hat.
/// Zurück an den Master geht es erst nach zwei gesunden Antworten hintereinander (kein Hin- und Herspringen).
/// </summary>
public sealed class FailoverMonitor
{
    public static readonly TimeSpan SilenceWindow = TimeSpan.FromMinutes(10);
    private const int SilenceMinMessages = 3;
    private const int HealthyToReturn = 2;

    private readonly Queue<DateTime> _local = new();
    private DateTime? _unhealthySince;
    private int _healthyInRow;

    public FailoverMonitor(TimeSpan failoverAfter) => FailoverAfter = failoverAfter;

    public TimeSpan FailoverAfter { get; set; }

    /// <summary>Slave leitet selbst weiter.</summary>
    public bool Active { get; private set; }

    /// <summary>Warum der Master als ausgefallen gilt (leer, wenn er in Ordnung ist).</summary>
    public string Problem { get; private set; } = string.Empty;

    public MasterStatus? LastStatus { get; private set; }
    public DateTime? LastContact { get; private set; }

    /// <summary>Eigene empfangene Meldung (für die Prüfung „Master empfängt nichts“).</summary>
    public void NoteLocalMessage(DateTime time)
    {
        _local.Enqueue(time);
        while (_local.Count > 0 && time - _local.Peek() > SilenceWindow) _local.Dequeue();
    }

    /// <summary>Ergebnis einer Anfrage an den Master auswerten. Liefert true, wenn sich <see cref="Active"/> geändert hat.</summary>
    public bool Report(DateTime now, MasterStatus? status, string? error = null)
    {
        if (status is not null)
        {
            LastStatus = status;
            LastContact = now;
        }
        var problem = Diagnose(now, status, error);
        Problem = problem ?? string.Empty;
        var before = Active;
        if (problem is null)
        {
            _unhealthySince = null;
            if (Active && ++_healthyInRow >= HealthyToReturn) Active = false;
        }
        else
        {
            _healthyInRow = 0;
            _unhealthySince ??= now;
            if (!Active && now - _unhealthySince.Value >= FailoverAfter) Active = true;
        }
        return Active != before;
    }

    private string? Diagnose(DateTime now, MasterStatus? status, string? error)
    {
        if (status is null) return "Master nicht erreichbar" + (string.IsNullOrEmpty(error) ? "" : $" ({error})");
        if (!status.Running) return "Empfang am Master gestoppt";
        while (_local.Count > 0 && now - _local.Peek() > SilenceWindow) _local.Dequeue();
        // Uhrzeiten beider PCs können abweichen: die Stille am Master an seiner eigenen Uhr messen.
        var masterSilent = status.LastReceived is null || status.Time - status.LastReceived.Value > SilenceWindow;
        if (masterSilent && _local.Count >= SilenceMinMessages)
            return $"Master hat seit {SilenceWindow.TotalMinutes:0} Minuten nichts empfangen, der Slave schon";
        return null;
    }

    /// <summary>
    /// Hat der Master diese Meldung auch empfangen? Gleiche RIC und SubRIC, Text gleich oder nur durch Bitfehler abweichend.
    /// Die Zeit wird großzügig verglichen, weil die Uhren der beiden PCs nicht genau gleich gehen müssen.
    /// </summary>
    /// <param name="statusReceived">Ortszeit des Slaves, als die Antwort ankam (für den Uhrenversatz).</param>
    public static bool MasterHas(MasterStatus status, DateTime statusReceived, int ric, int function, string text, DateTime localTime)
    {
        var offset = statusReceived - status.Time; // grober Uhrenversatz Slave − Master
        return status.Recent.Any(m =>
            m.Ric == ric && m.Function == function
            && Math.Abs((m.Time + offset - localTime).TotalSeconds) <= RedundancyProtocol.RecentWindow.TotalSeconds
            && (m.Text == text || (m.Text.Length > 0 && text.Length > 0 && ReceptionQuality.IsSimilar(m.Text, text))));
    }
}
