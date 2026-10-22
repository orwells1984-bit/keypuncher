using System.Globalization;
using System.Text.Json;
using Keypuncher.Core;
using Microsoft.Data.Sqlite;

namespace Keypuncher.Storage;

/// <summary>
/// One world in one SQLite file (so a world per child, or an archived world, is just another file).
/// The app saves every couple of seconds while something changed and on close; each save is one
/// transaction, so a crash or a pulled plug loses at most the last few seconds.
/// </summary>
public sealed class WorldStore : IDisposable
{
    private const int SchemaVersion = 1;
    private readonly SqliteConnection _db;

    public WorldStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS World (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Entity (
                Id INTEGER PRIMARY KEY,
                Baked INTEGER NOT NULL,
                Recipe TEXT NOT NULL,
                X REAL NOT NULL, Y REAL NOT NULL,
                Born REAL NOT NULL, Scale REAL NOT NULL, Phase REAL NOT NULL, Energy REAL NOT NULL);
            """);
        Exec($"INSERT OR IGNORE INTO World VALUES ('schema', '{SchemaVersion}');");
    }

    /// <summary>Replaces the saved world with the given one.</summary>
    public void Save(World world)
    {
        using var tx = _db.BeginTransaction();
        Exec("DELETE FROM Entity;", tx);
        using (var insert = _db.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO Entity VALUES ($id, $baked, $recipe, $x, $y, $born, $scale, $phase, $energy);";
            var p = new[] { "$id", "$baked", "$recipe", "$x", "$y", "$born", "$scale", "$phase", "$energy" }
                .Select(n => insert.Parameters.Add(n, SqliteType.Text)).ToArray();
            void Write(Entity e, bool baked)
            {
                p[0].Value = e.Id;
                p[1].Value = baked ? 1 : 0;
                p[2].Value = JsonSerializer.Serialize(e.Recipe);
                p[3].Value = e.X;
                p[4].Value = e.Y;
                p[5].Value = e.Born;
                p[6].Value = e.Scale;
                p[7].Value = e.Phase;
                p[8].Value = e.Energy;
                insert.ExecuteNonQuery();
            }
            foreach (var e in world.Baked) Write(e, true);
            foreach (var e in world.Live.Where(e => e.IsPersistent && e.Recipe is not null)) Write(e, false);
        }
        Exec($"INSERT OR REPLACE INTO World VALUES ('time', '{world.Time.ToString("R", CultureInfo.InvariantCulture)}');", tx);
        tx.Commit();
        world.MarkSaved();
    }

    /// <summary>Loads the saved world into <paramref name="world"/>; an empty file gives an empty world.</summary>
    public void Load(World world)
    {
        double time = 0;
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT Value FROM World WHERE Key = 'time';";
            if (cmd.ExecuteScalar() is string s) time = double.Parse(s, CultureInfo.InvariantCulture);
        }

        var live = new List<Entity>();
        var baked = new List<Entity>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Baked, Recipe, X, Y, Born, Scale, Phase, Energy FROM Entity ORDER BY Born, Id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                Recipe? recipe;
                try { recipe = JsonSerializer.Deserialize<Recipe>(r.GetString(2)); }
                catch (JsonException) { continue; }
                if (recipe is null) continue;

                var e = new Entity
                {
                    Id = r.GetInt64(0),
                    Role = EntityRole.Thing,
                    Recipe = recipe,
                    X = r.GetDouble(3),
                    Y = r.GetDouble(4),
                    Born = r.GetDouble(5),
                    Scale = r.GetDouble(6),
                    Phase = r.GetDouble(7),
                    Energy = r.GetDouble(8),
                };
                (r.GetInt64(1) == 1 ? baked : live).Add(e);
            }
        }
        world.Load(time, live, baked);
    }

    public void Dispose() => _db.Dispose();

    private void Exec(string sql, SqliteTransaction? tx = null)
    {
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
