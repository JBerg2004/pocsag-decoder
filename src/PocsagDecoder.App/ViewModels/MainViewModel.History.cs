using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using PocsagDecoder.Core.Descriptions;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Reiter Verlauf: Suche über alle CSV-Tagesdateien und Export als Excel-Datei.</summary>
public sealed partial class MainViewModel
{
    private const int MaxHistoryResults = 20000;
    private string _historyFrom = DateTime.Today.AddDays(-7).ToString("dd.MM.yyyy");
    private string _historyTo = DateTime.Today.ToString("dd.MM.yyyy");
    private string _historySearch = string.Empty;
    private string _historySubRic = "Alle";
    private bool _historyOnlyForwarded;
    private string _historyStatus = "Zeitraum wählen und auf „Suchen“ klicken.";

    public ObservableCollection<MessageItem> HistoryResults { get; } = new();

    public IReadOnlyList<string> HistorySubRicChoices { get; } = ["Alle", "A", "B", "C", "D"];

    public string HistoryFrom { get => _historyFrom; set => Set(ref _historyFrom, value); }
    public string HistoryTo { get => _historyTo; set => Set(ref _historyTo, value); }

    /// <summary>Begriffe mit Leerzeichen getrennt; alle müssen vorkommen (RIC, Bezeichnung, Ort, Kurz, SubRIC-Bedeutung, Text, Weiterleitung).</summary>
    public string HistorySearch { get => _historySearch; set => Set(ref _historySearch, value); }

    public string HistorySubRic { get => _historySubRic; set => Set(ref _historySubRic, value); }

    /// <summary>Nur Meldungen, die an eine Schnittstelle gesendet wurden.</summary>
    public bool HistoryOnlyForwarded { get => _historyOnlyForwarded; set => Set(ref _historyOnlyForwarded, value); }

    public string HistoryStatus { get => _historyStatus; private set => Set(ref _historyStatus, value); }

    public RelayCommand SearchHistoryCommand { get; private set; } = null!;
    public RelayCommand HistoryRangeCommand { get; private set; } = null!;

    private void InitHistory()
    {
        SearchHistoryCommand = new RelayCommand(SearchHistoryAsync);
        // Parameter: Anzahl Tage zurück, 0 = nur heute, -1 = alles
        HistoryRangeCommand = new RelayCommand(p =>
        {
            var days = int.TryParse(p?.ToString(), out var d) ? d : 7;
            HistoryFrom = days < 0 ? "01.01.2000" : DateTime.Today.AddDays(-days).ToString("dd.MM.yyyy");
            HistoryTo = DateTime.Today.ToString("dd.MM.yyyy");
            _ = SearchHistoryAsync();
        });
    }

    private static bool TryParseDay(string text, out DateTime day) =>
        DateTime.TryParseExact(text?.Trim(), ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy"], CultureInfo.GetCultureInfo("de-DE"),
            DateTimeStyles.None, out day);

    private async Task SearchHistoryAsync()
    {
        if (!TryParseDay(HistoryFrom, out var from) || !TryParseDay(HistoryTo, out var to))
        {
            HistoryStatus = "Datum bitte als TT.MM.JJJJ eingeben, z. B. 01.10.2026.";
            return;
        }
        if (to < from) (from, to) = (to, from);
        var terms = (HistorySearch ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var function = HistorySubRic is { Length: 1 } s ? "ABCD".IndexOf(char.ToUpperInvariant(s[0])) : -1;
        var onlyForwarded = HistoryOnlyForwarded;
        var directory = _csv.Directory;
        HistoryStatus = "Suche läuft …";

        // Lesen im Hintergrund, Beschreibungen danach im UI-Thread aus dem aktuellen RIC-Verzeichnis ergänzen.
        List<HistoryEntry> found;
        int total;
        try
        {
            (found, total) = await Task.Run(() =>
            {
                var all = MessageHistory.Read(directory, from, to)
                    .Where(e => function < 0 || e.Message.Function == function)
                    .Where(e => !onlyForwarded || IsForwarded(e.Forwarding))
                    .ToList();
                return (all, all.Count);
            });
        }
        catch (Exception ex)
        {
            HistoryStatus = "Verlauf konnte nicht gelesen werden: " + ex.Message;
            return;
        }

        var items = new List<MessageItem>();
        foreach (var entry in found)
        {
            _directory.Annotate(entry.Message);
            var item = new MessageItem(entry.Message) { Forwarding = entry.Forwarding };
            if (terms.All(t => Matches(item, t))) items.Add(item);
        }
        items.Reverse(); // neueste zuerst
        HistoryResults.Clear();
        foreach (var item in items.Take(MaxHistoryResults)) HistoryResults.Add(item);

        var range = from == to ? $"am {from:dd.MM.yyyy}" : $"vom {from:dd.MM.yyyy} bis {to:dd.MM.yyyy}";
        HistoryStatus = items.Count switch
        {
            0 => $"Keine Meldungen gefunden ({range}).",
            > MaxHistoryResults => $"{items.Count:N0} Treffer {range}, angezeigt werden die neuesten {MaxHistoryResults:N0}. Bitte Zeitraum oder Suche eingrenzen.",
            _ => $"{items.Count:N0} Treffer {range}" + (terms.Length > 0 || function >= 0 || onlyForwarded ? $" (von {total:N0} Meldungen)." : "."),
        };
    }

    private static bool IsForwarded(string forwarding) =>
        forwarding.Length > 0 && !forwarding.StartsWith("Wiederholung", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(MessageItem m, string term) =>
        new[] { m.Ric, m.RicDescription, m.RicShortName, m.RicLocation, m.SubRicDescription, m.Text, m.Forwarding }
            .Any(f => f?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Aktuelle Trefferliste als Excel-Datei speichern.</summary>
    public void ExportHistory(string path)
    {
        try
        {
            XlsxWriter.Write(path, "Verlauf",
                ["Zeit", "RIC", "SubRIC", "Bezeichnung", "Kurz", "Ort", "SubRIC-Bedeutung", "Text", "Weiterleitung"],
                HistoryResults.Select(m => (IReadOnlyList<object?>)new object?[]
                {
                    m.Timestamp, m.Ric, m.SubRic, m.RicDescription, m.RicShortName, m.RicLocation, m.SubRicDescription, m.Text, m.Forwarding,
                }),
                [19, 10, 8, 30, 14, 20, 20, 50, 40]);
            HistoryStatus = $"{HistoryResults.Count:N0} Zeilen gespeichert: {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow, "Die Datei konnte nicht gespeichert werden:\n" + ex.Message, "Als Excel speichern",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
