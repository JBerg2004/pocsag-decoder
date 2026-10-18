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
        _tray = new TrayIcon(ShowFromTray, StartReceive, () => Vm.IsRunning, ExitApplication);
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText)) _tray.SetStatus(Vm.StatusText);
        };
        Vm.SystemError += (_, e) => _tray.ShowBalloon(e.Title, e.Text, warning: true);
        Vm.MessageMatched += (_, item) =>
        {
            if (IsVisible || !Vm.Settings.TrayNotifications || !item.TrayNotify) return;
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

    private void StartReceive()
    {
        if (!Vm.IsRunning && Vm.StartCommand.CanExecute(null)) Vm.StartCommand.Execute(null);
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
            Filter = "RIC-Listen (*.csv;*.txt;*.json;*.yaml;*.yml;*.ini)|*.csv;*.txt;*.json;*.yaml;*.yml;*.ini|BosMon-Konfiguration (*.ini)|*.ini|Alle Dateien (*.*)|*.*",
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

    private void BrowseLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Ordner für die empfangenen Meldungen wählen", InitialDirectory = Vm.LogDirectory };
        if (dlg.ShowDialog(this) == true) Vm.LogDirectory = dlg.FolderName;
    }

    private void ResetLogDirectory_Click(object sender, RoutedEventArgs e) => Vm.ResetLogDirectory();

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Vorlage speichern", Filter = "CSV (*.csv)|*.csv", FileName = "ric-vorlage.csv" };
        if (dlg.ShowDialog() == true) MainViewModel.SaveDescriptionTemplate(dlg.FileName);
    }

    // --- Menü „Fenster“ im Reiter Telegramme -----------------------------------

    private void PaneMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildPaneMenu();
        menu.PlacementTarget = PaneMenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildPaneMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        var active = Vm.ActivePane;
        var index = active is null ? -1 : Vm.Panes.IndexOf(active);

        menu.Items.Add(Item("Hinzufügen", () => Vm.AddPaneCommand.Execute(null)));
        var arrange = new MenuItem { Header = "Autom. Anordnen" };
        foreach (var layout in Vm.PaneLayouts)
        {
            var l = layout;
            var item = Item(l, () => Vm.PaneLayout = l);
            item.IsChecked = Vm.PaneLayout == l;
            arrange.Items.Add(item);
        }
        menu.Items.Add(arrange);
        menu.Items.Add(Item("Konfigurieren…", () =>
        {
            if (Vm.ActivePane is { } p) new PaneConfigDialog(p) { Owner = this }.ShowDialog();
        }, active is not null));
        menu.Items.Add(Item("Nach vorne verschieben", () => Vm.MovePane(active!, index - 1), index > 0));
        menu.Items.Add(Item("Nach hinten verschieben", () => Vm.MovePane(active!, index + 1), index >= 0 && index < Vm.Panes.Count - 1));
        menu.Items.Add(Item("Leeren", () => active?.Clear(), active is not null));
        menu.Items.Add(Item("Löschen", () =>
        {
            if (active is null) return;
            if (MessageBox.Show(this, $"Fenster „{active.Title}“ löschen?", "Fenster löschen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Vm.RemovePane(active);
        }, active is not null && Vm.Panes.Count > 1));
        menu.Items.Add(new Separator());
        for (var i = 0; i < Vm.Panes.Count; i++)
        {
            var p = Vm.Panes[i];
            var item = Item($"{i + 1} {p.Title}", () => Vm.ActivePane = p);
            item.IsChecked = p == active;
            menu.Items.Add(item);
        }
        return menu;
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
}
