using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Messaging;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class MessageTests
{
    private static readonly DateTime Noon = new(2026, 12, 31, 11, 0, 0, DateTimeKind.Utc);
    private sealed class Clock(DateTime utc) : TimeProvider
    { public DateTime Utc { get; set; } = utc; public override DateTimeOffset GetUtcNow() => new(Utc); }
    private static MessageService Service(IServiceProvider provider, Clock clock)
    { var store = provider.GetRequiredService<ILocalStore>(); return new(store, new LocalOperations(store, clock), clock); }
    private static MessageEdit Draft(string body = "Avviso aggregato", MessageKind kind = MessageKind.Operational, DateTime? local = null) => new(Guid.NewGuid(), body, kind, local, 0);

    [Fact]
    public void DomainProtectsWorkflowAndValidatesWithoutMutation()
    {
        var message = new OutboundMessage(Guid.NewGuid(), Guid.NewGuid(), " Riga\r\nDue ", MessageKind.Operational, Noon);
        Assert.Equal("Riga\nDue", message.Body);
        Assert.Throws<ArgumentException>(() => message.Queue(false)); Assert.Equal(0, message.Revision);
        Assert.Throws<ArgumentException>(() => message.Edit("Testo", (MessageKind)999, Noon)); Assert.Equal("Riga\nDue", message.Body);
        Assert.True(message.Queue(true)); Assert.False(message.Queue(true));
        Assert.Throws<InvalidOperationException>(() => message.Edit("Testo", MessageKind.Operational, Noon));
        Assert.Throws<InvalidOperationException>(() => message.MarkSent(Noon.AddSeconds(-1)));
        Assert.True(message.MarkSent(Noon)); Assert.False(message.MarkSent(Noon.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => message.Cancel(true));
        Assert.Equal(Noon, message.SentAtUtc); Assert.Equal(2, message.Revision);
    }
    [Fact]
    public void GeneratedSummaryFingerprintCannotBecomeStaleByEditingContent()
    {
        var message = new OutboundMessage(Guid.NewGuid(), Guid.NewGuid(), "Aggregati", MessageKind.Summary, Noon);
        message.SetSummaryFingerprint("1/2/3");
        Assert.False(message.Edit("Aggregati", MessageKind.Summary, Noon));
        Assert.Throws<InvalidOperationException>(() => message.Edit("Modificato", MessageKind.Summary, Noon));
        Assert.Throws<InvalidOperationException>(() => message.Edit("Aggregati", MessageKind.Operational, Noon));
        Assert.Equal(0, message.Revision);
        Assert.Equal("Aggregati", message.Body);
    }
    [Fact]
    public async Task ClearingFutureScheduleMakesDraftDueWithoutChangingRepeatedImmediateSaves()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var service = Service(scope.ServiceProvider, clock);
        var input = Draft(local: new DateTime(2027,1,2,12,0,0));
        var id = await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, null, input);
        await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, id, input with { ScheduledLocal = null });
        var due = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(Noon, due.ScheduledAtUtc); Assert.Equal(1, due.Revision);
        clock.Utc = Noon.AddMinutes(5);
        await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, id, input with { ScheduledLocal = null, Revision = 1 });
        var unchanged = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(Noon, unchanged.ScheduledAtUtc); Assert.Equal(1, unchanged.Revision);
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().CountAsync());
    }
    [Theory]
    [InlineData(31,7)] [InlineData(30,4)] [InlineData(15,4)] [InlineData(14,2)] [InlineData(7,2)] [InlineData(6,1)] [InlineData(1,1)] [InlineData(0,null)] [InlineData(-1,null)]
    public void SummaryFrequencyUsesEventCalendar(int days, int? expected)
    {
        var ev = new Event("Evento", Noon.AddDays(days));
        Assert.Equal(expected, SchedulePolicy.SummaryIntervalDays(Noon, ev));
    }
    [Theory]
    [InlineData(22,false)] [InlineData(23,true)] [InlineData(0,true)] [InlineData(8,true)] [InlineData(9,false)]
    public void QuietHoursCrossMidnight(int hour, bool expected)
    { Assert.Equal(expected, SchedulePolicy.IsQuiet(new DateTime(2026,12,31,hour,0,0), new BotSettings(Guid.NewGuid()))); }
    [Fact]
    public void QuietHoursSupportSameDayAndDisabledRangeAndSettingsRemainUnchangedOnFailure()
    {
        var settings = new BotSettings(Guid.NewGuid());
        settings.Update(false, 12, 14, 2, 4);
        Assert.True(SchedulePolicy.IsQuiet(new DateTime(2026,12,31,13,0,0), settings));
        Assert.False(SchedulePolicy.IsQuiet(new DateTime(2026,12,31,14,0,0), settings));
        Assert.Throws<ArgumentException>(() => settings.Update(true, 24, 14, 2, 4)); Assert.False(settings.Paused); Assert.Equal(1, settings.Revision);
        settings.Update(false, 12,12,0,0); Assert.False(SchedulePolicy.IsQuiet(new DateTime(2026,12,31,12,0,0),settings));
    }
    [Fact]
    public async Task DraftPreviewQueueConfirmationReplaysAndCancellationPreserveAudit()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var service = Service(scope.ServiceProvider, new Clock(Noon)); var input = Draft();
        var id = await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, null, input);
        Assert.Equal(id, await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, null, input));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, null, input with { Body = "Differente" }));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.TransitionAsync(factory.OrganizerPersonId, factory.EventId, id,0,false,false));
        Assert.Equal(0,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        Assert.True(await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,id,0,true,false));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,id,0,true,true));
        Assert.True(await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,id,1,true,true));
        Assert.False(await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,id,2,true,true));
        Assert.Equal(0,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        Assert.Equal(OutboundMessageStatus.Cancelled,Assert.Single((await service.ListAsync(factory.OrganizerPersonId,factory.EventId)).Messages).Status);
        Assert.Equal(3,await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().CountAsync());
    }
    [Fact]
    public async Task SchedulerHonorsQueueDueQuietPauseAndDailyCapAndRetriesAreNoops()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var service = Service(scope.ServiceProvider,clock);
        await SetEventDay(scope.ServiceProvider,factory.EventId,Noon);
        for(var i=0;i<4;i++)
        { var id=await service.SaveDraftAsync(factory.OrganizerPersonId,factory.EventId,null,Draft("Gioco "+i,MessageKind.Entertainment)); await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,id,0,true,false); }
        var future=await service.SaveDraftAsync(factory.OrganizerPersonId,factory.EventId,null,Draft("Futuro",local:new DateTime(2027,1,2,12,0,0)));
        await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,future,0,true,false);
        Assert.Equal(3,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        Assert.Equal(0,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        clock.Utc=Noon.AddHours(12); // midnight in Rome, new day but still quiet
        Assert.Equal(0,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        var operational=await service.SaveDraftAsync(factory.OrganizerPersonId,factory.EventId,null,Draft());
        await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,operational,0,true,false);
        await service.ConfigureAsync(factory.OrganizerPersonId,factory.EventId,new(true,23,9,3,5,0));
        Assert.Equal(0,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        await service.ConfigureAsync(factory.OrganizerPersonId,factory.EventId,new(false,23,9,3,5,1));
        Assert.Equal(1,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent); // Operational bypass
        clock.Utc=Noon.AddDays(1);
        Assert.Equal(1,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        var messages=(await service.ListAsync(factory.OrganizerPersonId,factory.EventId)).Messages;
        Assert.Equal(5,messages.Count(x=>x.Status==OutboundMessageStatus.Sent)); Assert.Equal(OutboundMessageStatus.Queued,messages.Single(x=>x.Id==future).Status);
    }
    [Theory]
    [InlineData(2026,3,29)] [InlineData(2026,10,25)]
    public async Task DstAmbiguousOrMissingSchedulesRejectWithoutWrites(int y,int m,int d)
    {
        await using var factory=new LocalWebFactory(); using var scope=factory.Services.CreateScope(); var service=Service(scope.ServiceProvider,new Clock(Noon));
        await Assert.ThrowsAsync<LocalValidationException>(()=>service.SaveDraftAsync(factory.OrganizerPersonId,factory.EventId,null,Draft(local:new DateTime(y,m,d,2,30,0))));
        Assert.Empty((await service.ListAsync(factory.OrganizerPersonId,factory.EventId)).Messages);
    }
    [Fact]
    public async Task ScheduledLocalTimeConvertsToUtcAndWaitsUntilDue()
    {
        await using var factory=new LocalWebFactory(); using var scope=factory.Services.CreateScope(); var clock=new Clock(Noon); var service=Service(scope.ServiceProvider,clock);
        await SetEventDay(scope.ServiceProvider,factory.EventId,Noon);
        var id=await service.SaveDraftAsync(factory.OrganizerPersonId,factory.EventId,null,Draft(local:new DateTime(2026,12,31,13,0,0)));
        await service.TransitionAsync(factory.OrganizerPersonId,factory.EventId,id,0,true,false);
        Assert.Equal(Noon.AddHours(1),Assert.Single((await service.ListAsync(factory.OrganizerPersonId,factory.EventId)).Messages).ScheduledAtUtc);
        Assert.Equal(0,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
        clock.Utc=Noon.AddHours(1); Assert.Equal(1,(await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true)).Sent);
    }
    [Fact]
    public async Task SummaryOnlyContainsAggregatesSkipsUnchangedAndPendingCopiesAndEventDay()
    {
        await using var factory=new LocalWebFactory(); using var scope=factory.Services.CreateScope(); var clock=new Clock(Noon); var service=Service(scope.ServiceProvider,clock);
        await SetEventDay(scope.ServiceProvider,factory.EventId,Noon.AddDays(40));
        Assert.Equal(new TickResult(1,true),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        Assert.Equal(new TickResult(0,false),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        var page=await service.ListAsync(factory.OrganizerPersonId,factory.EventId); var summary=Assert.Single(page.Messages);
        Assert.Contains("Riepilogo evento: confermati",summary.Body); Assert.DoesNotContain(page.Event.Name,summary.Body);
        var names=await scope.ServiceProvider.GetRequiredService<ILocalStore>().PeopleAsync(factory.EventId,default);
        foreach(var name in names) Assert.DoesNotContain(name.Name,summary.Body);
        Assert.DoesNotContain("€",summary.Body); Assert.DoesNotContain("saldo",summary.Body);
        clock.Utc=Noon.AddDays(8); Assert.Equal(new TickResult(0,false),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        // Changed aggregate, quiet hours: queue once and retry without duplicates.
        var db=scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        db.Add(new OrganizationTask(factory.EventId,"Nuova attività",null,TaskPriority.Normal,null,null)); await db.SaveChangesAsync();
        clock.Utc=Noon.AddDays(8).AddHours(12);
        Assert.Equal(new TickResult(0,true),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        Assert.Equal(new TickResult(0,false),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        await SetEventDay(scope.ServiceProvider,factory.EventId,clock.Utc);
        clock.Utc=clock.Utc.AddHours(12);
        Assert.Equal(new TickResult(0,false),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        Assert.Equal(2,(await service.ListAsync(factory.OrganizerPersonId,factory.EventId)).Messages.Count);
    }
    [Fact]
    public async Task AccessAndSettingsRevisionProtectAllWrites()
    {
        await using var factory=new LocalWebFactory(); using var scope=factory.Services.CreateScope(); var service=Service(scope.ServiceProvider,new Clock(Noon));
        await Assert.ThrowsAsync<LocalAccessException>(()=>service.TickAsync(Guid.NewGuid(),factory.EventId,true));
        await Assert.ThrowsAsync<LocalValidationException>(()=>service.TickAsync(factory.OrganizerPersonId,factory.EventId,false));
        await Assert.ThrowsAsync<LocalAccessException>(()=>service.SaveDraftAsync(factory.OrganizerPersonId,Guid.NewGuid(),null,Draft()));
        await service.ConfigureAsync(factory.OrganizerPersonId,factory.EventId,new(false,23,9,2,5,0));
        await Assert.ThrowsAsync<LocalConflictException>(()=>service.ConfigureAsync(factory.OrganizerPersonId,factory.EventId,new(true,23,9,3,5,0)));
        var db=scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var user=await db.Users.SingleAsync(x=>x.PersonId==factory.OrganizerPersonId); user.IsEnabled=false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(()=>service.ListAsync(factory.OrganizerPersonId,factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(()=>service.ConfigureAsync(factory.OrganizerPersonId,factory.EventId,new(true,23,9,2,5,1)));
    }
    [Fact]
    public async Task AuditFailureRollsBackOutboxAndSummarySettingsTogether()
    {
        var interceptor=new FailAudit(); await using var factory=new LocalWebFactory{SaveInterceptor=interceptor}; using var scope=factory.Services.CreateScope();
        var service=Service(scope.ServiceProvider,new Clock(Noon)); await SetEventDay(scope.ServiceProvider,factory.EventId,Noon.AddDays(40));
        interceptor.Armed=true;
        await Assert.ThrowsAsync<LocalValidationException>(()=>service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
        interceptor.Armed=false;
        var page=await service.ListAsync(factory.OrganizerPersonId,factory.EventId); Assert.Empty(page.Messages); Assert.Null(page.Settings.LastSummaryAtUtc);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().ToListAsync());
        Assert.Equal(new TickResult(1,true),await service.TickAsync(factory.OrganizerPersonId,factory.EventId,true));
    }
    [Fact]
    public async Task HttpRequiresAuthenticationCsrfExplicitConfirmationAndEncodesPreview()
    {
        await using var factory=new LocalWebFactory(); using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync("/Messages?eventId="+factory.EventId)).StatusCode);
        var login=Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login",Form(("UserName",LocalWebFactory.UserName),("Password",LocalWebFactory.Password),("__RequestVerificationToken",login)));
        var page=await client.GetStringAsync("/Messages/Create?eventId="+factory.EventId); var token=Token(page);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Messages/Save",Form(("EventId",factory.EventId.ToString()),("Body","x")))).StatusCode);
        var create=await client.PostAsync("/Messages/Save",Form(("EventId",factory.EventId.ToString()),("Body","<script>private()</script>"),("OperationId",Guid.NewGuid().ToString()),("__RequestVerificationToken",token)));
        Assert.Equal(HttpStatusCode.Redirect,create.StatusCode);
        var preview=await client.GetStringAsync(create.Headers.Location); Assert.Contains("&lt;script&gt;",preview); Assert.DoesNotContain("<script>private()</script>",preview);
        Assert.Contains("Confermo testo",preview);
        Guid id; using(var scope=factory.Services.CreateScope()) id=(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<OutboundMessage>().SingleAsync()).Id;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Messages/Transition",Form(("EventId",factory.EventId.ToString()),("Id",id.ToString()),("Revision","0"),("__RequestVerificationToken",Token(preview))))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Messages/Transition",Form(("EventId",factory.EventId.ToString()),("Id",id.ToString()),("Revision","0"),("Confirmed","true"),("__RequestVerificationToken",Token(preview))))).StatusCode);
    }
    [Fact]
    public async Task HttpEditRetainsScheduledSecondsAndFractionsWithoutSpuriousAudit()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)));
        Guid id; var instant = Noon.AddSeconds(12).AddTicks(1234567);
        using (var scope = factory.Services.CreateScope())
        {
            var service = Service(scope.ServiceProvider, new Clock(instant));
            id = await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, null, Draft());
        }
        var edit = await client.GetStringAsync($"/Messages/Edit?eventId={factory.EventId}&id={id}");
        var local = WebUtility.HtmlDecode(Regex.Match(edit, "name=\"ScheduledLocal\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.EndsWith(":12.123", local);
        var response = await client.PostAsync("/Messages/Save", Form(("EventId", factory.EventId.ToString()), ("Id", id.ToString()),
            ("Body", "Avviso aggregato"), ("Kind", "Operational"), ("Revision", "0"), ("ScheduledLocal", local), ("__RequestVerificationToken", Token(edit))));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var message = await db.Set<OutboundMessage>().SingleAsync();
            Assert.Equal(instant, message.ScheduledAtUtc); Assert.Equal(0, message.Revision);
            Assert.Equal(1, await db.Set<LocalAudit>().CountAsync());
        }
        var changedLocal = DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture).AddSeconds(1)
            .ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        var changed = await client.PostAsync("/Messages/Save", Form(("EventId", factory.EventId.ToString()), ("Id", id.ToString()),
            ("Body", "Avviso aggregato"), ("Kind", "Operational"), ("Revision", "0"), ("ScheduledLocal", changedLocal), ("__RequestVerificationToken", Token(edit))));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var message = await db.Set<OutboundMessage>().SingleAsync();
            Assert.Equal(new DateTime(instant.Ticks - instant.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc).AddSeconds(1), message.ScheduledAtUtc);
            Assert.Equal(1, message.Revision); Assert.Equal(2, await db.Set<LocalAudit>().CountAsync());
        }
    }
    [Fact]
    public async Task HttpBodyLimitUsesNormalizedTextAndEquivalentEditIsNoop()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)));
        var page = await client.GetStringAsync("/Messages/Create?eventId=" + factory.EventId);
        var token = Token(page); var operation = Guid.NewGuid();
        var body = string.Concat(Enumerable.Repeat("a\r\n", 1999)) + "ab";
        // 5999 raw characters normalize to 4000 before the domain limit is applied.
        var create = await client.PostAsync("/Messages/Save", Form(("EventId", factory.EventId.ToString()), ("Body", body),
            ("OperationId", operation.ToString()), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var message = await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<OutboundMessage>().SingleAsync();
            Assert.Equal(4000, message.Body.Length); Assert.DoesNotContain("\r", message.Body);
        }
        var edit = await client.PostAsync("/Messages/Save", Form(("EventId", factory.EventId.ToString()), ("Id", operation.ToString()),
            ("Revision", "0"), ("Body", body.ReplaceLineEndings("\n")), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        var tooLong = await client.PostAsync("/Messages/Save", Form(("EventId", factory.EventId.ToString()), ("Body", body + "x"),
            ("OperationId", Guid.NewGuid().ToString()), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            Assert.Equal(0, (await db.Set<OutboundMessage>().SingleAsync()).Revision);
            Assert.Equal(1, await db.Set<LocalAudit>().CountAsync());
        }
    }
    private static async Task SetEventDay(IServiceProvider provider,Guid eventId,DateTime utc)
    { var db=provider.GetRequiredService<TRobotDbContext>(); var ev=await db.Events.SingleAsync(x=>x.Id==eventId); ev.UpdateDetails(ev.Name,utc,ev.TimeZoneId,ev.Location,ev.Program); await db.SaveChangesAsync(); }
    private sealed class FailAudit:SaveChangesInterceptor
    {
        public bool Armed{get;set;}
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        { if(Armed&&data.Context!.ChangeTracker.Entries<LocalAudit>().Any(x=>x.State==EntityState.Added)) throw new InvalidOperationException("Audit simulato fallito."); return ValueTask.FromResult(result); }
    }
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params(string Key,string Value)[] pairs)=>new(pairs.Select(x=>new KeyValuePair<string,string>(x.Key,x.Value)));
}
