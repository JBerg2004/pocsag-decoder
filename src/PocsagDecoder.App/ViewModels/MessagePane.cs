using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Data;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Ein Fenster im Reiter Empfang mit eigenem Filter und eigener Spaltenauswahl.</summary>
public sealed class MessagePane : ObservableObject
{
    private readonly MainViewModel _main;
    private MessageItem? _selected;

    public MessagePane(MainViewModel main, ReceivePaneSettings settings)
    {
        _main = main;
        Settings = settings;
        View = new ListCollectionView(main.Messages) { Filter = o => o is MessageItem m && Matches(m) };
        ((INotifyCollectionChanged)main.Messages).CollectionChanged += (_, _) => OnPropertyChanged(nameof(VisibleCount));
        RemoveCommand = new RelayCommand(() => main.RemovePane(this), () => main.Panes.Count > 1);
    }

    public ReceivePaneSettings Settings { get; }
    public ListCollectionView View { get; }
    public RelayCommand RemoveCommand { get; }

    public MainViewModel Main => _main;
    public IReadOnlyList<string> SubRicChoices => _main.SubRicChoices;

    public string Title { get => Settings.Title; set => Change(() => Settings.Title = value ?? "", refresh: false); }
    public string Ric { get => Settings.Ric; set => Change(() => Settings.Ric = value ?? ""); }
    public string SubRic { get => Settings.SubRic; set => Change(() => Settings.SubRic = value ?? "Alle"); }
    public string Text { get => Settings.Text; set => Change(() => Settings.Text = value ?? ""); }
    public string Description { get => Settings.Description; set => Change(() => Settings.Description = value ?? ""); }
    public bool OnlyMatched { get => Settings.OnlyMatched; set => Change(() => Settings.OnlyMatched = value); }

    public int VisibleCount => View.Count;

    /// <summary>Ausgewählte Zeile; wird an das Hauptmodell weitergegeben (Kontextmenü).</summary>
    public MessageItem? SelectedItem
    {
        get => _selected;
        set { if (Set(ref _selected, value) && value is not null) _main.SelectedMessage = value; }
    }

    public IReadOnlyList<string> HiddenColumns => Settings.HiddenColumns;

    public void SetHiddenColumns(IEnumerable<string> headers)
    {
        Settings.HiddenColumns = headers.ToList();
        _main.RequestSave();
    }

    public void Refresh()
    {
        View.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
    }

    private void Change(Action apply, bool refresh = true, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        apply();
        OnPropertyChanged(name);
        if (refresh) Refresh();
        _main.RequestSave();
    }

    private bool Matches(MessageItem item)
    {
        if (OnlyMatched && !item.Matched) return false;
        if (SubRic is "A" or "B" or "C" or "D" && item.Message.SubRic != SubRic[0]) return false;
        if (!string.IsNullOrWhiteSpace(Text)
            && !item.Text.Contains(Text.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(Description))
        {
            // Mehrere Begriffe mit | = einer davon muss vorkommen.
            var hay = string.Join(" ", item.RicDescription, item.RicShortName, item.RicLocation, item.SubRicDescription);
            var terms = Description.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (terms.Length > 0 && !terms.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) return false;
        }
        if (!string.IsNullOrWhiteSpace(Ric))
        {
            // Teileingabe wie "12345" soll auch ohne * funktionieren.
            var pattern = Ric.Trim();
            if (!pattern.Contains('*') && !pattern.Contains('-') && !pattern.Contains(',') && !pattern.Contains('?'))
                return item.Ric.Contains(pattern) || item.Message.Ric.ToString().Contains(pattern);
            if (!RicPattern.TryParse(pattern, out var p, out _) || !p.IsMatch(item.Message.Ric)) return false;
        }
        return true;
    }
}
