using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PocsagDecoder.App.ViewModels;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.App;

/// <summary>Reiter „Schnittstellen“: Liste links, Editor in Abschnitten rechts.</summary>
public partial class TargetsView : UserControl
{
    private TextBox? _lastTextBox;

    public TargetsView()
    {
        InitializeComponent();
        // Telegrammereignisse können sich im anderen Reiter geändert haben.
        IsVisibleChanged += (_, _) => { if (IsVisible && DataContext is MainViewModel vm) vm.RefreshTargetUsage(); };
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    /// <summary>Sichtbar, wenn der Wert nicht null ist.</summary>
    public static IValueConverter NotNullToVis { get; } = new NotNullConverter();

    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewTargetDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.SelectedKind is { } kind) Vm.AddTarget(kind);
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.TestTargetCommand.CanExecute(null)) Vm.TestTargetCommand.Execute(null);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e) => Vm.DuplicateTarget();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedTarget is not { } t) return;
        var used = Vm.SelectedTargetUsedBy.StartsWith("Benutzt", StringComparison.Ordinal) ? "\n\n" + Vm.SelectedTargetUsedBy : "";
        if (MessageBox.Show(Window.GetWindow(this), $"Schnittstelle „{t.Name}“ löschen?{used}", "Schnittstelle löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var index = Vm.Targets.IndexOf(t);
        Vm.Targets.Remove(t);
        Vm.SelectedTarget = Vm.Targets.Count == 0 ? null : Vm.Targets[Math.Min(index, Vm.Targets.Count - 1)];
    }

    private void Editor_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBox tb) _lastTextBox = tb;
    }

    private void Placeholders_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var kv in Vm.Placeholders)
        {
            var item = new MenuItem { Header = $"{kv.Key}   –   {kv.Value}" };
            var key = kv.Key;
            item.Click += (_, _) => Insert(key);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Insert(string placeholder)
    {
        var tb = _lastTextBox;
        if (tb is null || !tb.IsVisible || !IsAncestorOf(tb))
        {
            Clipboard.SetText(placeholder);
            MessageBox.Show(Window.GetWindow(this), $"Erst in ein Textfeld klicken. {placeholder} wurde in die Zwischenablage kopiert.",
                "Platzhalter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var caret = tb.CaretIndex;
        tb.SelectedText = placeholder;
        tb.CaretIndex = caret + placeholder.Length;
        tb.SelectionLength = 0;
        tb.Focus();
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedTarget is not { } t) return;
        string filter = t.Kind switch
        {
            TargetKinds.Sound => "Ton-Dateien (*.wav;*.mp3)|*.wav;*.mp3|Alle Dateien|*.*",
            TargetKinds.Database => "SQLite-Datenbank (*.db;*.sqlite)|*.db;*.sqlite|Alle Dateien|*.*",
            _ => "Programme und Skripte (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|Alle Dateien|*.*",
        };
        FileDialog dlg = t.Kind == TargetKinds.Database
            ? new SaveFileDialog { Filter = filter, OverwritePrompt = false, FileName = "telegramme.db" }
            : new OpenFileDialog { Filter = filter };
        dlg.Title = t.FileLabel;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(t.FilePath));
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }
        catch (ArgumentException) { }
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) t.FilePath = dlg.FileName;
    }

    private sealed class NotNullConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
