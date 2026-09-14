// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Startup;

/// <summary>
/// Part 1, <c>SELECT * FROM ConferenceSettings</c>. The table was dropped by the
/// <c>20260910103810_DropDeadConferenceSettings</c> migration, and the live
/// value of <c>WatchOnlineLink</c> was supposed to have been carried over into
/// <c>LinkWatches</c>.
/// <para>
/// The real database is opened <b>read-only</b> (<c>Mode=ReadOnly</c>): this
/// SELECT is the check, but nothing may be written to it.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class ConferenceSettingsTests
{
    private readonly AppFixture _app;

    public ConferenceSettingsTests(AppFixture app) => _app = app;

    [Fact]
    public async Task В_новата_база_таблицата_вече_не_съществува()
    {
        var tables = await _app.Db.ReadAsync(async db =>
        {
            var names = new List<string>();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));

            return names;
        });

        Assert.DoesNotContain("ConferenceSettings", tables);
        Assert.Contains("LinkWatches", tables);
    }

    [Fact]
    public void В_истинската_база_няма_непренесен_линк()
    {
        var live = TestPaths.LiveDbFile;

        Assert.True(File.Exists(live),
            $"Няма {live} — проверката за пренесения WatchOnlineLink не може да се направи.");

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = live,
                Mode = SqliteOpenMode.ReadOnly       // the real database is not touched
            }.ToString());

        connection.Open();

        var leftover = QueryStrings(connection,
            "SELECT WatchOnlineLink FROM ConferenceSettings", tolerateMissingTable: true);

        var carried = QueryStrings(connection,
            "SELECT WatchOnlineLink FROM LinkWatches", tolerateMissingTable: false);

        // Every value left in the old table has to be present in the new one too.
        foreach (var value in leftover.Where(v => !string.IsNullOrWhiteSpace(v) && v != "#"))
        {
            Assert.Contains(value, carried);
        }

        // And the other way round: the migration only drops the table, it carries
        // no data. If there was ever a row in it, the value must have been carried
        // over beforehand. What is guarded here is that nothing is lost now.
        Assert.True(leftover.Count == 0 || carried.Count > 0,
            "В ConferenceSettings има ред, а LinkWatches е празна — линкът е загубен.");
    }

    private static List<string> QueryStrings(SqliteConnection connection, string sql, bool tolerateMissingTable)
    {
        var values = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            using var reader = command.ExecuteReader();
            while (reader.Read())
                values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }
        catch (SqliteException) when (tolerateMissingTable)
        {
            // The table is gone, which is the desired state.
        }

        return values;
    }
}
