using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("T.Robot: prototipo solo Development. Introdurre autenticazione prima del deploy.");

// Log strutturati su console, senza richiedere accesso al registro eventi Windows.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5238));
builder.Services.AddControllersWithViews();
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("TRobot")
    ?? throw new InvalidOperationException("ConnectionStrings:TRobot mancante."));
if (!Path.IsPathRooted(connection.DataSource))
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
connection.ForeignKeys = true;
builder.Services.AddDbContext<TRobotDbContext>(options => options.UseSqlite(connection.ToString()));

var app = builder.Build();
app.UseRouting();
app.MapStaticAssets();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
