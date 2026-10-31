using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TRobot.Application.Menu;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class MealPreferenceWebTests
{
    [Fact]
    public async Task OrganizerPreparesMergedListOpensAndClosesWithAuthorizationConfirmationAndAntiforgery()
    {
        await using var factory = new WebFlowTests.LocalWebFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PortalModules:Enabled:Menu"] = "false" })));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var url = "/MealVoting?eventId=" + factory.EventId;
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(url)).StatusCode);
        var login = await client.GetStringAsync("/Access/Login");
        await client.PostAsync("/Access/Login", Form(new() {
            ["UserName"] = WebFlowTests.LocalWebFactory.UserName, ["Password"] = WebFlowTests.LocalWebFactory.Password,
            ["__RequestVerificationToken"] = Token(login) }));
        var html = await client.GetStringAsync(url); var token = Token(html);
        Assert.Contains("Gestisci la raccolta", html);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/MealVoting/Start", Form(new() { ["EventId"] = factory.EventId.ToString() }))).StatusCode);
        var cycleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/MealVoting/Start", Form(new() {
            ["EventId"] = factory.EventId.ToString(), ["CycleId"] = cycleId.ToString(), ["__RequestVerificationToken"] = token }))).StatusCode);
        async Task Post(string action, Dictionary<string, string> data)
        {
            data["EventId"] = factory.EventId.ToString(); data["CycleId"] = cycleId.ToString();
            data["Confirmed"] = "true"; data["__RequestVerificationToken"] = token;
            var response = await client.PostAsync("/MealVoting/" + action, Form(data)); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        await Post("Start", new());
        Guid[] proposalIds;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var person = (await db.EventParticipants.SingleAsync(x => x.EventId == factory.EventId)).PersonId;
            var a = new MealProposal(factory.EventId, person, new(2030, 12, 31), MealKind.Dinner, "Lasagne", false, DateTime.UtcNow);
            var b = new MealProposal(factory.EventId, person, a.Day, a.Meal, "Lasagna", true, DateTime.UtcNow);
            db.AddRange(a, b); await db.SaveChangesAsync(); proposalIds = [a.Id, b.Id];
        }
        await Post("Prepare", new() { ["Revision"] = "0" });
        MealVotingCycle cycle; MealVotingEntry entry;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            cycle = await db.Set<MealVotingCycle>().SingleAsync(); entry = await db.Set<MealVotingEntry>().FirstAsync();
        }
        await Post("Entry", new() {
            ["Revision"] = cycle.Revision.ToString(), ["EntryRevision"] = entry.Revision.ToString(), ["Id"] = entry.Id.ToString(),
            ["Name"] = "Lasagne <script>test</script>", ["ProposalIds[0]"] = proposalIds[0].ToString(), ["ProposalIds[1]"] = proposalIds[1].ToString() });
        html = await client.GetStringAsync(url);
        Assert.Contains("Lasagne &lt;script&gt;test&lt;/script&gt;", html); Assert.DoesNotContain("<script>test</script>", html);
        Assert.Contains("Anteprima dell", html);
        using (var scope = host.Services.CreateScope()) cycle = await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<MealVotingCycle>().SingleAsync();
        await Post("Open", new() { ["Revision"] = cycle.Revision.ToString() });
        html = await client.GetStringAsync(url); Assert.Contains("Risultati provvisori", html); Assert.Contains("!preferenze", html);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            cycle = await db.Set<MealVotingCycle>().SingleAsync();
            Assert.Equal(MealVotingState.Open, cycle.State); Assert.Equal(2, await db.Set<MealProposal>().CountAsync());
            var participant = await db.EventParticipants.SingleAsync(x => x.EventId == factory.EventId);
            participant.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
            var household = new Household(factory.EventId, "Nucleo sintetico HTTP", Guid.NewGuid());
            var member = new HouseholdMember(factory.EventId, household.Id, participant.PersonId, HouseholdMemberKind.Adult, null, Guid.NewGuid());
            var stay = new AttendanceStay(factory.EventId, participant.PersonId); stay.Set(new(2030, 12, 31), 1);
            db.AddRange(household, member, stay); await db.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<MealVotingService>();
            var ballot = await service.ReadBallotAsync(factory.OrganizerPersonId, factory.EventId, participant.PersonId, participant.PersonId, new(2030, 12, 31), MealKind.Dinner);
            await service.SavePreferencesAsync(factory.OrganizerPersonId, factory.EventId, new(Guid.NewGuid(), cycle.Id, cycle.Revision,
                participant.PersonId, participant.PersonId, ballot.Day, ballot.Meal, [entry.Id], ballot.BallotRevision, ballot.EligibilityFingerprint));
        }
        await Post("Close", new() { ["Revision"] = cycle.Revision.ToString() });
        html = await client.GetStringAsync(url); Assert.Contains("Risultato conservato alla chiusura", html);
        Assert.Contains("Conteggio aggiornato sulle presenze correnti", html);
        Assert.Contains("Storico delle preferenze", html); Assert.Contains("Registrata da", html);
        Assert.Contains("Lasagne &lt;script&gt;test&lt;/script&gt;", html); Assert.DoesNotContain("<script>test</script>", html);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url + "&historyPage=0")).StatusCode);
        var forbidden = await client.GetAsync("/MealVoting?eventId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode); Assert.Contains("Denied", forbidden.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Menu?eventId=" + factory.EventId)).StatusCode);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
}
