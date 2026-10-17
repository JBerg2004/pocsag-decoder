using PocsagDecoder.Core.Descriptions;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Tests;

public class DescriptionTests
{
    [Fact]
    public void ParsesTemplate()
    {
        var r = RicDirectory.Parse(RicDirectory.Template);
        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Entries.Count);
        var dir = new RicDirectory(r.Entries);
        Assert.Equal("FF Musterstadt · Vollalarm", dir.Describe(1234567, 0));
        Assert.Equal("FF Musterstadt · Info", dir.Describe(1234567, 3));
        Assert.Equal("FF Beispieldorf", dir.Describe(1234568, 2)); // C leer → nur Bezeichnung
        Assert.Equal("", dir.Describe(7654321, 0));
    }

    [Fact]
    public void ParsesColumnsByHeaderInAnyOrder()
    {
        var r = RicDirectory.Parse("RIC\tSubRIC D\tSubRIC A\tEinheit\n0012345\tSirene\tVollalarm\tWehr Nord\n");
        Assert.Empty(r.Errors);
        var dir = new RicDirectory(r.Entries);
        Assert.Equal("Wehr Nord · Vollalarm", dir.Describe(12345, 0));
        Assert.Equal("Wehr Nord · Sirene", dir.Describe(12345, 3));
        Assert.Equal("Wehr Nord", dir.Describe(12345, 1));
    }

    [Fact]
    public void ParsesColumnsWithoutHeader()
    {
        var r = RicDirectory.Parse("1234568;FF Test;Voll;Klein;Probe;Info\n1234569;A1;B1;C1;D1\n");
        Assert.Empty(r.Errors);
        var dir = new RicDirectory(r.Entries);
        Assert.Equal("FF Test · Probe", dir.Describe(1234568, 2));
        Assert.Equal("D1", dir.Describe(1234569, 3));
    }

    [Fact]
    public void AcceptsOneLinePerSubRicAndMergesByRic()
    {
        var text = "RIC,SubRIC,Text\n" +
                   "0012345,1,Wehr Nord\n" +              // SubRIC 1 = A
                   "12345\tB\tWehr Nord, Gruppe 2\n" +    // Tab
                   "12345;;Allgemein\n" +
                   "1234568C;Sirene\n" +                  // Kurzform
                   "1234569;Gerätewart\n" +               // nur RIC
                   "# Kommentar\n\n" +
                   "abc;A;kaputt\n" +
                   "1234570;E;falsche SubRIC\n" +
                   "9999999;A;zu groß\n";
        var r = RicDirectory.Parse(text);
        Assert.Equal(3, r.Entries.Count);
        Assert.Equal(3, r.Errors.Count);
        var dir = new RicDirectory(r.Entries);
        Assert.Equal("Allgemein · Wehr Nord", dir.Describe(12345, 0));
        Assert.Equal("Allgemein · Wehr Nord, Gruppe 2", dir.Describe(12345, 1));
        Assert.Equal("Sirene", dir.Describe(1234568, 2));
        Assert.Equal("Gerätewart", dir.Describe(1234569, 3));
    }

    [Fact]
    public void ParsesJson()
    {
        var r = RicDirectory.Parse("""
            [{"ric": 1234568, "name": "FF Test", "a": "Voll", "d": "Info"},
             {"ric": "1234568", "subric": "B", "description": "Klein"}]
            """);
        Assert.Empty(r.Errors);
        var dir = new RicDirectory(r.Entries);
        Assert.Equal("FF Test · Voll", dir.Describe(1234568, 0));
        Assert.Equal("FF Test · Klein", dir.Describe(1234568, 1));
    }

    [Fact]
    public void ExportRoundTrips()
    {
        var entries = RicDirectory.Parse(RicDirectory.Template).Entries;
        var again = RicDirectory.Parse(RicDirectory.ToCsv(entries));
        Assert.Empty(again.Errors);
        Assert.Equal(entries.Count, again.Entries.Count);
        Assert.Equal("Sirene", again.Entries[1].D);
        Assert.Equal("", again.Entries[1].C);
    }

    [Fact]
    public void MigratesLegacySettings()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "settings.json");
        File.WriteAllText(path, """{"Descriptions":[{"Ric":5,"SubRic":"","Description":"FF"},{"Ric":5,"SubRic":"C","Description":"Probe"}]}""");
        var s = SettingsStore.Load(path);
        Assert.Null(s.Descriptions);
        Assert.Equal("FF · Probe", new RicDirectory(s.RicEntries).Describe(5, 2));
    }

    [Fact]
    public void DescriptionPlaceholderIsAvailable()
    {
        var dir = new RicDirectory(RicDirectory.Parse(RicDirectory.Template).Entries);
        var m = new PocsagMessage { Ric = 1234567, Function = 1, Text = "x" };
        dir.Annotate(m);
        Assert.Equal("FF Musterstadt · Kleinalarm|FF Musterstadt|Kleinalarm",
            TemplateRenderer.Render("{description}|{ric_description}|{subric_description}", m, "r", TemplateEscaping.None));
    }

    [Fact]
    public void GlobalSubRicsApplyToAllRicsUnlessOverridden()
    {
        var dir = new RicDirectory(RicDirectory.Parse("RIC;Bezeichnung;A;B;C;D\n1;FF Eins;;;Probe;\n").Entries);
        dir.SetGlobalSubRics(["Vollalarm", "Kleinalarm", "Probealarm", "Info"]);
        Assert.Equal("FF Eins · Vollalarm", dir.Describe(1, 0));
        Assert.Equal("FF Eins · Probe", dir.Describe(1, 2));      // Eintrag der RIC hat Vorrang
        Assert.Equal("Info", dir.Describe(999, 3));               // RIC unbekannt → nur SubRIC
        var m = new PocsagMessage { Ric = 999, Function = 1 };
        dir.Annotate(m);
        Assert.Equal("", m.RicDescription);
        Assert.Equal("Kleinalarm", m.SubRicDescription);
    }

    [Fact]
    public void ParsesBosWatchConfig()
    {
        const string yaml = """
            server:
              port: 8080
            router:
              - name: Vorverarbeitung
                route:
                  - type: module
                    res: descriptor
                    config:
                      - scanField: ric
                        descrField: description
                        descriptions:
                        - for: "1234501"
                          add: "0.1 Musterstadt Leitung"
                        - for: 1234502
                          add: "0.2 Musterstadt Presse Ö"
                      - scanField: tone
                        descriptions:
                        - for: "12345"
                          add: "Sirene"
            """;
        var r = RicDirectory.Parse(yaml);
        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Entries.Count);
        Assert.Equal("0.1 Musterstadt Leitung", r.Entries[0].Name);
        Assert.Equal("0.2 Musterstadt Presse Ö", r.Entries[1].Name);
    }

    [Fact]
    public void SubRicLetterCaseIsConfigurable()
    {
        var m = new PocsagMessage { Ric = 1234567, Function = 1, Text = "x" };
        try
        {
            TemplateRenderer.LowercaseSubRic = true;
            Assert.Equal("b|B|b", TemplateRenderer.Render("{subric}|{subric_upper}|{subric_lower}", m, "", TemplateEscaping.None));
            TemplateRenderer.LowercaseSubRic = false;
            Assert.Equal("B", TemplateRenderer.Render("{subric}", m, "", TemplateEscaping.None));
        }
        finally { TemplateRenderer.LowercaseSubRic = false; }
    }

    [Fact]
    public void ShortNameIsImportedExportedAndSent()
    {
        var r = RicDirectory.Parse("RIC;Bezeichnung;Bezeichnung kurz;A\n1234567;11.1 Musterdorf WeFü;Musterdorf WeFü;\n");
        Assert.Empty(r.Errors);
        Assert.Equal("Musterdorf WeFü", r.Entries[0].ShortName);
        Assert.Equal("11.1 Musterdorf WeFü", r.Entries[0].Name);
        Assert.Contains("1234567;11.1 Musterdorf WeFü;Musterdorf WeFü;", RicDirectory.ToCsv(r.Entries));
        var again = RicDirectory.Parse(RicDirectory.ToCsv(r.Entries));
        Assert.Equal("Musterdorf WeFü", again.Entries[0].ShortName);

        var dir = new RicDirectory(r.Entries);
        var m = new PocsagMessage { Ric = 1234567, Function = 0, Text = "x" };
        dir.Annotate(m);
        Assert.Equal("Musterdorf%20WeF%C3%BC", TemplateRenderer.Render("{ric_short}", m, "", TemplateEscaping.Url));
    }

    [Fact]
    public void LocationIsImportedExportedAndSent()
    {
        var r = RicDirectory.Parse("RIC;Ort;Kurz;Bezeichnung\n1234567;Musterdorf;11 WeFü;11 Musterdorf WeFü\n");
        Assert.Empty(r.Errors);
        Assert.Equal("Musterdorf", r.Entries[0].Location);
        Assert.Equal("11 WeFü", r.Entries[0].ShortName);
        Assert.Equal("11 Musterdorf WeFü", r.Entries[0].Name);
        var again = RicDirectory.Parse(RicDirectory.ToCsv(r.Entries)).Entries[0];
        Assert.Equal(("Musterdorf", "11 WeFü", "11 Musterdorf WeFü"), (again.Location, again.ShortName, again.Name));
        var m = new PocsagMessage { Ric = 1234567, Function = 0 };
        new RicDirectory(r.Entries).Annotate(m);
        Assert.Equal("Musterdorf", TemplateRenderer.Render("{ric_location}", m, "", TemplateEscaping.None));
    }

    private const string BosMonEvents =
        """
        <section name="events">
          <item name="items">3</item>
          <section name="item0">
            <item name="enabled">True</item>
            <item name="description">1.0 FF Musterstadt</item>
            <section name="filters">
              <item name="items">1</item>
              <section name="item0">
                <item name="type">pocsag</item>
                <item name="address">1234567</item>
                <item name="function">*</item>
              </section>
            </section>
            <section name="action0">
              <item name="actionId">2</item>
              <item name="networkHost">192.0.2.10</item>
              <item name="networkUser">Musterstadt Staffel</item>
              <item name="networkPort">5555</item>
            </section>
          </section>
          <section name="item1">
            <item name="enabled">False</item>
            <item name="description">Probealarm</item>
            <section name="filters">
              <section name="item0">
                <item name="type">pocsag</item>
                <item name="address">1234568</item>
              </section>
            </section>
            <section name="action0">
              <item name="actionId">2</item>
              <item name="networkHost">127.0.0.1</item>
              <item name="networkUser">Probeeinheit</item>
            </section>
          </section>
          <section name="item2">
            <item name="enabled">True</item>
            <item name="description">2.0 Beispieldorf</item>
            <section name="filters">
              <section name="item0">
                <item name="type">pocsag</item>
                <item name="address">0012345</item>
              </section>
            </section>
          </section>
        </section>
        """;

    [Fact]
    public void ImportsBosMonEventUsers()
    {
        var r = RicDirectory.Parse(BosMonEvents);
        Assert.Empty(r.Errors);
        Assert.True(r.FillEmptyOnly);
        Assert.Equal(2, r.Entries.Count);
        var a = r.Entries.Single(e => e.Ric == 1234567);
        Assert.Equal("Musterstadt Staffel", a.BosMonUser);
        Assert.Equal("1.0 FF Musterstadt", a.Name);
        Assert.Equal("", r.Entries.Single(e => e.Ric == 12345).BosMonUser);
        Assert.DoesNotContain(r.Entries, e => e.Ric == 1234568); // deaktiviertes Testereignis
    }

    [Fact]
    public void BosMonImportKeepsExistingNames()
    {
        var existing = new RicEntry { Ric = 1234567, Name = "FF Musterstadt" };
        existing.FillEmptyFrom(RicDirectory.Parse(BosMonEvents).Entries.Single(e => e.Ric == 1234567));
        Assert.Equal("FF Musterstadt", existing.Name);
        Assert.Equal("Musterstadt Staffel", existing.BosMonUser);
    }

    [Fact]
    public void BosMonUserColumnRoundTripsAndRenders()
    {
        var csv = RicDirectory.ToCsv([new RicEntry { Ric = 1234567, Name = "FF Musterstadt", BosMonUser = "Staffel 1" }]);
        var back = RicDirectory.Parse(csv).Entries.Single();
        Assert.Equal("Staffel 1", back.BosMonUser);
        Assert.Equal("FF Musterstadt", back.Name);
        var m = new PocsagMessage { Ric = 1234567, Function = 0 };
        new RicDirectory([back]).Annotate(m);
        Assert.Equal("Staffel 1", TemplateRenderer.Render("{bosmon_user}", m, "", TemplateEscaping.None));
    }

    [Fact]
    public void ParsesBosMonRicList()
    {
        var r = RicDirectory.Parse("\uFEFF\"1234567*\",\"Musterstadt\",\"01 WeFü\",\"01 Musterstadt WeFü\"\r\n\"1234568*\",\"\",\"\",\"FR Beispieldorf\"\r\n");
        Assert.Empty(r.Errors);
        var a = r.Entries.Single(e => e.Ric == 1234567);
        Assert.Equal(("Musterstadt", "01 WeFü", "01 Musterstadt WeFü"), (a.Location, a.ShortName, a.Name));
        Assert.Equal("FR Beispieldorf", r.Entries.Single(e => e.Ric == 1234568).Name);
    }
}
