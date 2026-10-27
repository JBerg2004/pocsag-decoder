using System.Windows;

namespace PocsagDecoder.App;

public partial class SubRicDialog : Window
{
    public SubRicDialog(IReadOnlyList<string> current, IReadOnlyList<string> shorts)
    {
        InitializeComponent();
        var boxes = new[] { BoxA, BoxB, BoxC, BoxD };
        var shortBoxes = new[] { ShortA, ShortB, ShortC, ShortD };
        for (var i = 0; i < 4; i++)
        {
            boxes[i].Text = i < current.Count ? current[i] : string.Empty;
            shortBoxes[i].Text = i < shorts.Count ? shorts[i] : string.Empty;
        }
        Loaded += (_, _) => BoxA.Focus();
    }

    public string[] Names => [BoxA.Text.Trim(), BoxB.Text.Trim(), BoxC.Text.Trim(), BoxD.Text.Trim()];

    public string[] Shorts => [ShortA.Text.Trim(), ShortB.Text.Trim(), ShortC.Text.Trim(), ShortD.Text.Trim()];

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
