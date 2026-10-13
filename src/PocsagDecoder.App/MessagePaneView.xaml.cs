using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PocsagDecoder.App.ViewModels;

namespace PocsagDecoder.App;

/// <summary>Ein Empfangsfenster: eigener Filter, eigene Spaltenauswahl.</summary>
public partial class MessagePaneView : UserControl
{
    public MessagePaneView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MessagePane old) old.ColumnsChanged -= OnColumnsChanged;
            if (e.NewValue is MessagePane pane) pane.ColumnsChanged += OnColumnsChanged;
            ApplyHiddenColumns();
        };
        Grid.Loaded += (_, _) => AttachColumnHeaderMenu();
        // Klick irgendwo ins Fenster wählt es aus.
        PreviewMouseDown += (_, _) => { if (Pane is { } p) p.Main.ActivePane = p; };
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => ApplyHiddenColumns();

    // --- Titelleiste: Doppelklick konfiguriert, Ziehen vertauscht -------------

    private Point _dragStart;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        if (e.ClickCount == 2) OpenConfiguration();
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Pane is null) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(TitleBar, new DataObject(typeof(MessagePane), Pane), DragDropEffects.Move);
    }

    private void Pane_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(typeof(MessagePane)) is MessagePane src && src != Pane ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Pane_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(MessagePane)) is not MessagePane src || Pane is not { } target || src == target) return;
        target.Main.MovePane(src, target.Main.Panes.IndexOf(target));
        target.Main.ActivePane = src;
    }

    private void Configure_Click(object sender, RoutedEventArgs e) => OpenConfiguration();

    public void OpenConfiguration()
    {
        if (Pane is null) return;
        new PaneConfigDialog(Pane) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private MessagePane? Pane => DataContext as MessagePane;

    private static string ColumnName(DataGridColumn c) => c.Header?.ToString() ?? string.Empty;

    private void ApplyHiddenColumns()
    {
        if (Pane is not { } pane) return;
        foreach (var c in Grid.Columns)
            c.Visibility = pane.HiddenColumns.Contains(ColumnName(c)) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveHiddenColumns() =>
        Pane?.SetHiddenColumns(Grid.Columns.Where(x => x.Visibility != Visibility.Visible).Select(ColumnName));

    private ContextMenu BuildColumnMenu()
    {
        var menu = new ContextMenu();
        foreach (var column in Grid.Columns)
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
                if (!item.IsChecked && Grid.Columns.Count(x => x.Visibility == Visibility.Visible) <= 1)
                {
                    item.IsChecked = true;
                    return;
                }
                c.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                SaveHiddenColumns();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = "Alle Spalten anzeigen" };
        all.Click += (_, _) =>
        {
            foreach (var c in Grid.Columns) c.Visibility = Visibility.Visible;
            SaveHiddenColumns();
        };
        menu.Items.Add(all);
        return menu;
    }

    /// <summary>Rechtsklick auf einen Spaltenkopf öffnet ebenfalls die Spaltenauswahl.</summary>
    private void AttachColumnHeaderMenu()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new EventSetter(MouseRightButtonUpEvent, new MouseButtonEventHandler((s, e) =>
        {
            var menu = BuildColumnMenu();
            menu.PlacementTarget = s as UIElement;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        })));
        Grid.ColumnHeaderStyle = style;
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (Pane?.SelectedItem is { } m)
            Clipboard.SetText($"{m.Timestamp:dd.MM.yyyy HH:mm:ss}\t{m.Ric}\t{m.SubRic}\t{m.Text}");
    }
}
