using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Decoding;

/// <summary>
/// Schätzt die Empfangsqualität aus den Wiederholungen: BOS-Alarme werden meist zweimal gesendet.
/// Kommt die Wiederholung mit genau demselben Text an, war der Empfang fehlerfrei; weicht sie nur
/// in einzelnen Zeichen ab, hat mindestens eine der beiden Aussendungen Bitfehler.
/// Verschlüsselte oder binäre Texte funktionieren dabei genauso, sie müssen nur gleich ankommen.
/// </summary>
public sealed class ReceptionQuality
{
    public const int WindowSize = 50;
    private const int MinimumSamples = 5;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(90);

    private readonly Queue<bool> _recent = new();
    private readonly Dictionary<(int Ric, int Function), (DateTime Time, string Text)> _last = new();

    /// <summary>Ausgewertete Wiederholungen (höchstens <see cref="WindowSize"/>).</summary>
    public int Samples => _recent.Count;

    /// <summary>Wiederholungen mit abweichendem Text im Fenster.</summary>
    public int Faulty => _recent.Count(f => f);

    /// <summary>Anteil fehlerfreier Wiederholungen 0–100, null solange zu wenige ausgewertet sind.</summary>
    public int? Percent => Samples < MinimumSamples ? null : (int)Math.Round(100.0 * (Samples - Faulty) / Samples);

    /// <summary>0 = unbekannt, 1 = schlecht, 2 = mittel, 3 = gut</summary>
    public int Level => Percent switch
    {
        null => 0,
        >= 95 => 3,
        >= 80 => 2,
        _ => 1,
    };

    public string LevelText => Level switch
    {
        3 => "gut",
        2 => "mittel",
        1 => "schlecht",
        _ => "noch zu wenige Wiederholungen",
    };

    /// <summary>Meldung bewerten. Reine Ton-Alarme haben keinen Text zum Vergleichen und werden übersprungen.</summary>
    public void Add(PocsagMessage message)
    {
        if (message.ContentType is not (PocsagContentType.Alpha or PocsagContentType.Numeric) || message.Text.Length == 0) return;
        var key = (message.Ric, message.Function);
        if (_last.TryGetValue(key, out var previous) && message.Timestamp - previous.Time <= RepeatWindow)
        {
            if (previous.Text == message.Text) Record(faulty: false);
            // Ähnlicher Text = dieselbe Meldung mit Bitfehlern. Ganz anderer Text = neue Meldung, zählt nicht.
            else if (IsSimilar(previous.Text, message.Text)) Record(faulty: true);
        }
        _last[key] = (message.Timestamp, message.Text);
        if (_last.Count > 2000)
            foreach (var old in _last.Where(kv => message.Timestamp - kv.Value.Time > RepeatWindow).Select(kv => kv.Key).ToList())
                _last.Remove(old);
    }

    public void Reset()
    {
        _recent.Clear();
        _last.Clear();
    }

    private void Record(bool faulty)
    {
        _recent.Enqueue(faulty);
        while (_recent.Count > WindowSize) _recent.Dequeue();
    }

    /// <summary>Höchstens 40 % der Zeichen unterschiedlich (Levenshtein-Abstand).</summary>
    public static bool IsSimilar(string a, string b)
    {
        var longer = Math.Max(a.Length, b.Length);
        if (longer == 0) return true;
        if (Math.Abs(a.Length - b.Length) > longer * 0.4) return false;
        return Distance(a, b) <= longer * 0.4;
    }

    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
