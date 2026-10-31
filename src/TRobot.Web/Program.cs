using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using System.Threading.RateLimiting;
using TRobot.Application.Attendance;
using TRobot.Application.Demo;
using TRobot.Application.Accounts;
using TRobot.Application.Accounting;
using TRobot.Application.Events;
using TRobot.Application.History;
using TRobot.Application.Households;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Application.Polls;
using TRobot.Application.Games;
using TRobot.Application.Messaging;
using TRobot.Application.Tasks;
using TRobot.Application.Simulation;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;
using TRobot.Web.Security;
using TRobot.Web.Services;
using TRobot.Application.WhatsApp;
using TRobot.Infrastructure.WhatsApp;

var initializeDemo = args.Contains("--initialize-demo", StringComparer.Ordinal);
var initializeIdentity = args.Contains("--initialize-identity", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--initialize-demo" && x != "--initialize-identity").ToArray());
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsProduction())
    throw new InvalidOperationException("T.Robot: sono supportati solo Development e Production.");
HostingProductionConfiguration? production = null;
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".local", "whatsapp", "gateway.json")), optional: true, reloadOnChange: false);
else
    production = HostingProductionConfiguration.Load(builder.Configuration,
        builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), initializeDemo, initializeIdentity);

// Log strutturati su console, senza richiedere accesso al registro eventi Windows.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (production is null)
    builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5238));
else
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        // Ignore alternate endpoints supplied through Kestrel configuration: this host exposes HTTPS only.
        options.Configure(new ConfigurationBuilder().Build());
        options.AddServerHeader = false;
        options.Listen(production.ListenAddress, production.HttpsPort, listen => listen.UseHttps(https =>
        {
            https.ServerCertificate = production.Certificate;
            https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        }));
    });
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(production.DataProtectionKeysPath))
        .SetApplicationName("TRobot");
    builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
    builder.Services.AddHsts(options => { options.MaxAge = TimeSpan.FromDays(30); options.IncludeSubDomains = false; });
}
builder.Services.AddSingleton(sp => PortalModules.Load(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddControllersWithViews(options => options.Filters.Add<PortalModuleFilter>());
if (production is null) builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(WhatsAppHostingConfiguration.Load(builder.Configuration, builder.Environment.IsDevelopment()));
builder.Services.AddScoped<IWhatsAppStore, WhatsAppStore>();
builder.Services.AddScoped<WhatsAppService>();
builder.Services.AddHttpClient<IWhatsAppGateway, WhatsAppGatewayClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddScoped<AttendanceStore>();
builder.Services.AddScoped<IAttendanceStore>(sp => sp.GetRequiredService<AttendanceStore>());
builder.Services.AddScoped<IDemoSetupStore>(sp => sp.GetRequiredService<AttendanceStore>());
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<SimulationCommandService>();
builder.Services.AddScoped<OperationalCommandService>();
builder.Services.AddScoped<PollQuizCommandService>();
builder.Services.AddScoped<OrganizerAttendanceService>();
builder.Services.AddScoped<DemoSetupService>();
builder.Services.AddScoped<IOrganizerAccountStore, OrganizerAccountStore>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<IEventManagementStore, EventManagementStore>();
builder.Services.AddScoped<EventManagementService>();
builder.Services.AddScoped<ParticipantNameService>();
builder.Services.AddScoped<IEventHistoryStore, EventHistoryStore>();
builder.Services.AddScoped<EventHistoryService>();
builder.Services.AddScoped<ILocalStore, LocalStore>();
builder.Services.AddScoped<LocalOperations>();
builder.Services.AddScoped<ILocalDashboardStore, LocalDashboardStore>();
builder.Services.AddScoped<LocalDashboardService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<HouseholdService>();
builder.Services.AddScoped<TRobot.Application.Lodging.LodgingService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<TRobot.Application.Menu.MealProposalService>();
builder.Services.AddScoped<PollService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<IAutomaticSchedulerStore, AutomaticSchedulerStore>();
if (builder.Environment.IsDevelopment() && !initializeDemo && !initializeIdentity)
    builder.Services.AddHostedService<AutomaticSchedulerWorker>();
builder.Services.AddIdentityCore<OrganizerUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<TRobotDbContext>().AddSignInManager<OrganizerSignInManager>().AddClaimsPrincipalFactory<OrganizerClaimsFactory>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".TRobot.Identity";
    options.Cookie.HttpOnly = true;
    if (production is not null) options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.LoginPath = "/Access/Login";
    options.AccessDeniedPath = "/Access/Denied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("whatsapp-incoming", _ => RateLimitPartition.GetFixedWindowLimiter("whatsapp-loopback", _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("login", _ => RateLimitPartition.GetFixedWindowLimiter("local-login", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
    }));
});
var connection = new SqliteConnectionStringBuilder(
    production?.ConnectionString ?? builder.Configuration.GetConnectionString("TRobot")
    ?? throw new InvalidOperationException("ConnectionStrings:TRobot mancante."));
if (!Path.IsPathRooted(connection.DataSource))
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
connection.ForeignKeys = true;
builder.Services.AddDbContext<TRobotDbContext>(options => options.UseSqlite(connection.ToString()));

var app = builder.Build();
// Validate the final host configuration, including test-host overrides, before accepting requests.
_ = app.Services.GetRequiredService<PortalModules>();
if (initializeDemo || initializeIdentity)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var organizerId = initializeDemo
            ? await scope.ServiceProvider.GetRequiredService<DemoSetupService>().InitializeAsync(localSetupAuthorized: true)
            : AdministratorSetup.ReadLegacyPersonId(builder.Environment.ContentRootPath);
        await AdministratorSetup.InitializeAsync(scope.ServiceProvider.GetRequiredService<AccountService>(), builder.Environment.ContentRootPath, organizerId);
        Console.WriteLine("Amministratore pronto. Credenziali nel file locale escluso da Git: " + AdministratorSetup.CredentialsPath(builder.Environment.ContentRootPath));
    }
    catch (Exception ex) when (ex is AccountValidationException or InvalidOperationException)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (production is not null)
{
    app.Lifetime.ApplicationStopped.Register(production.Certificate.Dispose);
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        await next();
    });
}
app.UseExceptionHandler("/Home/Error");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaticAssets().AllowAnonymous();
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();

public partial class Program;
