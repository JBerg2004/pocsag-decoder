using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PocsagDecoder.App.ViewModels;

namespace PocsagDecoder.App;

public partial class MainWindow : Window
{
    private bool _closingHandled;

    public MainWindow()
    {
        InitializeComponent();
        Vm.RuleCreated += (_, _) => RulesTab.IsSelected = true;
        Vm.DescriptionRequested += (_, _) => DescriptionsTab.IsSelected = true;
        ((INotifyCollectionChanged)Vm.LogLines).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };
        Loaded += (_, _) =>
        {
            ApplyHiddenColumns();
            Vm.StartIfConfigured();
        };
        MessagesGrid.Loaded += (_, _) => AttachColumnHeaderMenu();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closingHandled) return;
        e.Cancel = true;
        _closingHandled = true;
        await Task.Yield(); // Close() darf nicht während OnClosing aufgerufen werden
        await Vm.ShutdownAsync();
        Close();
    }

    private void BrowseRtlFm_Click(object sender, RoutedEventArgs e)
    {
        if (Browse("rtl_fm.exe", Vm.Decoder.RtlFmPath) is { } path)
        {
            Vm.Decoder.RtlFmPath = path;
            RefreshBindings();
        }
    }

    private void BrowseMultimon_Click(object sender, RoutedEventArgs e)
    {
        if (Browse("multimon-ng.exe", Vm.Decoder.MultimonPath) is { } path)
        {
            Vm.Decoder.MultimonPath = path;
            RefreshBindings();
        }
    }

    private static string? Browse(string fileName, string current)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"{fileName} auswählen",
            Filter = $"{fileName}|{fileName}|Programme (*.exe)|*.exe",
            FileName = fileName,
        };
        try
        {
            var dir = System.IO.Path.GetDirectoryName(current);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }
        catch (ArgumentException) { }
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>DecoderOptions meldet keine Änderungen, daher Bindungen neu laden.</summary>
    private void RefreshBindings()
    {
        var dc = DataContext;
        DataContext = null;
        DataContext = dc;
    }

    private void ImportDescriptions_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "RIC-Beschreibungen importieren",
            Filter = "RIC-Listen (*.csv;*.txt;*.json;*.yaml;*.yml)|*.csv;*.txt;*.json;*.yaml;*.yml|Alle Dateien (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        var replace = false;
        if (Vm.Descriptions.Count > 0)
        {
            var answer = MessageBox.Show(
                $"Es sind bereits {Vm.Descriptions.Count} Beschreibungen vorhanden.\n\n" +
                "Ja = vorhandene Liste ersetzen\nNein = ergänzen (gleiche RIC/SubRIC wird aktualisiert)",
                "Import", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            replace = answer == MessageBoxResult.Yes;
        }
        Vm.ImportDescriptions(dlg.FileName, replace);
    }

    private void ExportDescriptions_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "RIC-Beschreibungen exportieren", Filter = "CSV (*.csv)|*.csv", FileName = "ric-beschreibungen.csv" };
        if (dlg.ShowDialog() == true) Vm.ExportDescriptions(dlg.FileName);
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Vorlage speichern", Filter = "CSV (*.csv)|*.csv", FileName = "ric-vorlage.csv" };
        if (dlg.ShowDialog() == true) MainViewModel.SaveDescriptionTemplate(dlg.FileName);
    }

    // --- Spaltenauswahl im Reiter Empfang ------------------------------------

    private static string ColumnName(DataGridColumn c) => c.Header?.ToString() ?? string.Empty;

    private void ApplyHiddenColumns()
    {
        foreach (var c in MessagesGrid.Columns)
            c.Visibility = Vm.Settings.HiddenColumns.Contains(ColumnName(c)) ? Visibility.Collapsed : Visibility.Visible;
    }

    private ContextMenu BuildColumnMenu()
    {
        var menu = new ContextMenu();
        foreach (var column in MessagesGrid.Columns)
        {
            var c = column;
            var item = new MenuItem
            {
                Header = ColumnName(c),
                IsCheckable = true,
                IsChecked = c.Visibility == Visibility.Visible,
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) =>
            {
                // Mindestens eine Spalte muss sichtbar bleiben.
                if (!item.IsChecked && MessagesGrid.Columns.Count(x => x.Visibility == Visibility.Visible) <= 1)
                {
                    item.IsChecked = true;
                    return;
                }
                c.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                Vm.SetHiddenColumns(MessagesGrid.Columns.Where(x => x.Visibility != Visibility.Visible).Select(ColumnName));
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = "Alle Spalten anzeigen" };
        all.Click += (_, _) =>
        {
            foreach (var c in MessagesGrid.Columns) c.Visibility = Visibility.Visible;
            Vm.SetHiddenColumns([]);
        };
        menu.Items.Add(all);
        return menu;
    }

    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildColumnMenu();
        menu.PlacementTarget = ColumnsButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Rechtsklick auf einen Spaltenkopf öffnet ebenfalls die Spaltenauswahl.</summary>
    private void AttachColumnHeaderMenu()
    {
        var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        style.Setters.Add(new EventSetter(MouseRightButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) =>
        {
            var menu = BuildColumnMenu();
            menu.PlacementTarget = s as UIElement;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        })));
        MessagesGrid.ColumnHeaderStyle = style;
    }

    // --- Allgemeine SubRIC-Zuordnung -----------------------------------------

    private void GlobalSubRics_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SubRicDialog(Vm.Settings.GlobalSubRics) { Owner = this };
        if (dlg.ShowDialog() == true) Vm.SetGlobalSubRics(dlg.Names);
    }

    private void RuleTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { Tag: string name } box && Vm.SelectedRule is { } rule)
            rule.SetTarget(name, box.IsChecked == true);
    }

    private void PickRics_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedRule is null) return;
        if (Vm.Descriptions.Count == 0)
        {
            MessageBox.Show(this, "Es sind noch keine RIC-Beschreibungen vorhanden. Bitte zuerst im Reiter „RIC-Beschreibungen“ eine Datei importieren.",
                "RICs auswählen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new RicPickerDialog(Vm.Descriptions, Vm.SelectedRuleSingleRics) { Owner = this };
        if (dlg.ShowDialog() == true) Vm.SetRuleRics(dlg.SelectedRics);
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedMessage is { } m)
            Clipboard.SetText($"{m.Timestamp:dd.MM.yyyy HH:mm:ss}\t{m.Ric}\t{m.SubRic}\t{m.Text}");
    }

    private void Placeholder_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: KeyValuePair<string, string> kv })
            Clipboard.SetText(kv.Key);
    }
}
