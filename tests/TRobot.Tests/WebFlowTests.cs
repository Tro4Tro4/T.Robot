using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using TRobot.Infrastructure.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace TRobot.Tests;

public sealed class WebFlowTests
{
    [Fact]
    public async Task LoginSimulationReplayAndLogoutWorkThroughRealHttpPipeline()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymous = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("http://localhost/Access/Login", anonymous.Headers.Location!.ToString());
        var stylesheet = await client.GetAsync("/lib/bootstrap/dist/css/bootstrap.min.css");
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType!.MediaType);
        var loginHtml = await client.GetStringAsync("/Access/Login");
        var loginToken = Token(loginHtml);
        var missingToken = await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password)));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        var wrong = await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", "invalid-code"), ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Credenziali non valide", await wrong.Content.ReadAsStringAsync());
        var signedIn = await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var cookie = signedIn.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(".TRobot.Identity="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("id=\"confirmed-count\">0", dashboard);
        Assert.Contains("Partecipante test HTTP", dashboard);
        Assert.Contains("no-store", (await client.GetAsync("/")).Headers.CacheControl!.ToString());
        var token = Token(dashboard);
        var messageId = Guid.NewGuid().ToString("N");
        var noCsrf = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, messageId));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var submitted = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, messageId, token));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        var updated = await client.GetStringAsync(submitted.Headers.Location);
        Assert.Contains("id=\"confirmed-count\">1", updated);
        var repeated = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, messageId, token));
        Assert.Equal(HttpStatusCode.Redirect, repeated.StatusCode);
        var replayHtml = await client.GetStringAsync(repeated.Headers.Location);
        Assert.Contains("nessun duplicato", replayHtml);
        Assert.Contains("id=\"confirmed-count\">1", replayHtml);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            Assert.Equal(1, await db.ParticipationReceipts.CountAsync());
            Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        }

        var logout = await client.PostAsync("/Access/Logout", Form(("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task InvalidMessageAndEventTamperingCannotWrite()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginToken = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", loginToken)));
        var token = Token(await client.GetStringAsync("/"));
        var bad = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, "invalid-message", token, "!presenza si 3"));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("Comando non supportato", await bad.Content.ReadAsStringAsync());
        var unauthorizedEvent = Guid.NewGuid();
        var forged = await client.PostAsync("/Home/Simulate", Simulation(unauthorizedEvent, "forged-message", token));
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Contains("/Access/Denied", forged.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(forged.Headers.Location)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().ParticipationReceipts.ToListAsync());
    }

    [Fact]
    public async Task StatusChangesAndOldReplayKeepDashboardConsistent()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginToken = Token(await client.GetStringAsync("/Access/Login"));
        var login = await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = Token(await client.GetStringAsync("/"));
        var steps = new[]
        {
            (Answer: "si", Label: "Confermato", Confirmed: 1, Undecided: 0, Absent: 0),
            (Answer: "forse", Label: "Indeciso", Confirmed: 0, Undecided: 1, Absent: 0),
            (Answer: "no", Label: "Assente", Confirmed: 0, Undecided: 0, Absent: 1),
            (Answer: "si", Label: "Confermato", Confirmed: 1, Undecided: 0, Absent: 0)
        };
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            var submitted = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, "cycle-" + i, token, "!presenza " + step.Answer));
            Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
            var html = await client.GetStringAsync(submitted.Headers.Location);
            Assert.Contains("Stato aggiornato: " + step.Label, html);
            AssertCounts(html, step.Confirmed, step.Undecided, step.Absent);
        }
        var oldReplay = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, "cycle-1", token, "!presenza forse"));
        Assert.Equal(HttpStatusCode.Redirect, oldReplay.StatusCode);
        var replayHtml = await client.GetStringAsync(oldReplay.Headers.Location);
        Assert.Contains("nessun duplicato", replayHtml);
        AssertCounts(replayHtml, 1, 0, 0);
        var conflict = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, "cycle-1", token, "!presenza no"));
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("dati diversi", conflictHtml);
        AssertCounts(conflictHtml, 1, 0, 0);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(4, await db.ParticipationReceipts.CountAsync());
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("forse", "Indeciso", 1, 0)]
    [InlineData("no", "Assente", 0, 1)]
    public async Task RepeatedStatusWithNewIdShowsUnchangedOutcome(string answer, string label, int undecided, int absent)
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginToken = Token(await client.GetStringAsync("/Access/Login"));
        var login = await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = Token(await client.GetStringAsync("/"));
        var first = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, "first", token, "!presenza " + answer));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var second = await client.PostAsync("/Home/Simulate", Simulation(factory.EventId, "second", token, "!presenza " + answer));
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        var html = await client.GetStringAsync(second.Headers.Location);
        Assert.Contains("Stato già impostato: " + label, WebUtility.HtmlDecode(html));
        Assert.Contains("Conteggi invariati", html);
        AssertCounts(html, 0, undecided, absent);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().ParticipationReceipts.CountAsync());
    }

    private static void AssertCounts(string html, int confirmed, int undecided, int absent)
    {
        Assert.Contains($"id=\"confirmed-count\">{confirmed}", html);
        Assert.Contains($"id=\"undecided-count\">{undecided}", html);
        Assert.Contains($"id=\"absent-count\">{absent}", html);
        Assert.Contains("id=\"no-response-count\">0", html);
        Assert.Contains("id=\"total-count\">1", html);
    }

    [Fact]
    public async Task LoginAttemptsAreRateLimited()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", "invalid-code"), ("__RequestVerificationToken", token)))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", "invalid-code"), ("__RequestVerificationToken", token)))).StatusCode);
    }

    private static FormUrlEncodedContent Simulation(Guid eventId, string id, string? token = null, string text = "!presenza si")
    {
        var values = new List<(string, string)> { ("Input.EventId", eventId.ToString()), ("Input.SenderId", "synthetic-http-sender"),
            ("Input.MessageId", id), ("Input.Text", text) };
        if (token is not null) values.Add(("__RequestVerificationToken", token));
        return Form(values.ToArray());
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Token anti-CSRF assente dal modulo.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    internal sealed class LocalWebFactory : WebApplicationFactory<Program>
    {
        public const string UserName = "organizer-test";
        public const string Password = "Synthetic-Test-Password1!";
        private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
        private readonly Guid organizerId;
        public Guid EventId { get; }
        public Guid OrganizerPersonId => organizerId;
        public Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor? SaveInterceptor { get; set; }

        public LocalWebFactory()
        {
            connection.Open();
            using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
            db.Database.Migrate();
            var ev = new Event("Evento test HTTP", DateTime.UtcNow);
            var organizer = new Person("Organizzatore test HTTP");
            var person = new Person("Partecipante test HTTP", "synthetic-http-sender");
            organizerId = organizer.Id;
            EventId = ev.Id;
            db.AddRange(ev, organizer, person, new EventOrganizer(ev.Id, organizer.Id), new EventParticipant(ev.Id, person.Id));
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["LocalScheduler:Enabled"] = "false", ["WhatsAppGateway:Enabled"] = "false" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TRobotDbContext>();
                services.RemoveAll<DbContextOptions<TRobotDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<TRobotDbContext>>();
                services.AddDbContext<TRobotDbContext>(options =>
                {
                    options.UseSqlite(connection);
                    if (SaveInterceptor is not null) options.AddInterceptors(SaveInterceptor);
                });
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<OrganizerUser>>();
            var user = new OrganizerUser
            {
                Id = Guid.NewGuid(), PersonId = organizerId, UserName = UserName,
                IsSystemAdministrator = true, LockoutEnabled = true
            };
            var result = users.CreateAsync(user, Password).GetAwaiter().GetResult();
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(x => x.Code)));
            return host;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
