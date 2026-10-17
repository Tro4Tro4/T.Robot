using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using TRobot.Infrastructure.Identity;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class OperationalCommandTests
{
    private static SimulatedMessage Message(LocalWebFactory f, string text, string? id = null) =>
        new(f.EventId, "synthetic-http-sender", id ?? Guid.NewGuid().ToString("N"), text);

    private static async Task<(Guid Person, Guid Menu, Guid Task)> Setup(IServiceProvider p, LocalWebFactory f)
    {
        await p.GetRequiredService<AttendanceService>().SimulateAsync(f.OrganizerPersonId, Message(f, "!presenza si"));
        var db = p.GetRequiredService<TRobotDbContext>(); var person = (await db.EventParticipants.SingleAsync()).PersonId;
        var menu = new MenuItem(Guid.NewGuid(), f.EventId, "Acqua sintetica", MenuCategory.Drink, 12.5m, "litri", null, MenuAvailability.Planned, "Nota riservata");
        var task = new OrganizationTask(f.EventId, "Preparare tavoli", "Descrizione riservata", TaskPriority.High, null, null);
        db.AddRange(menu, task); await db.SaveChangesAsync(); return (person, menu.Id, task.Id);
    }

    [Fact]
    public async Task ReferencesAreStableTypedEventScopedAndRejectAmbiguityWithFullGuidFallback()
    {
        var eventId = Guid.NewGuid();
        var first = new OrganizationTask(eventId, "Uno", null, TaskPriority.Normal, null, null, Guid.Parse("abcdef01-0000-0000-0000-000000000001"));
        var second = new OrganizationTask(eventId, "Due", null, TaskPriority.Normal, null, null, Guid.Parse("abcdef01-0000-0000-0000-000000000002"));
        Assert.Equal("abcdef01", SimulationReferences.Short(first.Id));
        Assert.Same(first, SimulationReferences.Resolve(new[] { first }, "ABCDEF01"));
        Assert.Throws<LocalValidationException>(() => SimulationReferences.Resolve(new[] { first, second }, "abcdef01"));
        Assert.Same(first, SimulationReferences.Resolve(new[] { first, second }, first.Id.ToString()));
        Assert.Throws<LocalValidationException>(() => SimulationReferences.Resolve(new[] { first }, "abcdef0"));
        Assert.Throws<LocalValidationException>(() => SimulationReferences.Resolve(new[] { first }, Guid.NewGuid().ToString()));
        Assert.Throws<LocalValidationException>(() => SimulationReferences.Resolve(new[] { first }, "zzzzzzzz"));
        Assert.Throws<ArgumentException>(() => new SimulationActionReceipt(eventId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SimulationAction.BringMenu, first.Id, DateTime.Now));
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var service = p.GetRequiredService<SimulationCommandService>();
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, $"!porto {rows.Task}"), true));
        var db = p.GetRequiredService<TRobotDbContext>(); var foreign = new Event("Altro", DateTime.UtcNow);
        var menu = new MenuItem(Guid.NewGuid(), foreign.Id, "Estraneo", MenuCategory.Other, 1, "pezzi"); db.AddRange(foreign, menu); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, $"!porto {menu.Id}"), true));
        Assert.Empty(await db.Set<SimulationActionReceipt>().ToListAsync());
    }

    [Fact]
    public async Task OperationalFlowPreservesContentAndAttendanceAndReplaysWithoutOverwritingLaterChanges()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var service = p.GetRequiredService<SimulationCommandService>(); var db = p.GetRequiredService<TRobotDbContext>();
        var bring = Message(f, "!porto " + SimulationReferences.Short(rows.Menu), "bring-one");
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, bring));
        Assert.False((await service.ExecuteAsync(f.OrganizerPersonId, bring, true)).Duplicate);
        var menu = await db.Set<MenuItem>().SingleAsync(); Assert.Equal(rows.Person, menu.ResponsiblePersonId); Assert.Equal(MenuAvailability.Assigned, menu.Availability);
        Assert.Equal("Acqua sintetica", menu.Name); Assert.Equal(12.5m, menu.Quantity); Assert.Equal("Nota riservata", menu.Notes);
        var take = Message(f, "!prendo " + SimulationReferences.Short(rows.Task), "take-one"); await service.ExecuteAsync(f.OrganizerPersonId, take, true);
        var complete = Message(f, "!fatto " + rows.Task, "done-one"); await service.ExecuteAsync(f.OrganizerPersonId, complete, true);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, take, true)).Duplicate);
        var task = await db.Set<OrganizationTask>().SingleAsync(); Assert.Equal(OrganizationTaskStatus.Completed, task.Status); Assert.Equal(rows.Person, task.AssigneeId);
        Assert.Equal("Preparare tavoli", task.Title); Assert.Equal("Descrizione riservata", task.Description); Assert.Equal(TaskPriority.High, task.Priority);
        menu = await db.Set<MenuItem>().SingleAsync(); menu.Update(menu.Name, menu.Category, menu.Quantity, menu.Unit, menu.ResponsiblePersonId, MenuAvailability.Ready, menu.Notes);
        (await db.EventParticipants.SingleAsync()).ChangeStatus(ParticipationStatus.Absent, DateTime.UtcNow); await db.SaveChangesAsync();
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, bring with { Text = "!PORTO " + rows.Menu }, true)).Duplicate);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, complete, true)).Duplicate);
        Assert.Equal(MenuAvailability.Ready, (await db.Set<MenuItem>().SingleAsync()).Availability);
        Assert.Equal(ParticipationStatus.Absent, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal(3, await db.Set<SimulationActionReceipt>().CountAsync()); Assert.Equal(3, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Simulation"));
        Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
        Assert.Single(await db.ParticipationReceipts.ToListAsync());
    }

    [Theory]
    [InlineData("!menu")]
    [InlineData("!todo")]
    public async Task LocalListsPaginateFiveRowsWithStableReferencesWithoutNotesOrAssignees(string command)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>(); var person = (await db.EventParticipants.SingleAsync()).PersonId;
        for (var i = 0; i < 7; i++)
        {
            db.Add(new MenuItem(Guid.NewGuid(), f.EventId, $"Voce {i:00}", MenuCategory.Drink, 2, "litri", person, MenuAvailability.Assigned, "Nota nascosta"));
            db.Add(new OrganizationTask(f.EventId, $"Attività {i:00}", "Dettagli nascosti", TaskPriority.Normal, person, null));
        }
        await db.SaveChangesAsync(); var service = p.GetRequiredService<SimulationCommandService>();
        var first = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, command))).Reply;
        var second = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, command + " 2"))).Reply;
        Assert.Contains("Pagina 1/2", first); Assert.Contains($"Pagina successiva:\n{command} 2", first);
        Assert.Contains("Pagina 2/2", second); Assert.DoesNotContain("Pagina successiva:", second);
        Assert.Equal(5, first.Split('\n').Count(x => x.StartsWith("*", StringComparison.Ordinal))); Assert.Equal(2, second.Split('\n').Count(x => x.StartsWith("*", StringComparison.Ordinal)));
        foreach (var reply in new[] { first, second })
        {
            Assert.DoesNotContain("Nota nascosta", reply); Assert.DoesNotContain("Dettagli nascosti", reply);
            foreach (var name in await db.People.Select(x => x.DisplayName).ToListAsync()) Assert.DoesNotContain(name, reply);
        }
        foreach (var suffix in new[] { " 0", " -1", " 100001", " 3", " abc" })
            await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, command + suffix)));
        Assert.Empty(await db.Set<SimulationActionReceipt>().ToListAsync()); Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("undecided")]
    [InlineData("noresponse")]
    [InlineData("child")]
    public async Task NewCommitmentsRequireConfirmedAdultForAllActions(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var db = p.GetRequiredService<TRobotDbContext>();
        var task = await db.Set<OrganizationTask>().SingleAsync(); task.Update(task.Title, task.Description, task.Priority, OrganizationTaskStatus.InProgress, rows.Person, null);
        if (kind == "child")
        {
            var household = new Household(f.EventId, "Nucleo sintetico", Guid.NewGuid());
            db.AddRange(household, new HouseholdMember(f.EventId, household.Id, rows.Person, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        }
        else (await db.EventParticipants.SingleAsync()).ChangeStatus(kind == "absent" ? ParticipationStatus.Absent
            : kind == "undecided" ? ParticipationStatus.Undecided : ParticipationStatus.NoResponse, DateTime.UtcNow);
        await db.SaveChangesAsync(); var service = p.GetRequiredService<SimulationCommandService>();
        foreach (var command in new[] { "!porto " + rows.Menu, "!prendo " + rows.Task, "!fatto " + rows.Task })
            await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, command), true));
        Assert.Empty(await db.Set<SimulationActionReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData("menu-other")]
    [InlineData("menu-ready")]
    [InlineData("menu-cancelled")]
    [InlineData("task-other")]
    [InlineData("task-completed")]
    [InlineData("task-cancelled")]
    [InlineData("complete-unassigned")]
    public async Task CommandsDoNotTakeOthersAssignmentsOrReopenFinishedTargets(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var other = new Person("Altro sintetico");
        db.AddRange(other, new EventParticipant(f.EventId, other.Id)); await db.SaveChangesAsync();
        if (kind.StartsWith("menu"))
        {
            var menu = await db.Set<MenuItem>().SingleAsync();
            menu.Update(menu.Name, menu.Category, menu.Quantity, menu.Unit, kind == "menu-other" ? other.Id : null,
                kind == "menu-other" ? MenuAvailability.Assigned : kind == "menu-ready" ? MenuAvailability.Ready : MenuAvailability.Cancelled, menu.Notes);
        }
        else
        {
            var task = await db.Set<OrganizationTask>().SingleAsync();
            task.Update(task.Title, task.Description, task.Priority, kind == "task-completed" ? OrganizationTaskStatus.Completed
                : kind == "task-cancelled" ? OrganizationTaskStatus.Cancelled : OrganizationTaskStatus.Todo, kind == "task-other" ? other.Id : null, null);
        }
        await db.SaveChangesAsync(); var command = kind.StartsWith("menu") ? "!porto " + rows.Menu : (kind == "complete-unassigned" ? "!fatto " : "!prendo ") + rows.Task;
        await Assert.ThrowsAsync<LocalValidationException>(() => p.GetRequiredService<SimulationCommandService>().ExecuteAsync(f.OrganizerPersonId, Message(f, command), true));
        Assert.Empty(await db.Set<SimulationActionReceipt>().ToListAsync());
    }

    [Fact]
    public async Task ReceiptsRejectChangedActionTargetSenderAndOtherCommandTypesAndRevalidateAccessOnReplay()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var service = p.GetRequiredService<SimulationCommandService>();
        var message = Message(f, "!prendo " + rows.Task, "shared-message"); await service.ExecuteAsync(f.OrganizerPersonId, message, true);
        foreach (var text in new[] { "!fatto " + rows.Task, "!porto " + rows.Menu, "!presenza no", "!vota " + Guid.NewGuid() + " 1" })
            await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { Text = text }, true));
        var otherTask = new OrganizationTask(f.EventId, "Altra", null, TaskPriority.Normal, null, null);
        var person = new Person("Altro mittente", "other-synthetic-sender"); db.AddRange(otherTask, person, new EventParticipant(f.EventId, person.Id)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { Text = "!prendo " + otherTask.Id }, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { SenderId = "other-synthetic-sender" }, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { SenderId = "unknown" }, true));
        var presence = Message(f, "!presenza si", "presence-collision"); await service.ExecuteAsync(f.OrganizerPersonId, presence);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, presence with { Text = "!porto " + rows.Menu }, true));
        var option = new PollOption(f.EventId, Guid.NewGuid(), 1, "Test");
        // Canonical vote receipt fixture validates collision before any target changes.
        var poll = new Poll(f.EventId, option.PollId, "Test", DateTime.UtcNow.AddDays(1));
        db.AddRange(poll, option, new PollVoteReceipt(f.EventId, poll.Id, SimulationReferences.MessageId(f.EventId, "vote-collision"),
            f.OrganizerPersonId, rows.Person, option.Id, DateTime.UtcNow)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { MessageId = "vote-collision", Text = "!porto " + rows.Menu }, true));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { EventId = Guid.NewGuid() }, true));
        var assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, message, true));
        assignment.SetActive(true); (await db.Users.SingleAsync()).IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, message, true));
        Assert.Single(await db.Set<SimulationActionReceipt>().ToListAsync());
    }

    [Fact]
    public async Task FailureAfterSaveRollsBackTargetReceiptAndAuditTogether()
    {
        var interceptor = new FailAfterSave(); await using var f = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var service = p.GetRequiredService<SimulationCommandService>(); var message = Message(f, "!porto " + rows.Menu);
        interceptor.Armed = true; await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(f.OrganizerPersonId, message, true)); interceptor.Armed = false;
        var db = p.GetRequiredService<TRobotDbContext>(); var menu = await db.Set<MenuItem>().SingleAsync();
        Assert.Null(menu.ResponsiblePersonId); Assert.Equal(MenuAvailability.Planned, menu.Availability); Assert.Equal(0, menu.Revision);
        Assert.Empty(await db.Set<SimulationActionReceipt>().ToListAsync()); Assert.Empty(await db.Set<LocalAudit>().ToListAsync());
        await service.ExecuteAsync(f.OrganizerPersonId, message, true); Assert.Single(await db.Set<SimulationActionReceipt>().ToListAsync());
    }

    [Fact]
    public async Task NewIdsForExistingOwnAssignmentsRecordReceiptsWithoutChangingRevisionAndRejectAnotherActorReplay()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Setup(p, f);
        var service = p.GetRequiredService<SimulationCommandService>(); var db = p.GetRequiredService<TRobotDbContext>();
        var bring = Message(f, "!porto " + rows.Menu); var take = Message(f, "!prendo " + rows.Task);
        await service.ExecuteAsync(f.OrganizerPersonId, bring, true); await service.ExecuteAsync(f.OrganizerPersonId, take, true);
        var secondBring = bring with { MessageId = "same-assignment-new-message" }; var secondTake = take with { MessageId = "same-task-new-message" };
        await service.ExecuteAsync(f.OrganizerPersonId, secondBring, true); await service.ExecuteAsync(f.OrganizerPersonId, secondTake, true);
        Assert.Equal(1, (await db.Set<MenuItem>().SingleAsync()).Revision); Assert.Equal(1, (await db.Set<OrganizationTask>().SingleAsync()).Revision);
        Assert.Equal(4, await db.Set<SimulationActionReceipt>().CountAsync());
        await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!fatto " + rows.Task), true);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, secondTake, true)).Duplicate);
        Assert.Equal(OrganizationTaskStatus.Completed, (await db.Set<OrganizationTask>().SingleAsync()).Status);
        var secondActor = new Person("Secondo organizzatore sintetico");
        db.AddRange(secondActor, new EventOrganizer(f.EventId, secondActor.Id), new OrganizerUser
        { Id = Guid.NewGuid(), PersonId = secondActor.Id, UserName = "second-organizer-synthetic", NormalizedUserName = "SECOND-ORGANIZER-SYNTHETIC" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(secondActor.Id, secondBring, true));
        Assert.Equal(5, await db.Set<SimulationActionReceipt>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDistinctPeopleCannotReplaceEachOthersAssignment(bool taskTarget)
    {
        await using var f = new LocalWebFactory(); (Guid Person, Guid Menu, Guid Task) rows;
        using (var setup = f.Services.CreateScope())
        {
            rows = await Setup(setup.ServiceProvider, f); var db = setup.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var second = new Person("Secondo partecipante sintetico", "second-synthetic-sender");
            var participation = new EventParticipant(f.EventId, second.Id); participation.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
            db.AddRange(second, participation); await db.SaveChangesAsync();
        }
        using var firstScope = f.Services.CreateScope(); using var secondScope = f.Services.CreateScope();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = (taskTarget ? "!prendo " : "!porto ") + (taskTarget ? rows.Task : rows.Menu);
        async Task<Exception?> Run(IServiceProvider p, string sender)
        {
            await start.Task;
            return await Record.ExceptionAsync(() => p.GetRequiredService<SimulationCommandService>()
                .ExecuteAsync(f.OrganizerPersonId, Message(f, command) with { SenderId = sender }, true));
        }
        var one = Run(firstScope.ServiceProvider, "synthetic-http-sender"); var two = Run(secondScope.ServiceProvider, "second-synthetic-sender");
        start.SetResult(); var results = await Task.WhenAll(one, two);
        Assert.Single(results, x => x is null); Assert.IsType<LocalValidationException>(Assert.Single(results, x => x is not null));
        using var check = f.Services.CreateScope(); Assert.Single(await check.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<SimulationActionReceipt>().ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPresenceAndCommitmentCannotShareMessageId()
    {
        await using var f = new LocalWebFactory(); Guid menuId;
        using (var setup = f.Services.CreateScope()) menuId = (await Setup(setup.ServiceProvider, f)).Menu;
        using var first = f.Services.CreateScope(); using var second = f.Services.CreateScope();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Run(IServiceProvider p, string command)
        {
            await start.Task;
            return await Record.ExceptionAsync(() => p.GetRequiredService<SimulationCommandService>()
                .ExecuteAsync(f.OrganizerPersonId, Message(f, command, "concurrent-cross-type"), true));
        }
        var presence = Run(first.ServiceProvider, "!presenza si"); var bring = Run(second.ServiceProvider, "!porto " + menuId);
        start.SetResult(); var results = await Task.WhenAll(presence, bring);
        Assert.Single(results, x => x is null); Assert.IsType<LocalConflictException>(Assert.Single(results, x => x is not null));
        using var check = f.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.ParticipationReceipts.CountAsync(x => x.MessageId == "concurrent-cross-type") + await db.Set<SimulationActionReceipt>().CountAsync());
    }

    [Fact]
    public async Task HttpEnforcesCsrfExplicitConfirmationAndEncodesLocalTitles()
    {
        await using var f = new LocalWebFactory(); Guid menuId;
        using (var scope = f.Services.CreateScope())
        {
            var rows = await Setup(scope.ServiceProvider, f); menuId = rows.Menu; var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var menu = await db.Set<MenuItem>().SingleAsync(); menu.Update("<script>titolo</script>", menu.Category, menu.Quantity, menu.Unit, null, menu.Availability, menu.Notes); await db.SaveChangesAsync();
        }
        using var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        token = Token(await client.GetStringAsync("/?eventId=" + f.EventId));
        var values = new[] { ("Input.EventId", f.EventId.ToString()), ("Input.SenderId", "synthetic-http-sender"), ("Input.MessageId", "http-bring"), ("Input.Text", "!porto " + SimulationReferences.Short(menuId)) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Home/Simulate", Form(values))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Home/Simulate", Form(values.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(values.Append(("__RequestVerificationToken", token)).Append(("Input.ConfirmVote", "true")).ToArray()))).StatusCode);
        var listing = values.Select(x => x.Item1 == "Input.MessageId" ? (x.Item1, "list-id") : x.Item1 == "Input.Text" ? (x.Item1, "!menu") : x).Append(("__RequestVerificationToken", token)).ToArray();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(listing))).StatusCode);
        var html = await client.GetStringAsync("/?eventId=" + f.EventId);
        Assert.Contains("&lt;script&gt;titolo&lt;/script&gt;", html); Assert.DoesNotContain("<script>titolo</script>", html);
        Assert.Contains(SimulationReferences.Short(menuId), html); Assert.DoesNotContain("Nota riservata", html);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Armed) throw new ApplicationException("Errore sintetico dopo salvataggio."); return ValueTask.FromResult(result); }
    }
}
