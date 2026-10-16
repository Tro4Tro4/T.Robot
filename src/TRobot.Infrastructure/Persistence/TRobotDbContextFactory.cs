using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TRobot.Infrastructure.Persistence;

public sealed class TRobotDbContextFactory : IDesignTimeDbContextFactory<TRobotDbContext>
{
    public TRobotDbContext CreateDbContext(string[] args)
    {
        // Il default coincide con quello Web anche se il comando parte dalla radice.
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TRobot.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Eseguire dotnet ef dalla cartella della soluzione o da una sottocartella.");
        var webRoot = Path.Combine(root.FullName, "src", "TRobot.Web");
        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(webRoot, "trobot.db"), ForeignKeys = true
        };
        return new(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection.ToString()).Options);
    }
}
