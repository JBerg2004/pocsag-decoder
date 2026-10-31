using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PocsagDecoder.App.ViewModels;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.App;

public partial class SetupWindow : Window
{
    private readonly MainViewModel _main;
    private int _step;
    private double _frequency;

    public SetupWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        var d = main.Decoder;
        RtlPathBox.Text = d.RtlFmPath;
        MultimonPathBox.Text = d.MultimonPath;
        CustomModeBox.IsChecked = d.Mode == DecoderSourceMode.CustomCommand;
        CustomCommandBox.Text = d.CustomCommand;
        FrequencyBox.Text = d.FrequencyMHz.ToString("0.000###", CultureInfo.CurrentCulture);
        Baud512.IsChecked = d.Pocsag512;
        Baud1200.IsChecked = d.Pocsag1200;
        Baud2400.IsChecked = d.Pocsag2400;
        ThemeManager.ApplyTitleBar(this);
        ShowStep();
    }

    private void ShowStep()
    {
        var pages = new[] { ReceiverPage, FrequencyPage, ForwardingPage, SummaryPage };
        for (var i = 0; i < pages.Length; i++) pages[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        ProgressLabel.Text = $"SCHRITT {_step + 1} VON 4";
        StepTitle.Text = new[] { "Empfänger vorbereiten", "Frequenz festlegen", "Weiterleitung auswählen", "Bereit für den Empfang" }[_step];
        BackButton.IsEnabled = _step > 0;
        FrequencyPage.IsEnabled = CustomModeBox.IsChecked != true;
        NextButton.Content = _step == 3 ? "Einrichtung speichern" : "Weiter";
        ValidationMessage.Text = "";
        if (_step == 3)
            SummaryText.Text = (CustomModeBox.IsChecked == true ? "Empfang: eigener Befehl" : $"Empfang: RTL-SDR auf {_frequency:0.000} MHz")
                + "\nWeiterleitung: " + (WebhookBox.IsChecked == true ? WebhookNameBox.Text.Trim() + " (alle Meldungen)" : "Nur empfangen")
                + "\nBestehende Ereignisse und Schnittstellen bleiben erhalten.";
    }

    private bool ValidateStep()
    {
        string? error = null;
        if (_step == 0)
        {
            if (CustomModeBox.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(CustomCommandBox.Text)) error = "Bitte einen Empfangsbefehl eintragen.";
            }
            else if (!ToolExists(RtlPathBox.Text) || !ToolExists(MultimonPathBox.Text))
                error = "Bitte die vorhandenen Dateien rtl_fm.exe und multimon-ng.exe auswählen.";
        }
        if (_step == 1 && CustomModeBox.IsChecked != true)
        {
            error = ReceiverSetup.ValidateFrequency(FrequencyBox.Text, out _frequency);
            if (error is null && Baud512.IsChecked != true && Baud1200.IsChecked != true && Baud2400.IsChecked != true)
                error = "Bitte mindestens eine POCSAG-Geschwindigkeit auswählen.";
        }
        if (_step == 2 && WebhookBox.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(WebhookNameBox.Text)) error = "Bitte einen Namen für die Schnittstelle eintragen.";
            else if (WebhookNameBox.Text.IndexOfAny([',', ';']) >= 0) error = "Der Name darf kein Komma oder Semikolon enthalten.";
            else if (_main.Targets.Any(t => t.Name.Equals(WebhookNameBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))) error = "Eine Schnittstelle mit diesem Namen existiert bereits.";
            else error = ReceiverSetup.ValidateWebhookUrl(WebhookUrlBox.Text);
        }
        ValidationMessage.Text = error ?? "";
        return error is null;
    }

    private static bool ToolExists(string path)
    {
        path = path.Trim();
        if (string.IsNullOrEmpty(path)) return false;
        return File.Exists(path) || File.Exists(Path.Combine(AppContext.BaseDirectory, path))
            || (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Any(dir => File.Exists(Path.Combine(dir.Trim('"'), path)));
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateStep()) return;
        if (_step < 3) { _step++; ShowStep(); return; }
        var d = _main.Decoder;
        d.Mode = CustomModeBox.IsChecked == true ? DecoderSourceMode.CustomCommand : DecoderSourceMode.RtlSdr;
        d.RtlFmPath = RtlPathBox.Text.Trim();
        d.MultimonPath = MultimonPathBox.Text.Trim();
        d.CustomCommand = CustomCommandBox.Text.Trim();
        if (d.Mode == DecoderSourceMode.RtlSdr)
        {
            d.FrequencyMHz = _frequency;
            d.Pocsag512 = Baud512.IsChecked == true;
            d.Pocsag1200 = Baud1200.IsChecked == true;
            d.Pocsag2400 = Baud2400.IsChecked == true;
        }
        if (WebhookBox.IsChecked == true)
        {
            var target = new WebhookTarget { Name = WebhookNameBox.Text.Trim(), Url = WebhookUrlBox.Text.Trim(), Enabled = true };
            _main.Targets.Add(target);
            _main.Rules.Add(new FilterRule { Name = "Weiterleitung an " + target.Name, RicPattern = "*", TargetName = target.Name, Enabled = true });
            _main.SelectedTarget = target;
        }
        _main.Settings.SetupCompleted = true;
        _main.SaveCommand.Execute(null);
        DialogResult = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) { _step--; ShowStep(); } }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void BrowseRtl_Click(object sender, RoutedEventArgs e) => Browse(RtlPathBox);
    private void BrowseMultimon_Click(object sender, RoutedEventArgs e) => Browse(MultimonPathBox);
    private void Browse(System.Windows.Controls.TextBox box)
    {
        var dialog = new OpenFileDialog { Filter = "Programme (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName;
    }
}
