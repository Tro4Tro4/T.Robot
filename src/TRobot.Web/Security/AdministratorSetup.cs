using System.Security.Cryptography;
using System.Text.Json;
using TRobot.Application.Accounts;

namespace TRobot.Web.Security;

public static class AdministratorSetup
{
    public static string CredentialsPath(string contentRoot) => Path.GetFullPath(Path.Combine(contentRoot, "../../.local/identity-admin.txt"));

    public static Guid ReadLegacyPersonId(string contentRoot)
    {
        var path = Path.GetFullPath(Path.Combine(contentRoot, "../../.local/local-access.json"));
        if (!File.Exists(path)) throw new InvalidOperationException("Configurazione della demo precedente assente. Per un database vuoto usa --initialize-demo.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("OrganizerPersonId").GetGuid();
    }

    public static async Task InitializeAsync(AccountService accounts, string contentRoot, Guid personId)
    {
        var path = CredentialsPath(contentRoot);
        if (File.Exists(path)) throw new InvalidOperationException("Il file delle credenziali Identity esiste già e non viene sovrascritto.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        // Riserva il file prima di modificare il DB, così errori di accesso al filesystem non creano account inutilizzabili.
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            await accounts.BootstrapAsync(true, personId, "admin", password);
        }
        catch
        {
            await stream.DisposeAsync();
            File.Delete(path); // Solo il file appena creato e ancora vuoto da questa operazione.
            throw;
        }
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync("Nome utente: admin" + Environment.NewLine + "Password: " + password + Environment.NewLine);
    }
}
