using System.Windows;
using System.Threading;
using System.Windows.Threading;

namespace PocsagDecoder.App;

public partial class App : Application
{
    private const string InstanceName = "PocsagDecoder.SingleInstance";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private bool _ownsMutex;

    /// <summary>Beim Beenden das Programm neu starten (nach dem Wiederherstellen einer Sicherung).</summary>
    public bool RestartOnExit { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
        // Titelleiste jedes Fensters (auch Dialoge) passend zum hellen oder dunklen Design.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ThemeManager.ApplyTitleBar((Window)s)));

        // Nur eine Instanz: ein zweiter Start holt das laufende Fenster nach vorn (der SDR-Stick kann nur einmal benutzt werden).
        // Nach „Sicherung wiederherstellen“ (--restarted) warten, bis die alte Instanz beendet ist.
        var restarted = e.Args.Any(a => a.Equals("--restarted", StringComparison.OrdinalIgnoreCase));
        _instanceMutex = new Mutex(false, InstanceName + ".Mutex");
        bool first;
        try { first = _instanceMutex.WaitOne(restarted ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { first = true; }
        _ownsMutex = first;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        var minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow { StartHidden = minimized };
        MainWindow = window;
        // Beim Abmelden/Herunterfahren nicht im Infobereich festhalten, sondern sauber beenden.
        SessionEnding += (_, _) => window.AllowExit();
        window.Show();

        var signal = _showSignal;
        var listener = new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(window.ShowFromTray);
        }) { IsBackground = true, Name = "SingleInstance" };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            _instanceMutex?.ReleaseMutex();
            _ownsMutex = false;
        }
        if (RestartOnExit && Environment.ProcessPath is { } exe)
        {
            try { System.Diagnostics.Process.Start(exe, "--restarted"); }
            catch (Exception) { /* dann startet der Nutzer selbst neu */ }
        }
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
