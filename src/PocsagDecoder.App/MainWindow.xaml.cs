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
        _tray = new TrayIcon(ShowFromTray, ToggleReceive, () => Vm.IsRunning, ExitApplication);
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText)) _tray.SetStatus(Vm.StatusText);
        };
        Vm.MessageMatched += (_, item) =>
        {
            if (IsVisible || !Vm.Settings.TrayNotifications) return;
            var head = $"RIC {item.Ric} {item.SubRic}" + (item.RicDescription.Length > 0 ? " – " + item.RicDescription : "");
            _tray.ShowBalloon(head, item.Text);
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && Vm.Settings.MinimizeToTray) Hide();
        };
        Loaded += (_, _) =>
        {
            Vm.StartIfConfigured();
            if (StartHidden || Vm.Settings.StartMinimized) Hide();
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private readonly TrayIcon _tray;
    private bool _exitRequested;
    private bool _hiddenHintShown;

    /// <summary>Per Kommandozeile "--minimized": direkt im Infobereich starten.</summary>
    public bool StartHidden { get; init; }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ToggleReceive()
    {
        var cmd = Vm.IsRunning ? Vm.StopCommand : Vm.StartCommand;
        if (cmd.CanExecute(null)) cmd.Execute(null);
    }

    /// <summary>Nächstes Schließen beendet das Programm wirklich (z.B. beim Herunterfahren).</summary>
    public void AllowExit() => _exitRequested = true;

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested && !_closingHandled && Vm.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_hiddenHintShown)
            {
                _hiddenHintShown = true;
                _tray.ShowBalloon("POCSAG Decoder läuft weiter",
                    "Der Empfang läuft im Hintergrund. Öffnen per Klick auf das Symbol, Beenden per Rechtsklick → Beenden.");
            }
            return;
        }
        if (_closingHandled) { _tray.Dispose(); return; }
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

    private void Placeholder_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: KeyValuePair<string, string> kv })
            Clipboard.SetText(kv.Key);
    }
}
