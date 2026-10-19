using System.Runtime.InteropServices;

namespace PocsagDecoder.Core.Settings;

/// <summary>
/// Hält Windows wach (kein Energiesparmodus, kein Ruhezustand), solange das Programm läuft.
/// Der Bildschirm darf trotzdem ausgehen. Gilt für den Thread, der <see cref="Set"/> aufruft (hier der UI-Thread).
/// </summary>
public sealed class SleepPrevention
{
    public const uint EsContinuous = 0x80000000;
    public const uint EsSystemRequired = 0x00000001;

    private readonly Func<uint, uint> _setState;

    /// <param name="setState">Zum Testen austauschbar; Standard ist SetThreadExecutionState.</param>
    public SleepPrevention(Func<uint, uint>? setState = null)
    {
        _setState = setState ?? SetNative;
    }

    public bool Active { get; private set; }

    /// <summary>Sperre ein- oder ausschalten; liefert false, wenn Windows den Aufruf ablehnt.</summary>
    public bool Set(bool prevent)
    {
        if (prevent == Active) return true;
        var flags = prevent ? EsContinuous | EsSystemRequired : EsContinuous;
        var ok = _setState(flags) != 0;
        if (ok) Active = prevent;
        return ok;
    }

    private static uint SetNative(uint flags) =>
        OperatingSystem.IsWindows() ? SetThreadExecutionState(flags) : 0;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
