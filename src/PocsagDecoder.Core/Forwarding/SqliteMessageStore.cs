using Microsoft.Data.Sqlite;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>Speichert Telegramme in einer SQLite-Datei (Tabelle „telegramme“).</summary>
public static class SqliteMessageStore
{
    private static readonly object Lock = new();

    public static void Insert(string path, PocsagMessage m, string ruleName)
    {
        lock (Lock)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            db.Open();
            using (var create = db.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS telegramme (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        zeit TEXT NOT NULL,
                        ric TEXT NOT NULL,
                        subric TEXT NOT NULL,
                        funktion INTEGER NOT NULL,
                        baud INTEGER NOT NULL,
                        typ TEXT NOT NULL,
                        text TEXT NOT NULL,
                        bezeichnung TEXT NOT NULL,
                        kurz TEXT NOT NULL,
                        ort TEXT NOT NULL,
                        subric_bedeutung TEXT NOT NULL,
                        ereignis TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ix_telegramme_ric ON telegramme(ric);
                    CREATE INDEX IF NOT EXISTS ix_telegramme_zeit ON telegramme(zeit);
                    """;
                create.ExecuteNonQuery();
            }
            using var insert = db.CreateCommand();
            insert.CommandText = """
                INSERT INTO telegramme (zeit, ric, subric, funktion, baud, typ, text, bezeichnung, kurz, ort, subric_bedeutung, ereignis)
                VALUES ($zeit, $ric, $subric, $funktion, $baud, $typ, $text, $bez, $kurz, $ort, $subbed, $ereignis)
                """;
            insert.Parameters.AddWithValue("$zeit", m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            insert.Parameters.AddWithValue("$ric", m.RicFormatted);
            insert.Parameters.AddWithValue("$subric", m.SubRic.ToString());
            insert.Parameters.AddWithValue("$funktion", m.Function);
            insert.Parameters.AddWithValue("$baud", m.Baud);
            insert.Parameters.AddWithValue("$typ", m.ContentType.ToString());
            insert.Parameters.AddWithValue("$text", m.Text ?? "");
            insert.Parameters.AddWithValue("$bez", m.RicDescription ?? "");
            insert.Parameters.AddWithValue("$kurz", m.RicShortName ?? "");
            insert.Parameters.AddWithValue("$ort", m.RicLocation ?? "");
            insert.Parameters.AddWithValue("$subbed", m.SubRicDescription ?? "");
            insert.Parameters.AddWithValue("$ereignis", ruleName ?? "");
            insert.ExecuteNonQuery();
        }
    }
}
