using System.Windows;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.App;

/// <summary>Auswahl der Art beim Anlegen einer Schnittstelle, nach Gruppen sortiert.</summary>
public partial class NewTargetDialog : Window
{
    public NewTargetDialog()
    {
        InitializeComponent();
        Groups.ItemsSource = TargetKinds.All
            .GroupBy(TargetKinds.Category)
            .Select(g => new
            {
                Name = g.Key,
                Kinds = g.Select(k => new { Kind = k, KindIcon = TargetKinds.Icon(k), KindIconIsText = TargetKinds.IconIsText(k), Color = TargetKinds.Color(k), Short = TargetKinds.Short(k) }).ToList(),
            })
            .ToList();
    }

    public string? SelectedKind { get; private set; }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        SelectedKind = (string)((FrameworkElement)sender).Tag;
        DialogResult = true;
    }
}
