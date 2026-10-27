using System.Diagnostics;
using System.Text;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Decoding;

/// <summary>
/// Startet rtl_fm und multimon-ng, verbindet beide über eine Pipe und
/// meldet jede dekodierte POCSAG-Nachricht über <see cref="MessageReceived"/>.
/// </summary>
public sealed class DecoderPipeline : IAsyncDisposable
{
    private readonly DecoderOptions _options;
    private Process? _rtlFm;
    private Process? _multimon;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;

    public DecoderPipeline(DecoderOptions options) => _options = options;

    public event EventHandler<PocsagMessage>? MessageReceived;

    /// <summary>Diagnoseausgaben (stderr der Tools, Start/Stopp).</summary>
    public event EventHandler<string>? Log;

    /// <summary>Wird ausgelöst, wenn ein Prozess unerwartet beendet wurde.</summary>
    public event EventHandler? Stopped;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();

        if (_options.Mode == DecoderSourceMode.CustomCommand)
        {
            StartCustom();
        }
        else
        {
            StartRtlSdr();
        }
        IsRunning = true;
    }

    private void StartRtlSdr()
    {
        var rtlArgs = _options.BuildRtlFmArguments();
        var mmArgs = _options.BuildMultimonArguments();
        OnLog($"Starte: \"{_options.RtlFmPath}\" {rtlArgs}");
        OnLog($"Starte: \"{_options.MultimonPath}\" {mmArgs}");

        _multimon = CreateProcess(_options.MultimonPath, mmArgs, redirectInput: true);
        _rtlFm = CreateProcess(_options.RtlFmPath, rtlArgs, redirectInput: false);

        StartProcess(_multimon, "multimon-ng", parseStdout: true);
        try
        {
            StartProcess(_rtlFm, "rtl_fm", parseStdout: false);
        }
        catch
        {
            KillQuietly(_multimon);
            throw;
        }

        // Rohes Audio (22050 Hz, s16le) von rtl_fm nach multimon-ng kopieren.
        var token = _cts!.Token;
        _pumpTask = Task.Run(async () =>
        {
            try
            {
                await _rtlFm.StandardOutput.BaseStream.CopyToAsync(_multimon.StandardInput.BaseStream, 4096, token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }, token);
    }

    private void StartCustom()
    {
        OnLog($"Starte: cmd.exe /c {_options.CustomCommand}");
        _multimon = CreateProcess("cmd.exe", "/c " + _options.CustomCommand, redirectInput: false);
        StartProcess(_multimon, "Befehl", parseStdout: true);
    }

    private static Process CreateProcess(string fileName, string arguments, bool redirectInput)
    {
        var workDir = Path.GetDirectoryName(fileName);
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = redirectInput,
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? Environment.CurrentDirectory : workDir,
                StandardOutputEncoding = Encoding.Latin1,
                StandardErrorEncoding = Encoding.Latin1,
            },
            EnableRaisingEvents = true,
        };
    }

    private void StartProcess(Process process, string name, bool parseStdout)
    {
        if (parseStdout)
        {
            process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        }
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) OnLog($"[{name}] {e.Data}");
        };
        process.Exited += (_, _) =>
        {
            int? code = null;
            try
            {
                // Restliche Ausgaben (z.B. Fehlermeldungen) noch einlesen, bevor gestoppt wird.
                Task.Run(() => process.WaitForExit()).Wait(2000);
                code = process.ExitCode;
            }
            catch { }
            OnLog($"[{name}] beendet (Exit-Code {code?.ToString() ?? "?"})");
            if (IsRunning && !(_cts?.IsCancellationRequested ?? true))
            {
                _ = StopAsync().ContinueWith(_ => Stopped?.Invoke(this, EventArgs.Empty));
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"{name} konnte nicht gestartet werden ({process.StartInfo.FileName}): {ex.Message}", ex);
        }

        if (parseStdout) process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (MultimonParser.TryParse(line, out var message) && message is not null)
        {
            if (_options.GermanUmlauts && message.ContentType == PocsagContentType.Alpha)
            {
                message = new PocsagMessage
                {
                    Timestamp = message.Timestamp,
                    Baud = message.Baud,
                    Ric = message.Ric,
                    Function = message.Function,
                    ContentType = message.ContentType,
                    Text = GermanCharset.Convert(message.Text),
                    UntrimmedText = message.UntrimmedText is null ? null : GermanCharset.Convert(message.UntrimmedText),
                    Raw = message.Raw,
                };
            }
            MessageReceived?.Invoke(this, message);
        }
        else
        {
            OnLog($"[decoder] {line}");
        }
    }

    /// <summary>Speist eine Zeile so ein, als käme sie von multimon-ng (für Tests/Simulation).</summary>
    public void InjectLine(string line) => HandleLine(line);

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        KillQuietly(_rtlFm);
        KillQuietly(_multimon);
        if (_pumpTask is not null)
        {
            try { await _pumpTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        _rtlFm?.Dispose();
        _multimon?.Dispose();
        _rtlFm = null;
        _multimon = null;
        _pumpTask = null;
        OnLog("Decoder gestoppt.");
    }

    private static void KillQuietly(Process? p)
    {
        try
        {
            if (p is not null && !p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private void OnLog(string text) => Log?.Invoke(this, text);

    public async ValueTask DisposeAsync() => await StopAsync();
}
