using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows.Threading;
using PocsagDecoder.App.ViewModels;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.App.Tests;

public class PresentationTests
{
    [Fact]
    public void DeliveryDetailsUpdateAfterCompletionAndFailure()
    {
        var item = new MessageItem(new PocsagMessage());
        var changes = new List<string?>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        item.Forwarding = "Webhook: sende…";
        Assert.Equal("Wird gesendet…", item.DeliverySummary);
        item.Forwarding = "Webhook: ✓ 200 OK | MQTT: ✓ veröffentlicht";
        Assert.Equal(2, item.DeliveryLines.Count);
        Assert.Equal("Erfolgreich gesendet", item.DeliverySummary);
        item.HasError = true;
        Assert.Equal("Fehler beim Versand", item.DeliverySummary);
        Assert.Contains(nameof(MessageItem.DeliverySummary), changes);
        Assert.Contains(nameof(MessageItem.DeliveryLines), changes);
    }

    [Fact]
    public void SearchCombinesWithFiltersAndPreservesColumnPreferences() => RunSta(() =>
    {
        var dir = Directory.CreateTempSubdirectory("pocsag-ui-tests");
        try
        {
            var settings = AppSettings.CreateDefault();
            settings.CheckForUpdates = false;
            settings.PreventSleep = false;
            settings.LogDirectory = dir.FullName;
            var path = Path.Combine(dir.FullName, "settings.json");
            SettingsStore.Save(settings, path);
            var main = new MainViewModel(path);
            main.Messages.Add(new MessageItem(new PocsagMessage { Ric = 1234567, Text = "ciphertext", RicDescription = "Wache Nord", RicLocation = "Musterstadt" }));
            main.Messages.Add(new MessageItem(new PocsagMessage { Ric = 765432, Text = "other", RicDescription = "Wache Süd" }));
            var pane = main.Panes[0];
            pane.QuickSearch = "nord";
            Assert.Equal(1, pane.VisibleCount);
            pane.QuickSearch = "1234567";
            Assert.Equal(1, pane.VisibleCount);
            pane.QuickSearch = "Musterstadt";
            Assert.Equal(1, pane.VisibleCount);
            pane.Ric = "765432";
            Assert.Equal(0, pane.VisibleCount);
            pane.Ric = "";
            pane.QuickSearch = "";
            Assert.Equal(2, pane.VisibleCount);
            pane.SetColumnVisible("Ort", false);
            main.CompactReceiveList = false;
            main.CompactReceiveList = true;
            Assert.Contains("Ort", pane.HiddenColumns);
            Assert.DoesNotContain("Text", pane.HiddenColumns);
            main.SaveCommand.Execute(null);
            Assert.True(SettingsStore.Load(path).CompactReceiveList);
            main.SelectedMessage = main.Messages[0];
            main.ClearMessagesCommand.Execute(null);
            Assert.Null(main.SelectedMessage);
        }
        finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); dir.Delete(true); }
    });

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI test did not finish");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
