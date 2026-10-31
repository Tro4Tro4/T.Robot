using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TRobot.Application.Maintenance;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class ScenarioCleanupStore(TRobotDbContext db) : IScenarioCleanupStore
{
    public async Task<ScenarioCleanupReport> ExecuteAsync(ScenarioCleanupRequest request, CancellationToken ct = default)
    {
        // Validate even if a caller bypasses the Application facade.
        ScenarioCleanupService.Validate(request);
        await db.Database.OpenConnectionAsync(ct);
        if (await Scalar("PRAGMA foreign_keys", ct) != "1")
            throw new InvalidOperationException("La manutenzione richiede foreign_keys ON.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ev = await db.Events.AsNoTracking().SingleAsync(x => x.Id == request.EventId, ct);
        if (ev.Name != ScenarioCleanupService.AllowedName || ev.Revision != request.Revision)
            throw new InvalidOperationException("Nome o revisione dello scenario diversi da quelli autorizzati.");
        var normalized = request.AdministratorUserName.Trim().ToUpperInvariant();
        var admin = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == normalized, ct);
        if (admin is not { IsEnabled: true, IsSystemAdministrator: true }
            || (admin.LockoutEnabled && admin.LockoutEnd > DateTimeOffset.UtcNow)
            || !await db.EventOrganizers.AnyAsync(x => x.EventId == ev.Id && x.PersonId == admin.PersonId && x.IsActive, ct))
            throw new UnauthorizedAccessException("Amministratore abilitato e assegnato richiesto.");
        if (await db.Set<WhatsAppConnection>().AnyAsync(x => x.EventId == ev.Id && x.Enabled, ct))
            throw new InvalidOperationException("Spostare o disabilitare il collegamento WhatsApp prima della rimozione.");
        await CheckIntegrity(ct);
        var before = await Snapshot(request.EventId, ct);
        if (request.Apply && !string.Equals(request.ExpectedDatabaseFingerprint, before.FullHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Database modificato dopo il dry-run. Ripetere la verifica.");
        if (!request.Apply)
            return new(ev.Id, ev.Revision, false, before.FullHash, before.PreservedHash, before.Counts);

        // FK enforcement stays enabled. Deferral permits the Poll/DecisionOption cycle;
        // every constraint is checked explicitly below and by SQLite at commit.
        await Execute("PRAGMA defer_foreign_keys=ON", ct);
        foreach (var table in before.ScopedTables.Where(x => x != "Events"))
            await Delete(table, "EventId", ev.Id, ct);
        await Delete("Events", "Id", ev.Id, ct);
        await CheckIntegrity(ct);
        var after = await Snapshot(ev.Id, ct);
        if (before.PreservedHash != after.PreservedHash || after.Counts.Values.Any(x => x != 0))
            throw new InvalidOperationException("La verifica di isolamento della cancellazione non e riuscita.");
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return new(ev.Id, ev.Revision, true, before.FullHash, before.PreservedHash, before.Counts);
    }

    private async Task<SnapshotResult> Snapshot(Guid eventId, CancellationToken ct)
    {
        var tables = new List<(string Name, string Schema)>();
        await using (var cmd = Command("SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) tables.Add((reader.GetString(0), reader.GetString(1)));
        var mapped = db.Model.GetEntityTypes().Select(x => x.GetTableName()).ToHashSet(StringComparer.Ordinal);
        if (tables.Any(x => !mapped.Contains(x.Name) && x.Name is not ("__EFMigrationsHistory" or "__EFMigrationsLock")))
            throw new InvalidOperationException("Tabelle non riconosciute: rivedere la manutenzione prima di procedere.");
        var full = new List<string>(); var preserved = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal); var scopedTables = new List<string>();
        foreach (var (name, schema) in tables)
        {
            full.Add(schema); preserved.Add(schema);
            await using var cmd = Command($"SELECT * FROM {Quote(name)}");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var scopeColumn = Array.IndexOf(columns, name == "Events" ? "Id" : "EventId");
            if (scopeColumn >= 0) { scopedTables.Add(name); counts[name] = 0; }
            var rows = new List<string>(); var otherRows = new List<string>();
            while (await reader.ReadAsync(ct))
            {
                var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray();
                var row = JsonSerializer.Serialize(values.Select(value => new object?[]
                    { value?.GetType().FullName, value is byte[] blob ? Convert.ToHexString(blob) : value }));
                rows.Add(row);
                if (scopeColumn >= 0 && Guid.TryParse(Convert.ToString(values[scopeColumn]), out var id) && id == eventId)
                    counts[name]++;
                else otherRows.Add(row);
            }
            full.AddRange(rows.Order(StringComparer.Ordinal).Select(x => name + ":" + x));
            preserved.AddRange(otherRows.Order(StringComparer.Ordinal).Select(x => name + ":" + x));
        }
        return new(Hash(full), Hash(preserved), counts, scopedTables);
    }

    private async Task Delete(string table, string column, Guid id, CancellationToken ct)
    {
        await using var cmd = Command($"DELETE FROM {Quote(table)} WHERE {Quote(column)} = $event");
        var p = cmd.CreateParameter(); p.ParameterName = "$event"; p.Value = id; cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private async Task CheckIntegrity(CancellationToken ct)
    {
        if (await Scalar("PRAGMA integrity_check", ct) != "ok") throw new InvalidOperationException("SQLite integrity_check fallito.");
        await using var cmd = Command("PRAGMA foreign_key_check");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) throw new InvalidOperationException("SQLite foreign_key_check fallito.");
    }
    private async Task<string?> Scalar(string sql, CancellationToken ct)
    { await using var cmd = Command(sql); return Convert.ToString(await cmd.ExecuteScalarAsync(ct)); }
    private async Task Execute(string sql, CancellationToken ct)
    { await using var cmd = Command(sql); await cmd.ExecuteNonQueryAsync(ct); }
    private DbCommand Command(string sql)
    {
        var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = sql;
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction(); return cmd;
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static string Hash(IEnumerable<string> lines) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(lines))));
    private sealed record SnapshotResult(string FullHash, string PreservedHash, Dictionary<string, int> Counts, List<string> ScopedTables);
}
