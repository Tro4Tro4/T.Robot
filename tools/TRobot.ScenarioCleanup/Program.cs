using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Maintenance;
using TRobot.Infrastructure.Persistence;

if (args.Length is not (3 or 6) || !int.TryParse(args[2], out var revision))
{
    Console.Error.WriteLine("Uso: TRobot.ScenarioCleanup <db-esistente> <admin-user-name> <revisione> [<fingerprint-dry-run> DELETE:ce014f6d-1a90-4388-b187-771235bcb918 <backup-sha256>]");
    return 2;
}
try
{
    var path = Path.GetFullPath(args[0]);
    if (!File.Exists(path)) throw new InvalidOperationException("Il DB deve esistere.");
    var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString();
    await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connectionString).Options);
    var request = new ScenarioCleanupRequest(args[1], ScenarioCleanupService.AllowedEventId, revision,
        args.Length == 6, args.ElementAtOrDefault(3), args.ElementAtOrDefault(4), args.ElementAtOrDefault(5));
    var report = await new ScenarioCleanupService(new ScenarioCleanupStore(db)).ExecuteAsync(request);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Manutenzione interrotta: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
