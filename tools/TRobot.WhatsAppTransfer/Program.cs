using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Local;
using TRobot.Application.Maintenance;
using TRobot.Application.WhatsApp;
using TRobot.Infrastructure.Persistence;
using TRobot.Infrastructure.WhatsApp;

// One operation authorized by the operator, with two explicit identity confirmations.
if (args.Length is not (2 or 5) || args.Length == 5 && (args[2] != "--apply" || args[3] != "--confirmed-dan-tro" || args[4].Length != 64 || !args[4].All(Uri.IsHexDigit)))
{
    Console.Error.WriteLine("Uso: <db> <admin> [--apply --confirmed-dan-tro <backup-sha256>]; gateway.json tramite LoadCredential systemd.");
    return 2;
}
var phase = "configurazione";
try
{
    var sourceId = ScenarioCleanupService.AllowedEventId;
    var targetId = Guid.Parse("fc3dfb11-fbcf-46e9-b677-9521e1c07f03");
    var mapping = new[] {
        (Source: Guid.Parse("683765c0-3dc1-4362-8fe4-0bc27d735f42"), Target: Guid.Parse("94940dab-8c0c-48b5-8d17-7060a25a79d4"), Name: "Dan"),
        (Source: Guid.Parse("84c2bbc0-945b-48c2-b517-84cabbc33375"), Target: Guid.Parse("db70be70-6913-40f0-9ac3-885332f38784"), Name: "Tro")
    };
    var credentialDirectory = Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY") ?? throw new InvalidOperationException();
    using var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(credentialDirectory, "gateway.json")));
    var options = new WhatsAppGatewayOptions(true, "http://127.0.0.1:5240", config.RootElement.GetProperty("WhatsAppGateway").GetProperty("Token").GetString()!);
    options.RequireEnabled();
    if (!File.Exists(args[0])) throw new InvalidOperationException();
    var connection = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(args[0]), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false };
    await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection.ToString()).Options);
    phase = "amministratore";
    var account = await db.Users.SingleAsync(u => u.UserName == args[1] && u.IsEnabled && u.IsSystemAdministrator);
    var store = new LocalStore(db, TimeProvider.System);
    using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    var gateway = new WhatsAppGatewayClient(http, options);
    var service = new WhatsAppService(store, new WhatsAppStore(db), new LocalOperations(store, TimeProvider.System), gateway, options, TimeProvider.System);
    phase = "eventi";
    var source = await service.ReadAsync(account.PersonId, sourceId);
    var target = await service.ReadAsync(account.PersonId, targetId);
    if (!source.CanManageDevice || !target.CanManageDevice || source.Event.Name != ScenarioCleanupService.AllowedName || target.Event.Name != "Capodanno 2027") throw new InvalidOperationException();
    phase = "collegamenti";
    if (source.Connection?.Enabled != true || target.Connection?.Enabled == true || target.Aliases.Count is not (0 or 4) || source.Connection.GroupId is not { } group) throw new InvalidOperationException();
    phase = "stato gateway";
    var health = await service.HealthAsync(account.PersonId, sourceId);
    if (health.Status != "connected" || health.ActiveGroupId != group) throw new InvalidOperationException();
    phase = "associazioni sorgente";
    var aliases = source.Aliases.Where(a => a.IsActive).ToArray();
    if (aliases.Length != 4 || aliases.Any(a => !mapping.Any(m => m.Source == a.PersonId))) throw new InvalidOperationException();
    var members = await service.MembersAsync(account.PersonId, sourceId);
    var selected = new List<(Guid Person, string Member, HashSet<string> Hashes)>();
    foreach (var item in mapping)
    {
        phase = "anagrafica " + item.Name;
        if (source.Participants.Single(p => p.Id == item.Source).Name != item.Name || target.Participants.Single(p => p.Id == item.Target).Name != item.Name) throw new InvalidOperationException();
        var expected = aliases.Where(a => a.PersonId == item.Source).Select(a => a.SenderHash).ToHashSet(StringComparer.Ordinal);
        phase = "associazioni destinazione " + item.Name;
        if (target.Aliases.Count != 0 && !expected.SetEquals(target.Aliases.Where(a => a.IsActive && a.PersonId == item.Target).Select(a => a.SenderHash))) throw new InvalidOperationException();
        var matches = members.Where(m => new[] { m.Id }.Concat(m.AlternateIds).Any(id => expected.Contains(options.Fingerprint("sender", id)))).ToArray();
        phase = "membro " + item.Name;
        if (matches.Length != 1 || expected.Count != 2) throw new InvalidOperationException();
        var hashes = new[] { matches[0].Id }.Concat(matches[0].AlternateIds).Select(id => options.Fingerprint("sender", id)).ToHashSet(StringComparer.Ordinal);
        phase = "alias " + item.Name;
        if (!expected.SetEquals(hashes) || selected.Any(s => s.Member == matches[0].Id)) throw new InvalidOperationException();
        selected.Add((item.Target, matches[0].Id, expected));
    }
    Console.WriteLine("Preflight valido: gruppo originale e due associazioni confermate corrispondono ai membri attuali.");
    if (args.Length == 2) return 0;
    // Run only while Web and automatic backup are stopped, to serialize device changes.
    phase = "disconnessione sorgente";
    await service.DisconnectAsync(account.PersonId, sourceId, confirmed: true);
    phase = "connessione destinazione";
    await service.ConnectAsync(account.PersonId, targetId, confirmed: true);
    for (var attempt = 0; attempt < 45; attempt++)
    {
        health = await service.HealthAsync(account.PersonId, targetId);
        if (health.Status == "connected") break;
        if (health.Status is "qr" or "error") throw new InvalidOperationException();
        await Task.Delay(1000);
    }
    if (health.Status != "connected") throw new InvalidOperationException();
    target = await service.ReadAsync(account.PersonId, targetId);
    phase = "attivazione gruppo destinazione";
    await service.SelectGroupAsync(account.PersonId, targetId, group, target.Connection?.Revision ?? 0, confirmed: true);
    target = await service.ReadAsync(account.PersonId, targetId);
    phase = "associazione destinazione";
    foreach (var selectedPerson in selected)
        await service.MapAsync(account.PersonId, targetId, selectedPerson.Member, selectedPerson.Person, target.Connection!.Revision, confirmed: true);
    phase = "verifica finale";
    source = await service.ReadAsync(account.PersonId, sourceId);
    target = await service.ReadAsync(account.PersonId, targetId);
    health = await service.HealthAsync(account.PersonId, targetId);
    if (source.Connection?.Enabled != false || target.Connection?.Enabled != true || health.Status != "connected" || health.ActiveGroupId != group) throw new InvalidOperationException();
    foreach (var person in selected)
        if (!person.Hashes.SetEquals(target.Aliases.Where(a => a.IsActive && a.PersonId == person.Person).Select(a => a.SenderHash))) throw new InvalidOperationException();
    Console.WriteLine("Trasferimento verificato: Capodanno 2027 attivo, gruppo originale e associazioni Dan/Tro conservati. Nessun messaggio inviato.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Trasferimento interrotto nella fase " + phase + ": " + ex.GetType().Name + ". Verificare lo stato prima di ripetere; nessuna riattivazione automatica dello scenario.");
    return 1;
}
