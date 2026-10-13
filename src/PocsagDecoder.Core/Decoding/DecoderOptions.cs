using System.Globalization;

namespace PocsagDecoder.Core.Decoding;

public enum DecoderSourceMode
{
    /// <summary>rtl_fm liest den SDR-Stick, multimon-ng dekodiert das Audio.</summary>
    RtlSdr,

    /// <summary>Beliebiger Befehl, dessen Ausgabe multimon-ng-Zeilen enthält.</summary>
    CustomCommand,
}

public sealed class DecoderOptions
{
    public DecoderSourceMode Mode { get; set; } = DecoderSourceMode.RtlSdr;

    public string RtlFmPath { get; set; } = "rtl_fm.exe";
    public string MultimonPath { get; set; } = "multimon-ng.exe";

    /// <summary>Frequenz in MHz, z.B. 173.255 (BOS-Funkmeldesystem).</summary>
    public double FrequencyMHz { get; set; } = 173.255;

    /// <summary>Verstärkung in dB, leer bzw. null = automatisch.</summary>
    public double? GainDb { get; set; }

    /// <summary>Frequenzkorrektur des Sticks in ppm.</summary>
    public int Ppm { get; set; }

    /// <summary>Squelch-Level für rtl_fm (0 = aus).</summary>
    public int Squelch { get; set; }

    /// <summary>Index des SDR-Sticks, falls mehrere angeschlossen sind.</summary>
    public int DeviceIndex { get; set; }

    public bool Pocsag512 { get; set; } = true;
    public bool Pocsag1200 { get; set; } = true;
    public bool Pocsag2400 { get; set; } = true;

    /// <summary>multimon-ng "-f": alpha (Standard bei BOS), numeric, skyper oder auto.</summary>
    public string DecodeMode { get; set; } = "alpha";

    /// <summary>Zusätzliche Parameter für rtl_fm.</summary>
    public string RtlFmExtraArgs { get; set; } = string.Empty;

    /// <summary>Zusätzliche Parameter für multimon-ng.</summary>
    public string MultimonExtraArgs { get; set; } = string.Empty;

    /// <summary>Kommandozeile für <see cref="DecoderSourceMode.CustomCommand"/> (wird über cmd.exe /c ausgeführt).</summary>
    public string CustomCommand { get; set; } =
        "rtl_fm.exe -f 173.255M -M fm -s 22050 - | multimon-ng.exe -t raw -a POCSAG512 -a POCSAG1200 -a POCSAG2400 -f alpha -";

    /// <summary>Wandelt [ \ ] { | } ~ in deutsche Umlaute um.</summary>
    public bool GermanUmlauts { get; set; } = true;

    public string BuildRtlFmArguments()
    {
        var inv = CultureInfo.InvariantCulture;
        var args = new List<string>
        {
            "-d", DeviceIndex.ToString(inv),
            "-f", FrequencyMHz.ToString("0.000###", inv) + "M",
            "-M", "fm",
            "-s", "22050",
            "-p", Ppm.ToString(inv),
            "-l", Squelch.ToString(inv),
        };
        if (GainDb is { } gain) args.AddRange(["-g", gain.ToString("0.0", inv)]);
        var s = string.Join(' ', args);
        if (!string.IsNullOrWhiteSpace(RtlFmExtraArgs)) s += " " + RtlFmExtraArgs.Trim();
        return s + " -";
    }

    public string BuildMultimonArguments()
    {
        var args = new List<string> { "-t", "raw" };
        if (Pocsag512) args.AddRange(["-a", "POCSAG512"]);
        if (Pocsag1200) args.AddRange(["-a", "POCSAG1200"]);
        if (Pocsag2400) args.AddRange(["-a", "POCSAG2400"]);
        var mode = string.IsNullOrWhiteSpace(DecodeMode) ? "alpha" : DecodeMode.Trim().ToLowerInvariant();
        args.AddRange(["-f", mode]);
        var s = string.Join(' ', args);
        if (!string.IsNullOrWhiteSpace(MultimonExtraArgs)) s += " " + MultimonExtraArgs.Trim();
        return s + " -";
    }
}
