using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Tasks;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class TaskTests
{
    private static TaskEdit Input(string title = "Preparare tavoli", int revision = 0,
        OrganizationTaskStatus status = OrganizationTaskStatus.Todo, bool confirm = false, Guid? assignee = null,
        DateTime? due = null) => new(title, "Descrizione\r\nnormalizzata", TaskPriority.Normal, status, assignee, due, revision, confirm, Guid.NewGuid());

    [Fact]
    public void DomainNormalizesAndPreservesStateWhenValidationFails()
    {
        var task = new OrganizationTask(Guid.NewGuid(), " Titolo ", "Riga\r\nDue", TaskPriority.High, null, null);
        Assert.Equal("Titolo", task.Title); Assert.Equal("Riga\nDue", task.Description);
        Assert.Throws<ArgumentOutOfRangeException>(() => task.Update(new string('x', 161), null, TaskPriority.Low, OrganizationTaskStatus.Completed, null, null));
        Assert.Equal("Titolo", task.Title); Assert.Equal(0, task.Revision);
        Assert.False(task.Update("Titolo", "Riga\nDue", TaskPriority.High, OrganizationTaskStatus.Todo, null, null));
        Assert.True(task.Update("Titolo", "Riga\nDue", TaskPriority.High, OrganizationTaskStatus.Completed, null, null));
        Assert.Equal(1, task.Revision);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(3, 0)] [InlineData(0, -1)] [InlineData(0, 4)]
    public void DomainRejectsUnknownEnums(int priority, int status)
    {
        var task = new OrganizationTask(Guid.NewGuid(), "Titolo", null, TaskPriority.Normal, null, null);
        Assert.Throws<ArgumentException>(() => task.Update("Titolo", null, (TaskPriority)priority, (OrganizationTaskStatus)status, null, null));
        Assert.Equal(TaskPriority.Normal, task.Priority); Assert.Equal(0, task.Revision);
    }

    [Fact]
    public async Task ChangesRequireMembershipAndCancellationConfirmationAndPreserveAudit()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TaskService>();
        var id = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, Input(assignee: factory.OrganizerPersonId,
            due: new DateTime(2026, 12, 31, 18, 0, 0)));
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        var task = Assert.Single(page.Tasks);
        Assert.Equal(new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc), task.DueAtUtc);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, Input(status: OrganizationTaskStatus.Cancelled)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, Input(assignee: Guid.NewGuid())));
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, Input(status: OrganizationTaskStatus.Cancelled, confirm: true)));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, Input()));
        Assert.False(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, Input(revision: 1, status: OrganizationTaskStatus.Cancelled)));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Tasks"));
        Assert.Equal(OrganizationTaskStatus.Cancelled, (await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Tasks.Single().Status);
    }

    [Theory]
    [InlineData(2026, 3, 29, 2, 30)]
    [InlineData(2026, 10, 25, 2, 30)]
    public async Task AmbiguousAndMissingLocalTimesDoNotWrite(int year, int month, int day, int hour, int minute)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TaskService>();
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId,
            Input(due: new DateTime(year, month, day, hour, minute, 0))));
        Assert.Empty((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Tasks);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().ToListAsync());
    }

    [Fact]
    public async Task HttpRequiresAuthenticationCsrfAndCurrentRevisionAndEncodesText()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tasks?eventId=" + factory.EventId)).StatusCode);
        await Login(client);
        var page = await client.GetStringAsync("/Tasks/Create?eventId=" + factory.EventId);
        var token = Token(page);
        var operationId = Regex.Match(page, "name=\"OperationId\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Tasks/Save", Form(("EventId", factory.EventId.ToString()), ("Title", "x")))).StatusCode);
        var creation = new[] { ("EventId", factory.EventId.ToString()), ("Title", "<script>alert(1)</script>"), ("OperationId", operationId), ("__RequestVerificationToken", token) };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Tasks/Save", Form(creation))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Tasks/Save", Form(creation))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/Tasks/Save", Form(("EventId", factory.EventId.ToString()), ("Title", "Payload diverso"), ("OperationId", operationId), ("__RequestVerificationToken", token)))).StatusCode);
        var listing = await client.GetStringAsync("/Tasks?eventId=" + factory.EventId);
        Assert.Contains("&lt;script&gt;", listing); Assert.DoesNotContain("<script>alert(1)</script>", listing);
        Guid id;
        using (var scope = factory.Services.CreateScope()) id = (await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<OrganizationTask>().SingleAsync()).Id;
        var edit = await client.GetStringAsync($"/Tasks/Edit?eventId={factory.EventId}&id={id}");
        var change = new[] { ("EventId", factory.EventId.ToString()), ("Id", id.ToString()), ("Title", "Titolo nuovo"), ("Revision", "0"), ("__RequestVerificationToken", Token(edit)) };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Tasks/Save", Form(change))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/Tasks/Save", Form(change))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tasks?eventId=" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task RevokedAccessAndCrossEventReferencesCannotReadOrWrite()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TaskService>();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(Guid.NewGuid(), factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.CreateAsync(factory.OrganizerPersonId, Guid.NewGuid(), Input()));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, Guid.NewGuid(), Input()));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var membership = await db.EventOrganizers.SingleAsync(); membership.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(factory.OrganizerPersonId, factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId, Input()));
        Assert.Empty(await db.Set<OrganizationTask>().ToListAsync());
    }

    [Fact]
    public async Task RecordFromAnotherManagedEventAndDisabledIdentityAreRejected()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TaskService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var other = new Event("Secondo evento gestibile", DateTime.UtcNow);
        var outsider = new Person("Solo nel secondo evento");
        db.AddRange(other, outsider, new EventOrganizer(other.Id, factory.OrganizerPersonId), new EventParticipant(other.Id, outsider.Id));
        await db.SaveChangesAsync();
        var taskId = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, Input());
        await Assert.ThrowsAsync<LocalAccessException>(() => service.UpdateAsync(factory.OrganizerPersonId, other.Id, taskId, Input("Tentativo altro evento")));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId, Input(assignee: outsider.Id)));
        var user = await db.Users.SingleAsync(x => x.PersonId == factory.OrganizerPersonId);
        user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(factory.OrganizerPersonId, factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, taskId, Input("Tentativo disabilitato")));
        Assert.Equal("Preparare tavoli", (await db.Set<OrganizationTask>().SingleAsync()).Title);
        Assert.Single(await db.Set<LocalAudit>().ToListAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackTaskAndRevision()
    {
        var interceptor = new FailTaskAudit();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TaskService>();
        var id = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, Input());
        interceptor.Armed = true;
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, Input("Modificato")));
        interceptor.Armed = false;
        var task = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Tasks);
        Assert.Equal("Preparare tavoli", task.Title); Assert.Equal(0, task.Revision);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().CountAsync());
    }
    private sealed class FailTaskAudit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LocalAudit>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Errore audit simulato.");
            return ValueTask.FromResult(result);
        }
    }
    private static async Task Login(HttpClient client)
    {
        var token = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName),
            ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)))).StatusCode);
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) => new(pairs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
}
