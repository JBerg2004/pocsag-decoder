using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.Tests;

public class SettingsRecoveryTests
{
    [Theory]
    [InlineData("{ broken")]
    [InlineData("null")]
    [InlineData("{\"Rules\":null}")]
    [InlineData("{\"Targets\":[null]}")]
    public void InvalidSettingsArePreservedAndSavingIsBlocked(string original)
    {
        var dir = Directory.CreateTempSubdirectory("pocsag-settings");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, original);
            var result = SettingsStore.LoadSafely(path);
            Assert.False(result.CanSave);
            Assert.NotNull(result.Error);
            Assert.NotNull(result.BackupPath);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Equal(original, File.ReadAllText(result.BackupPath!));
            Assert.All(result.Settings.Rules, rule => Assert.False(rule.Enabled));

            var again = SettingsStore.LoadSafely(path);
            Assert.NotEqual(result.BackupPath, again.BackupPath);
            Assert.Equal(original, File.ReadAllText(result.BackupPath!));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void UnreadableSettingsBlockSavingEvenWhenBackupFails()
    {
        var dir = Directory.CreateTempSubdirectory("pocsag-settings");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, "{}");
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = SettingsStore.LoadSafely(path);
            Assert.False(result.CanSave);
            Assert.Null(result.BackupPath);
            Assert.NotNull(result.Error);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void ValidAndMissingSettingsRemainWritable()
    {
        var dir = Directory.CreateTempSubdirectory("pocsag-settings");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            Assert.True(SettingsStore.LoadSafely(path).CanSave);
            var settings = AppSettings.CreateDefault();
            settings.DuplicateWindowSeconds = 123;
            SettingsStore.Save(settings, path);
            var result = SettingsStore.LoadSafely(path);
            Assert.True(result.CanSave);
            Assert.Null(result.BackupPath);
            Assert.Equal(123, result.Settings.DuplicateWindowSeconds);
            Assert.Single(Directory.GetFiles(dir.FullName));
        }
        finally { dir.Delete(true); }
    }
}
