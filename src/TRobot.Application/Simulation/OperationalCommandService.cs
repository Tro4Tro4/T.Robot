using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Simulation;

public sealed class OperationalCommandService(ILocalStore store, IAttendanceStore attendanceStore,
    LocalOperations operations, TimeProvider clock)
{
    public Task<SimulationCommandResult> ExecuteAsync(Guid actorId, SimulatedMessage message,
        SimulationAction action, string reference, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, message.EventId, async token =>
        {
            if (!confirmed) throw new LocalValidationException("Conferma esplicitamente l'operazione nel simulatore.");
            if (string.IsNullOrWhiteSpace(message.MessageId) || message.MessageId.Trim().Length > 100
                || string.IsNullOrWhiteSpace(message.SenderId) || message.SenderId.Trim().Length > 200)
                throw new LocalValidationException("Mittente e identificativo messaggio validi sono obbligatori.");
            if (!Enum.IsDefined(action)) throw new LocalValidationException("Azione non supportata.");
            var person = await attendanceStore.FindParticipantAsync(message.EventId, message.SenderId.Trim(), token)
                ?? throw new LocalValidationException("Il mittente non è un partecipante di questo evento.");
            var messageId = message.MessageId.Trim();
            var operationId = SimulationReferences.MessageId(message.EventId, messageId);
            if (await attendanceStore.FindReceiptAsync(message.EventId, messageId, token) is not null
                || await store.FindAsync<PollVoteReceipt>(message.EventId, operationId, token) is not null
                || await store.FindAsync<QuizAnswer>(message.EventId, operationId, token) is not null)
                throw new LocalConflictException("Identificativo messaggio già utilizzato per un'altra operazione.");
            var target = action == SimulationAction.BringMenu
                ? (ILocalRecord)SimulationReferences.Resolve(await store.ListAsync<MenuItem>(message.EventId, token), reference)
                : SimulationReferences.Resolve(await store.ListAsync<OrganizationTask>(message.EventId, token), reference);
            var receipt = await store.FindAsync<SimulationActionReceipt>(message.EventId, operationId, token);
            if (receipt is not null)
            {
                if (receipt.ActorId != actorId || receipt.PersonId != person.PersonId || receipt.Action != action || receipt.TargetId != target.Id)
                    throw new LocalConflictException("Identificativo messaggio già utilizzato con dati diversi.");
                return new SimulationCommandResult("Messaggio già elaborato: nessuna modifica duplicata.", true);
            }
            var child = (await store.ListAsync<HouseholdMember>(message.EventId, token))
                .Any(x => x.IsActive && x.PersonId == person.PersonId && x.Kind == HouseholdMemberKind.Child);
            if (person.Status != ParticipationStatus.Confirmed || child)
                throw new LocalValidationException("Può assumere impegni solo un adulto confermato di questo evento.");
            if (action == SimulationAction.BringMenu)
            {
                var item = await store.FindAsync<MenuItem>(message.EventId, target.Id, token) ?? throw new LocalAccessException();
                if (item.Availability is MenuAvailability.Ready or MenuAvailability.Cancelled
                    || (item.ResponsiblePersonId.HasValue && item.ResponsiblePersonId != person.PersonId)
                    || (item.ResponsiblePersonId is null && item.Availability != MenuAvailability.Planned))
                    throw new LocalValidationException("Voce non disponibile: non puoi sostituire altri incaricati né modificare voci pronte o annullate.");
                item.Update(item.Name, item.Category, item.Quantity, item.Unit, person.PersonId, MenuAvailability.Assigned, item.Notes);
            }
            else
            {
                var task = await store.FindAsync<OrganizationTask>(message.EventId, target.Id, token) ?? throw new LocalAccessException();
                if (task.Status is not (OrganizationTaskStatus.Todo or OrganizationTaskStatus.InProgress)
                    || (task.AssigneeId.HasValue && task.AssigneeId != person.PersonId)
                    || (action == SimulationAction.CompleteTask && task.AssigneeId != person.PersonId))
                    throw new LocalValidationException("Attività non disponibile: deve essere aperta e assegnata a te per completarla; non puoi sostituire altri incaricati.");
                task.Update(task.Title, task.Description, task.Priority,
                    action == SimulationAction.CompleteTask ? OrganizationTaskStatus.Completed : OrganizationTaskStatus.InProgress,
                    person.PersonId, task.DueAtUtc);
            }
            store.Add(new SimulationActionReceipt(message.EventId, operationId, actorId, person.PersonId, action, target.Id, clock.GetUtcNow().UtcDateTime));
            operations.Audit(message.EventId, actorId, target.Id, "Simulation", action.ToString());
            await store.SaveAsync(token);
            return new SimulationCommandResult(action == SimulationAction.BringMenu ? "Impegno menu registrato."
                : action == SimulationAction.ClaimTask ? "Attività presa in carico." : "Attività completata.");
        }, ct);
}
