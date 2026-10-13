using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Filtering;

/// <summary>
/// Unterdrückt Wiederholungen derselben Nachricht (gleiche RIC, SubRIC und Text)
/// innerhalb eines Zeitfensters. Alarmierungen werden oft mehrfach gesendet.
/// </summary>
public sealed class DuplicateFilter
{
    private readonly Dictionary<(int Ric, int Function, string Text), DateTime> _seen = new();
    private readonly object _lock = new();

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);

    public bool IsDuplicate(PocsagMessage message)
    {
        if (Window <= TimeSpan.Zero) return false;
        var key = (message.Ric, message.Function, message.Text);
        lock (_lock)
        {
            var now = message.Timestamp;
            foreach (var old in _seen.Where(kv => now - kv.Value > Window).Select(kv => kv.Key).ToList())
                _seen.Remove(old);

            if (_seen.TryGetValue(key, out var last) && now - last <= Window)
                return true;

            _seen[key] = now;
            return false;
        }
    }
}
