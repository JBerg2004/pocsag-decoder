using System.Windows;
using System.Threading;
using System.Windows.Threading;

namespace PocsagDecoder.App;

public partial class App : Application
{
    private const string InstanceName = "PocsagDecoder.SingleInstance";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
        // Titelleiste jedes Fensters (auch Dialoge) passend zum hellen oder dunklen Design.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ThemeManager.ApplyTitleBar((Window)s)));

        // Nur eine Instanz: ein zweiter Start holt das laufende Fenster nach vorn (der SDR-Stick kann nur einmal benutzt werden).
        _instanceMutex = new Mutex(true, InstanceName + ".Mutex", out var first);
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

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
