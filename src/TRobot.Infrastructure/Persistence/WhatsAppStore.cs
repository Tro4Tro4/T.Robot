using Microsoft.EntityFrameworkCore;
using TRobot.Application.WhatsApp;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class WhatsAppStore(TRobotDbContext db) : IWhatsAppStore
{
    public Task<bool> IsAdministratorAsync(Guid personId, CancellationToken ct) => db.Users.AnyAsync(x => x.PersonId == personId && x.IsEnabled && x.IsSystemAdministrator, ct);
    public async Task<IReadOnlyList<WhatsAppConnection>> ActiveConnectionsAsync(CancellationToken ct) => await db.Set<WhatsAppConnection>()
        .Where(x => x.Enabled).OrderBy(x => x.EventId).ToListAsync(ct);
    public Task<WhatsAppConnection?> ConnectionByGroupAsync(string groupId, CancellationToken ct) => db.Set<WhatsAppConnection>()
        .SingleOrDefaultAsync(x => x.Enabled && x.GroupId == groupId, ct);
    public async Task<IReadOnlyList<WhatsAppContactPair>> ActiveContactsAsync(CancellationToken ct)
    {
        var rows = await (from alias in db.Set<WhatsAppIdentityAlias>()
                          join person in db.People on alias.PersonId equals person.Id
                          where alias.IsActive
                          select new { person.DisplayName, alias.SenderHash }).Distinct().ToListAsync(ct);
        return rows.Select(x => new WhatsAppContactPair(x.DisplayName, x.SenderHash)).ToArray();
    }
    public Task<EventParticipant?> ParticipantAsync(Guid eventId, Guid personId, CancellationToken ct) => db.EventParticipants.SingleOrDefaultAsync(x => x.EventId == eventId && x.PersonId == personId, ct);
    public Task<WhatsAppIncomingReceipt?> ReceiptAsync(Guid eventId, string messageHash, CancellationToken ct) => db.Set<WhatsAppIncomingReceipt>().SingleOrDefaultAsync(x => x.EventId == eventId && x.MessageHash == messageHash, ct);
    public async Task<IReadOnlyList<LocalAudit>> AuditsSinceAsync(Guid eventId, string action, DateTime afterUtc, CancellationToken ct) => await db.Set<LocalAudit>()
        .AsNoTracking().Where(x => x.EventId == eventId && x.Action == action && x.AtUtc > afterUtc).ToListAsync(ct);
}
