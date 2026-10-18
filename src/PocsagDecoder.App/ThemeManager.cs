using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PocsagDecoder.App;

/// <summary>Hell, Dunkel oder wie in Windows eingestellt. Tauscht die Farb-Datei in den App-Ressourcen.</summary>
public static class ThemeManager
{
    public const string FollowSystem = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    private static string _mode = FollowSystem;
    private static bool _listening;

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(string? mode)
    {
        _mode = mode is Light or Dark ? mode : FollowSystem;
        if (!_listening)
        {
            // Windows-Einstellung „App-Modus“ geändert: bei „Wie Windows“ sofort nachziehen.
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _mode == FollowSystem)
                    Application.Current?.Dispatcher.BeginInvoke(() => Update());
            };
            _listening = true;
        }
        Update();
    }

    private static void Update()
    {
        var dark = _mode == Dark || (_mode == FollowSystem && WindowsUsesDarkApps());
        var app = Application.Current;
        if (app is null) return;
        var dictionaries = app.Resources.MergedDictionaries;
        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        if (dictionaries.Count > 0 && dictionaries[0].Source == source && IsDark == dark) return;
        var theme = new ResourceDictionary { Source = source };
        if (dictionaries.Count > 0) dictionaries[0] = theme;
        else dictionaries.Add(theme);
        IsDark = dark;
        foreach (Window w in app.Windows) ApplyTitleBar(w);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Titelleiste des Fensters passend hell oder dunkel (Windows 10 ab 20H1, Windows 11).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        var value = IsDark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // Ältere Windows-Versionen: Titelleiste bleibt, wie sie ist.
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window w) ApplyTitleBar(w);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
