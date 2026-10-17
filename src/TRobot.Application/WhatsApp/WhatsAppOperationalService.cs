using System.Security.Cryptography;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    private const string OperationalUnavailable = "⏳ Operazione non disponibile.\nControlla il riferimento e i requisiti con !aiuto, poi invia una nuova richiesta.";

    // Shares the authorized transaction with command receipt and audit persistence.
    private async Task<(string? Reply, bool Success)> OperationalReplyAsync(WhatsAppConnection connection,
        EventParticipant participant, string[] parts, DateTime now, CancellationToken ct)
    {
        var eventId = connection.EventId; var personId = participant.PersonId;
        if (parts.Length != 2) return ($"ℹ️ Sintassi:\n{parts[0].ToLowerInvariant()} {(parts[0].Equals("!conferma", StringComparison.OrdinalIgnoreCase) ? "<codice>" : "<rif>")}", false);
        var child = (await store.ListAsync<HouseholdMember>(eventId, ct))
            .Any(x => x.IsActive && x.PersonId == personId && x.Kind == HouseholdMemberKind.Child);
        if (participant.Status != ParticipationStatus.Confirmed || child) return (OperationalUnavailable, false);
        var command = parts[0].ToLowerInvariant();
        var proposals = await store.ListAsync<WhatsAppOperationalProposal>(eventId, ct);
        WhatsAppOperationalProposal? proposal = null;
        SimulationAction action; ILocalRecord? target;
        if (command == "!conferma")
        {
            if (parts[1].Length != 32 || !parts[1].All(Uri.IsHexDigit)) return (OperationalUnavailable, false);
            var codeHash = options.Fingerprint("operation-code", parts[1].ToLowerInvariant());
            proposal = proposals.SingleOrDefault(x => x.CodeHash == codeHash);
            if (proposal is null || proposal.PersonId != personId || proposal.ActorId != connection.ActorId
                || proposal.GroupId != connection.GroupId || proposal.ConnectionRevision != connection.Revision
                || proposal.ConsumedAtUtc.HasValue || now < proposal.CreatedAtUtc || now >= proposal.ExpiresAtUtc)
                return (OperationalUnavailable, false);
            action = proposal.Action;
            target = action == SimulationAction.BringMenu
                ? (ILocalRecord?)await store.FindAsync<MenuItem>(eventId, proposal.TargetId, ct)
                : await store.FindAsync<OrganizationTask>(eventId, proposal.TargetId, ct);
            if (target is null || target.Revision != proposal.TargetRevision) return (OperationalUnavailable, false);
        }
        else
        {
            action = command switch { "!porto" => SimulationAction.BringMenu, "!prendo" => SimulationAction.ClaimTask, _ => SimulationAction.CompleteTask };
            if (proposals.Count(x => x.PersonId == personId && !x.ConsumedAtUtc.HasValue && x.ExpiresAtUtc > now) >= 3)
                return ("⏳ Hai già tre richieste attive.\nConfermane una oppure attendi cinque minuti prima di riprovare.", false);
            try
            {
                var resolved = action == SimulationAction.BringMenu
                    ? (ILocalRecord)SimulationReferences.Resolve(await store.ListAsync<MenuItem>(eventId, ct), parts[1])
                    : SimulationReferences.Resolve(await store.ListAsync<OrganizationTask>(eventId, ct), parts[1]);
                target = action == SimulationAction.BringMenu
                    ? (ILocalRecord?)await store.FindAsync<MenuItem>(eventId, resolved.Id, ct)
                    : await store.FindAsync<OrganizationTask>(eventId, resolved.Id, ct);
            }
            catch (LocalValidationException) { return (OperationalUnavailable, false); }
        }
        if (target is MenuItem item)
        {
            if (item.Availability is MenuAvailability.Ready or MenuAvailability.Cancelled
                || (item.ResponsiblePersonId.HasValue && item.ResponsiblePersonId != personId)
                || (item.ResponsiblePersonId is null && item.Availability != MenuAvailability.Planned)) return (OperationalUnavailable, false);
        }
        else if (target is OrganizationTask task)
        {
            if (task.Status is not (OrganizationTaskStatus.Todo or OrganizationTaskStatus.InProgress)
                || (task.AssigneeId.HasValue && task.AssigneeId != personId)
                || (action == SimulationAction.CompleteTask && task.AssigneeId != personId)) return (OperationalUnavailable, false);
        }
        else return (OperationalUnavailable, false);
        if (proposal is null)
        {
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var pending = new WhatsAppOperationalProposal(eventId, personId, connection.ActorId!.Value, connection.GroupId!,
                connection.Revision, action, target.Id, target.Revision, options.Fingerprint("operation-code", code), now);
            store.Add(pending); operations.Audit(eventId, pending.ActorId, pending.Id, "WhatsApp", "OperationProposed");
            return ($"🙋 *Proposta di impegno*\nAzione: {command} · Rif. {SimulationReferences.Short(target.Id)}\n\nNessuna modifica prima della conferma.\nSolo tu puoi confermare, entro cinque minuti, nello stesso gruppo.\n\n👉 Copia e invia:\n!conferma {code}\n\nIo preparo la lista, a te l’ultimo sì. ✅", false);
        }
        if (target is MenuItem menu)
            menu.Update(menu.Name, menu.Category, menu.Quantity, menu.Unit, personId, MenuAvailability.Assigned, menu.Notes);
        else if (target is OrganizationTask todo)
            todo.Update(todo.Title, todo.Description, todo.Priority,
                action == SimulationAction.CompleteTask ? OrganizationTaskStatus.Completed : OrganizationTaskStatus.InProgress, personId, todo.DueAtUtc);
        var tracked = (await store.FindAsync<WhatsAppOperationalProposal>(eventId, proposal.Id, ct))!;
        tracked.Consume(now); operations.Audit(eventId, proposal.ActorId, target.Id, "WhatsApp", action.ToString());
        return (null, true);
    }
}
