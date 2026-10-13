using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PocsagDecoder.Core.Descriptions;

namespace PocsagDecoder.App;

public partial class RicPickerDialog : Window
{
    private readonly List<Row> _rows;
    private readonly ICollectionView _view;

    public RicPickerDialog(IEnumerable<RicEntry> entries, IReadOnlyCollection<int> selected)
    {
        InitializeComponent();
        _rows = entries.OrderBy(e => e.Ric)
            .Select(e => new Row(e.Ric, e.Name.Trim(), e.ShortName.Trim(), e.Location.Trim()) { IsChecked = selected.Contains(e.Ric) })
            .ToList();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is Row r && Matches(r);
        Grid.ItemsSource = _view;
        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public IEnumerable<int> SelectedRics => _rows.Where(r => r.IsChecked).Select(r => r.Ric);

    private bool Matches(Row r)
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        return q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || r.ShortName.Contains(w, StringComparison.OrdinalIgnoreCase) || r.Location.Contains(w, StringComparison.OrdinalIgnoreCase) || r.RicText.Contains(w));
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void SetVisible(bool value)
    {
        foreach (Row r in _view) r.IsChecked = value;
        UpdateCount();
    }

    private void AllVisible_Click(object sender, RoutedEventArgs e) => SetVisible(true);
    private void NoneVisible_Click(object sender, RoutedEventArgs e) => SetVisible(false);
    private void Check_Click(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount() => CountText.Text = $"{_rows.Count(r => r.IsChecked)} von {_rows.Count} RICs ausgewählt";

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public sealed class Row : INotifyPropertyChanged
    {
        private bool _isChecked;

        public Row(int ric, string name, string shortName, string location) { Ric = ric; Name = name; ShortName = shortName; Location = location; }

        public int Ric { get; }
        public string RicText => Ric.ToString("D7");
        public string Name { get; }
        public string ShortName { get; }
        public string Location { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
