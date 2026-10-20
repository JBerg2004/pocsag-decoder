using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Energiesparmodus und Ruhezustand verhindern, solange das Programm läuft.</summary>
public sealed partial class MainViewModel
{
    private readonly SleepPrevention _sleep = new();

    public bool PreventSleep
    {
        get => Settings.PreventSleep;
        set
        {
            if (Settings.PreventSleep == value) return;
            Settings.PreventSleep = value;
            ApplySleepPrevention();
            OnPropertyChanged();
            RequestSave();
        }
    }

    // Im Konstruktor auf dem UI-Thread: Die Sperre gilt für den aufrufenden Thread, und der läuft bis zum Programmende.
    private void InitSleepPrevention() { if (Settings.PreventSleep) ApplySleepPrevention(); }

    private void ApplySleepPrevention()
    {
        if (_sleep.Set(Settings.PreventSleep))
            AddLog(Settings.PreventSleep ? "Energiesparmodus und Ruhezustand sind gesperrt, solange das Programm läuft." : "Energiesparmodus und Ruhezustand sind wieder erlaubt.");
        else
            AddLog("Energiesparmodus konnte nicht gesperrt werden (SetThreadExecutionState abgelehnt).");
    }
}
