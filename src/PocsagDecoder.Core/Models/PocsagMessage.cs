namespace PocsagDecoder.Core.Models;

/// <summary>Art des POCSAG-Inhalts, wie von multimon-ng gemeldet.</summary>
public enum PocsagContentType
{
    /// <summary>Nur Ton (keine Nutzdaten).</summary>
    Tone,
    Numeric,
    Alpha,
    Skyper,
}

/// <summary>Eine dekodierte POCSAG-Nachricht.</summary>
public sealed class PocsagMessage
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Baudrate (512, 1200 oder 2400).</summary>
    public int Baud { get; init; }

    /// <summary>RIC (Radio Identity Code), 21 Bit Adresse.</summary>
    public int Ric { get; init; }

    /// <summary>Funktionsbits 0..3 aus dem Adresscodewort.</summary>
    public int Function { get; init; }

    /// <summary>SubRIC als Buchstabe A..D (Funktion 0..3).</summary>
    public char SubRic => SubRicFromFunction(Function);

    /// <summary>SubRIC als Zahl 1..4.</summary>
    public int SubRicNumber => Function + 1;

    public PocsagContentType ContentType { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>Beschreibung aus dem RIC-Verzeichnis (RIC und/oder SubRIC), leer wenn keine hinterlegt.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Beschreibung der RIC allein.</summary>
    public string RicDescription { get; set; } = string.Empty;

    /// <summary>Ort der RIC.</summary>
    public string RicLocation { get; set; } = string.Empty;

    /// <summary>Kurze Bezeichnung der RIC.</summary>
    public string RicShortName { get; set; } = string.Empty;

    /// <summary>BosMon-Benutzer der RIC (aus den RIC-Beschreibungen).</summary>
    public string RicBosMonUser { get; set; } = string.Empty;

    /// <summary>Beschreibung der SubRIC allein.</summary>
    public string SubRicDescription { get; set; } = string.Empty;

    /// <summary>Kurztext der SubRIC (z. B. „Alarm“ statt „Einsatz“), sonst wie <see cref="SubRicDescription"/>.</summary>
    public string SubRicShortDescription { get; set; } = string.Empty;

    /// <summary>Unveränderte Ausgabezeile des Decoders.</summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>RIC 7-stellig mit führenden Nullen, wie im BOS-Funk üblich.</summary>
    public string RicFormatted => Ric.ToString("D7");

    public static char SubRicFromFunction(int function) => (char)('A' + Math.Clamp(function, 0, 3));

    /// <summary>Wandelt "A".."D", "a".."d" oder "0".."3" bzw. "1".."4" (mit Präfix '#') in Funktionsbits um.</summary>
    public static bool TryParseSubRic(string? value, out int function)
    {
        function = -1;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.Length == 1)
        {
            var c = char.ToUpperInvariant(value[0]);
            if (c is >= 'A' and <= 'D') { function = c - 'A'; return true; }
            if (c is >= '0' and <= '3') { function = c - '0'; return true; }
        }
        return false;
    }

    public override string ToString() =>
        $"{Timestamp:HH:mm:ss} POCSAG{Baud} RIC {RicFormatted} {SubRic} ({ContentType}) {Text}";
}
