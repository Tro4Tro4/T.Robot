namespace TRobot.Application.Maintenance;

public sealed record ScenarioCleanupRequest(string AdministratorUserName, Guid EventId, int Revision,
    bool Apply = false, string? ExpectedDatabaseFingerprint = null, string? Confirmation = null,
    string? BackupSha256 = null);

public sealed record ScenarioCleanupReport(Guid EventId, int Revision, bool Applied,
    string DatabaseFingerprint, string PreservedRowsFingerprint, IReadOnlyDictionary<string, int> RemovedRows);

public interface IScenarioCleanupStore
{
    Task<ScenarioCleanupReport> ExecuteAsync(ScenarioCleanupRequest request, CancellationToken ct = default);
}

// Deliberately a one-scenario maintenance operation, not a general event deletion API.
public sealed class ScenarioCleanupService(IScenarioCleanupStore store)
{
    public static readonly Guid AllowedEventId = Guid.Parse("ce014f6d-1a90-4388-b187-771235bcb918");
    public const string AllowedName = "SIMULAZIONE \u2014 Capodanno al Casale, 20 persone";

    public Task<ScenarioCleanupReport> ExecuteAsync(ScenarioCleanupRequest request, CancellationToken ct = default)
    {
        Validate(request);
        return store.ExecuteAsync(request, ct);
    }

    public static void Validate(ScenarioCleanupRequest request)
    {
        if (request.EventId != AllowedEventId || request.Revision < 0 || string.IsNullOrWhiteSpace(request.AdministratorUserName))
            throw new InvalidOperationException("Ambito della manutenzione non autorizzato.");
        if (request.Apply && (request.Confirmation != $"DELETE:{AllowedEventId:D}"
            || !IsHash(request.ExpectedDatabaseFingerprint) || !IsHash(request.BackupSha256)))
            throw new InvalidOperationException("Servono conferma esplicita, fingerprint dry-run e SHA256 del backup privato.");
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
