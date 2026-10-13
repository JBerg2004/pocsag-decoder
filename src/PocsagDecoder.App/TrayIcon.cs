using System.Windows;
using Forms = System.Windows.Forms;

namespace PocsagDecoder.App;

/// <summary>Symbol im Infobereich (unten rechts), damit das Programm im Hintergrund weiterlaufen kann.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _startStop;

    public TrayIcon(Action open, Action startReceive, Func<bool> isRunning, Action exit)
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
        _startStop = new Forms.ToolStripMenuItem("Empfang starten", null, (_, _) => startReceive());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Öffnen", null, (_, _) => open()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_startStop);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Beenden", null, (_, _) => exit()));
        // Stoppen ist hier bewusst nicht möglich, nur Starten, falls der Empfang nicht läuft.
        menu.Opening += (_, _) => _startStop.Visible = !isRunning();

        _icon = new Forms.NotifyIcon
        {
            Icon = stream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream),
            Text = "POCSAG Decoder",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
        _icon.BalloonTipClicked += (_, _) => open();
    }

    /// <summary>Tooltip (max. 127 Zeichen).</summary>
    public void SetStatus(string text)
    {
        var t = "POCSAG Decoder – " + text;
        _icon.Text = t.Length > 127 ? t[..127] : t;
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, string.IsNullOrWhiteSpace(text) ? " " : text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
