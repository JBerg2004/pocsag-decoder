using System.Windows;
using PocsagDecoder.App.ViewModels;

namespace PocsagDecoder.App;

/// <summary>Name, Filter und Spalten eines Empfangsfensters.</summary>
public partial class PaneConfigDialog : Window
{
    public PaneConfigDialog(MessagePane pane)
    {
        InitializeComponent();
        DataContext = pane;
        ColumnList.ItemsSource = MessagePane.ColumnNames.Select(n => new ColumnOption(pane, n)).ToList();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class ColumnOption(MessagePane pane, string name) : ObservableObject
    {
        public string Name { get; } = name;

        public bool Visible
        {
            get => pane.IsColumnVisible(Name);
            set
            {
                pane.SetColumnVisible(Name, value);
                OnPropertyChanged(); // falls die letzte sichtbare Spalte nicht abgewählt werden durfte
            }
        }
    }
}
