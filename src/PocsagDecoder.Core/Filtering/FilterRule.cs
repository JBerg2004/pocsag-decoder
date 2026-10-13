using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Filtering;

public enum TextMatchMode
{
    Contains,
    NotContains,
    Regex,
}

/// <summary>
/// Weiterleitungsregel: passt eine Nachricht auf RIC, SubRIC und Text,
/// wird sie an das gewählte Ziel gesendet.
/// </summary>
public sealed class FilterRule : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _name = "Neues Ereignis";
    private string _ricPattern = string.Empty;
    private bool _subRicA = true, _subRicB = true, _subRicC = true, _subRicD = true;
    private string _text = string.Empty;
    private TextMatchMode _textMode = TextMatchMode.Contains;
    private string _targetName = string.Empty;
    private string _descriptionText = string.Empty;
    private TextMatchMode _descriptionMode = TextMatchMode.Contains;
    private bool _stopProcessing;
    private bool _sendRepeats;

    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Wiederholungen derselben Nachricht trotzdem senden (Wiederholungs-Sperre gilt für diese Regel nicht).</summary>
    public bool SendRepeats { get => _sendRepeats; set => Set(ref _sendRepeats, value); }
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Siehe <see cref="RicPattern"/>.</summary>
    public string RicPattern { get => _ricPattern; set => Set(ref _ricPattern, value); }

    public bool SubRicA { get => _subRicA; set => Set(ref _subRicA, value); }
    public bool SubRicB { get => _subRicB; set => Set(ref _subRicB, value); }
    public bool SubRicC { get => _subRicC; set => Set(ref _subRicC, value); }
    public bool SubRicD { get => _subRicD; set => Set(ref _subRicD, value); }

    /// <summary>Text-Bedingung, leer = beliebig.</summary>
    public string Text { get => _text; set => Set(ref _text, value); }
    public TextMatchMode TextMode { get => _textMode; set => Set(ref _textMode, value); }

    /// <summary>
    /// Bedingung auf die Beschreibung (Bezeichnung der RIC und SubRIC-Bedeutung, z.B. "FF Musterstadt · Vollalarm"),
    /// leer = beliebig. Mehrere Begriffe mit | trennen (bei Contains/NotContains: einer davon).
    /// </summary>
    public string DescriptionText { get => _descriptionText; set => Set(ref _descriptionText, value); }
    public TextMatchMode DescriptionMode { get => _descriptionMode; set => Set(ref _descriptionMode, value); }

    /// <summary>Name des Webhook-Ziels; leer = nur markieren, nicht senden.</summary>
    public string TargetName { get => _targetName; set => Set(ref _targetName, value); }

    /// <summary>Alle Ziele der Regel; im Feld <see cref="TargetName"/> durch Komma getrennt.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> TargetNames => SplitTargets(TargetName);

    public static List<string> SplitTargets(string? text) =>
        (text ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Ziel hinzufügen oder entfernen, Reihenfolge bleibt erhalten.</summary>
    public void SetTarget(string name, bool on)
    {
        var list = SplitTargets(TargetName);
        list.RemoveAll(t => t.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (on) list.Add(name);
        TargetName = string.Join(", ", list);
    }

    /// <summary>Nach einem Treffer keine weiteren Regeln prüfen.</summary>
    public bool StopProcessing { get => _stopProcessing; set => Set(ref _stopProcessing, value); }

    public bool AcceptsFunction(int function) => function switch
    {
        0 => SubRicA,
        1 => SubRicB,
        2 => SubRicC,
        3 => SubRicD,
        _ => false,
    };

    [JsonIgnore]
    public string SubRicSummary
    {
        get
        {
            var s = string.Concat(
                SubRicA ? "A" : "", SubRicB ? "B" : "", SubRicC ? "C" : "", SubRicD ? "D" : "");
            return s.Length == 4 ? "alle" : s.Length == 0 ? "keine" : s;
        }
    }

    /// <summary>Prüft die Regel; liefert eine Fehlermeldung bei ungültiger Konfiguration.</summary>
    public string? Validate()
    {
        if (!Filtering.RicPattern.TryParse(RicPattern, out _, out var error)) return error;
        if (DescriptionMode == TextMatchMode.Regex && !string.IsNullOrEmpty(DescriptionText))
        {
            try { _ = new Regex(DescriptionText); }
            catch (ArgumentException ex) { return "Ungültiger regulärer Ausdruck (Beschreibung): " + ex.Message; }
        }
        if (TextMode == TextMatchMode.Regex && !string.IsNullOrEmpty(Text))
        {
            try { _ = new Regex(Text); }
            catch (ArgumentException ex) { return "Ungültiger regulärer Ausdruck: " + ex.Message; }
        }
        return null;
    }

    public bool IsMatch(PocsagMessage message) => Enabled && MatchesConditions(message);

    /// <summary>Prüft nur die Bedingungen (RIC, SubRIC, Text, Beschreibung), unabhängig davon, ob das Ereignis aktiv ist.</summary>
    public bool MatchesConditions(PocsagMessage message)
    {
        if (!AcceptsFunction(message.Function)) return false;
        if (!Filtering.RicPattern.TryParse(RicPattern, out var ric, out _) || !ric.IsMatch(message.Ric)) return false;

        var description = string.Join(" · ", new[] { message.RicDescription, message.RicShortName, message.RicLocation, message.SubRicDescription }.Where(x => x.Length > 0));
        return Matches(message.Text, Text, TextMode) && Matches(description, DescriptionText, DescriptionMode);
    }

    private static bool Matches(string input, string condition, TextMatchMode mode)
    {
        if (string.IsNullOrEmpty(condition)) return true;
        if (mode == TextMatchMode.Regex) return SafeRegex(input, condition);
        var any = condition.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(term => input.Contains(term, StringComparison.OrdinalIgnoreCase));
        return mode == TextMatchMode.Contains ? any : !any;
    }

    private static bool SafeRegex(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(SubRicA) or nameof(SubRicB) or nameof(SubRicC) or nameof(SubRicD))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubRicSummary)));
    }
}
