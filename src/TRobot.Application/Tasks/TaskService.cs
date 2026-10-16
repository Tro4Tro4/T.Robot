using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Tasks;

public sealed record TaskEdit(string Title, string? Description, TaskPriority Priority,
    OrganizationTaskStatus Status, Guid? AssigneeId, DateTime? DueLocal, int Revision, bool ConfirmCancellation, Guid OperationId);
public sealed record TaskOverview(Event Event, IReadOnlyList<OrganizationTask> Tasks, IReadOnlyList<LocalPerson> People);

public sealed class TaskService(ILocalStore store, LocalOperations operations)
{
    public async Task<TaskOverview> ListAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        var tasks = await operations.ReadAsync<OrganizationTask>(actorId, eventId, ct);
        return new TaskOverview(await store.EventAsync(eventId, ct) ?? throw new LocalAccessException(),
            tasks.OrderBy(x => x.Status).ThenByDescending(x => x.Priority).ThenBy(x => x.DueAtUtc ?? DateTime.MaxValue).ThenBy(x => x.Title).ToArray(),
            await store.PeopleAsync(eventId, ct));
    }
    public Task<Guid> CreateAsync(Guid actorId, Guid eventId, TaskEdit input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            if (input.Status != OrganizationTaskStatus.Todo) throw new LocalValidationException("Una nuova attività deve essere da iniziare.");
            if (input.OperationId == Guid.Empty) throw new LocalValidationException("Identificativo operazione obbligatorio.");
            await operations.CheckPersonAsync(eventId, input.AssigneeId, token);
            var task = new OrganizationTask(eventId, input.Title, input.Description, input.Priority, input.AssigneeId,
                await operations.ToUtcAsync(eventId, input.DueLocal, token), input.OperationId);
            var existing = await store.FindAsync<OrganizationTask>(eventId, task.Id, token);
            if (existing is not null)
            {
                if (existing.Revision == 0 && existing.Title == task.Title && existing.Description == task.Description
                    && existing.Priority == task.Priority && existing.AssigneeId == task.AssigneeId && existing.DueAtUtc == task.DueAtUtc)
                    return existing.Id;
                throw new LocalConflictException("Identificativo già usato con dati diversi o attività modificata.");
            }
            store.Add(task); operations.Audit(eventId, actorId, task.Id, "Tasks", "Created");
            await store.SaveAsync(token); return task.Id;
        }, ct);
    public Task<bool> UpdateAsync(Guid actorId, Guid eventId, Guid id, TaskEdit input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var task = await store.FindAsync<OrganizationTask>(eventId, id, token) ?? throw new LocalAccessException();
            LocalOperations.Revision(task, input.Revision);
            if (input.Status == OrganizationTaskStatus.Cancelled && task.Status != input.Status && !input.ConfirmCancellation)
                throw new LocalValidationException("Conferma esplicitamente l'annullamento dell'attività.");
            await operations.CheckPersonAsync(eventId, input.AssigneeId, token);
            var changed = task.Update(input.Title, input.Description, input.Priority, input.Status, input.AssigneeId,
                await operations.ToUtcAsync(eventId, input.DueLocal, token));
            if (changed) { operations.Audit(eventId, actorId, task.Id, "Tasks", "Updated"); await store.SaveAsync(token); }
            return changed;
        }, ct);
}
