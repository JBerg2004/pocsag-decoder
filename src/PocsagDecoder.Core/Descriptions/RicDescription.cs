using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PocsagDecoder.Core.Descriptions;

/// <summary>
/// Eine RIC mit allgemeiner Bezeichnung und je einer Beschreibung pro SubRIC A–D.
/// </summary>
public sealed class RicEntry : INotifyPropertyChanged
{
    private int _ric;
    private string _name = string.Empty;
    private string _shortName = string.Empty;
    private string _location = string.Empty;
    private string _bosMonUser = string.Empty;
    private string _a = string.Empty, _b = string.Empty, _c = string.Empty, _d = string.Empty;

    public int Ric { get => _ric; set => Set(ref _ric, value); }

    /// <summary>Allgemeine Bezeichnung der RIC (z.B. "FF Musterstadt").</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Kurze Bezeichnung der RIC (z.B. "Musterdorf WeFü").</summary>
    public string ShortName { get => _shortName; set => Set(ref _shortName, value); }

    /// <summary>Ort der RIC (z.B. "Musterdorf").</summary>
    public string Location { get => _location; set => Set(ref _location, value); }

    /// <summary>Benutzer des BosMon-Netzwerkereignisses dieser RIC (bei FE2 der Einheiten-Code).</summary>
    public string BosMonUser { get => _bosMonUser; set => Set(ref _bosMonUser, value); }

    public string A { get => _a; set => Set(ref _a, value); }
    public string B { get => _b; set => Set(ref _b, value); }
    public string C { get => _c; set => Set(ref _c, value); }
    public string D { get => _d; set => Set(ref _d, value); }

    [JsonIgnore]
    public string RicFormatted => Ric.ToString("D7");

    /// <summary>Beschreibung der SubRIC zu den Funktionsbits 0–3.</summary>
    public string GetSub(int function) => function switch { 0 => A, 1 => B, 2 => C, 3 => D, _ => string.Empty };

    public void SetSub(int function, string value)
    {
        switch (function)
        {
            case 0: A = value; break;
            case 1: B = value; break;
            case 2: C = value; break;
            case 3: D = value; break;
        }
    }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name + ShortName + Location + BosMonUser + A + B + C + D);

    /// <summary>Übernimmt alle nicht leeren Felder von <paramref name="other"/>.</summary>
    public void MergeFrom(RicEntry other)
    {
        if (!string.IsNullOrWhiteSpace(other.Name)) Name = other.Name.Trim();
        if (!string.IsNullOrWhiteSpace(other.ShortName)) ShortName = other.ShortName.Trim();
        if (!string.IsNullOrWhiteSpace(other.Location)) Location = other.Location.Trim();
        if (!string.IsNullOrWhiteSpace(other.BosMonUser)) BosMonUser = other.BosMonUser.Trim();
        for (var f = 0; f < 4; f++)
            if (!string.IsNullOrWhiteSpace(other.GetSub(f))) SetSub(f, other.GetSub(f).Trim());
    }

    /// <summary>Übernimmt Felder von <paramref name="other"/> nur dort, wo hier noch nichts steht; der BosMon-Benutzer wird immer übernommen.</summary>
    public void FillEmptyFrom(RicEntry other)
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = other.Name.Trim();
        if (string.IsNullOrWhiteSpace(ShortName)) ShortName = other.ShortName.Trim();
        if (string.IsNullOrWhiteSpace(Location)) Location = other.Location.Trim();
        if (!string.IsNullOrWhiteSpace(other.BosMonUser)) BosMonUser = other.BosMonUser.Trim();
        for (var f = 0; f < 4; f++)
            if (string.IsNullOrWhiteSpace(GetSub(f))) SetSub(f, other.GetSub(f).Trim());
    }

    public RicEntry Clone() => new() { Ric = Ric, Name = Name, ShortName = ShortName, Location = Location, BosMonUser = BosMonUser, A = A, B = B, C = C, D = D };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string? value, [CallerMemberName] string? name = null)
    {
        value ??= string.Empty; // nicht trimmen: sonst verschluckt die Eingabe Leerzeichen beim Tippen
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Set(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RicFormatted)));
    }
}

/// <summary>Älteres Format (eine Zeile pro RIC/SubRIC), nur noch zum Übernehmen alter Einstellungen.</summary>
public sealed class RicDescription
{
    public int Ric { get; set; }
    public string SubRic { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
