using System.Text;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    public async Task<WhatsAppRubricaResult> AssociateFromRubricaAsync(Guid actorId, Guid eventId, int revision,
        bool confirmed, CancellationToken ct = default)
    {
        await RequireAdmin(actorId, eventId, ct);
        Confirm(confirmed);
        var connection = await RequireConnection(eventId, ct);
        LocalOperations.Revision(connection, revision);
        var members = await gateway.MembersAsync(connection.GroupId!, ct);
        return await ApplyRubricaAsync(actorId, eventId, connection.GroupId!, revision, members, ct);
    }

    private async Task<WhatsAppRubricaResult> ApplyRubricaAsync(Guid actorId, Guid eventId, string groupId,
        int revision, IReadOnlyList<GatewayMember> members, CancellationToken ct) =>
        await operations.ChangeAsync(actorId, eventId, async token =>
        {
            await RequireAdmin(actorId, eventId, token);
            var connection = await RequireConnection(eventId, token);
            LocalOperations.Revision(connection, revision);
            if (connection.GroupId != groupId)
                throw new LocalConflictException("Il gruppo e' cambiato. Ricarica la pagina.");

            var participants = await store.ParticipantsAsync(eventId, token);
            var contacts = await identities.ActiveContactsAsync(token);
            var aliases = await store.ListAsync<WhatsAppIdentityAlias>(eventId, token);
            // Un nome puo' avere piu' numeri e un numero piu' nomi. Il confronto avviene
            // sempre sulla coppia completa: nome normalizzato e impronta del numero.
            var pairs = contacts.Select(x => (NameKey: NameKey(x.Name), x.SenderHash)).ToHashSet();
            var byName = participants.GroupBy(x => NameKey(x.Name), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var byHash = aliases.GroupBy(x => x.SenderHash, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            var associated = 0; var alreadyAssociated = 0; var notRecognized = 0;
            var ambiguous = 0; var notParticipant = 0; var conflicts = 0;
            var changed = false; var audited = new HashSet<Guid>();
            foreach (var member in members)
            {
                var nameKey = NameKey(member.Label);
                var hashes = MemberHashes(member);
                if (hashes.Length == 0 || !hashes.Any(hash => pairs.Contains((nameKey, hash))))
                {
                    notRecognized++;
                    continue;
                }
                if (!byName.TryGetValue(nameKey, out var targets) || targets.Length == 0)
                {
                    notParticipant++;
                    continue;
                }
                if (targets.Length > 1)
                {
                    ambiguous++;
                    continue;
                }
                var target = targets[0];
                var rows = hashes.Select(hash => byHash.TryGetValue(hash, out var found) ? found[0] : null).ToArray();
                if (rows.Any(row => row is not null && row.PersonId != target.Id))
                {
                    // Non sovrascrivere mai un'associazione esistente: la rubrica resta un aiuto.
                    conflicts++;
                    continue;
                }
                var pending = hashes.Select((hash, index) => (Hash: hash, Row: rows[index]))
                    .Where(x => x.Row is null || !x.Row.IsActive).ToArray();
                if (pending.Length == 0)
                {
                    alreadyAssociated++;
                    continue;
                }
                // ListAsync legge gli alias senza tracciamento: prima di riattivarne uno va
                // ricaricata l'entita' tracciata, altrimenti la modifica non viene salvata.
                var reactivating = new List<WhatsAppIdentityAlias>();
                var stale = false;
                foreach (var row in pending.Where(x => x.Row is not null).Select(x => x.Row!))
                {
                    var tracked = await store.FindAsync<WhatsAppIdentityAlias>(eventId, row.Id, token);
                    if (tracked is null || tracked.PersonId != target.Id) { stale = true; break; }
                    reactivating.Add(tracked);
                }
                if (stale)
                {
                    conflicts++;
                    continue;
                }
                foreach (var row in reactivating)
                {
                    row.SetActive(true);
                    byHash[row.SenderHash] = [row];
                }
                foreach (var (hash, row) in pending.Where(x => x.Row is null))
                {
                    var added = new WhatsAppIdentityAlias(eventId, hash, target.Id);
                    store.Add(added);
                    byHash[hash] = [added];
                }
                associated++; changed = true;
                if (audited.Add(target.Id))
                    operations.Audit(eventId, actorId, target.Id, "WhatsApp", "IdentityMapped");
            }
            if (changed) await store.SaveAsync(token);
            return new WhatsAppRubricaResult(associated, alreadyAssociated, notRecognized, ambiguous,
                notParticipant, conflicts);
        }, ct);

    private static IReadOnlyList<WhatsAppRubricaEntry> RubricaEntries(IReadOnlyList<LocalPerson> participants,
        IReadOnlyList<WhatsAppContactPair> contacts)
    {
        var eventNames = participants.Select(x => NameKey(x.Name)).ToHashSet(StringComparer.Ordinal);
        return contacts
            .Select(x => (x.Name, Key: NameKey(x.Name), x.SenderHash))
            .Where(x => x.Key.Length > 0 && eventNames.Contains(x.Key))
            .DistinctBy(x => (x.Key, x.SenderHash))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new WhatsAppRubricaEntry(
                group.Select(x => x.Name.Trim()).Where(x => x.Length > 0)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First(),
                group.Select(x => x.SenderHash).Distinct(StringComparer.Ordinal).Count()))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] MemberHashes(GatewayMember member) =>
        new[] { member.Id }.Concat(member.AlternateIds)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 200
                && (x.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) || x.EndsWith("@lid", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Select(x => options.Fingerprint("sender", x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var normalized = name.Normalize(NormalizationForm.FormC);
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }
}
