using System.Windows;

namespace PocsagDecoder.App;

public partial class SubRicDialog : Window
{
    public SubRicDialog(IReadOnlyList<string> current)
    {
        InitializeComponent();
        var boxes = new[] { BoxA, BoxB, BoxC, BoxD };
        for (var i = 0; i < 4; i++) boxes[i].Text = i < current.Count ? current[i] : string.Empty;
        Loaded += (_, _) => BoxA.Focus();
    }

    public string[] Names => [BoxA.Text.Trim(), BoxB.Text.Trim(), BoxC.Text.Trim(), BoxD.Text.Trim()];

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
