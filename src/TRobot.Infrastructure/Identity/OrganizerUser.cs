using Microsoft.AspNetCore.Identity;

namespace TRobot.Infrastructure.Identity;

public sealed class OrganizerUser : IdentityUser<Guid>
{
    public Guid PersonId { get; set; }
    public bool IsSystemAdministrator { get; set; }
    public bool IsEnabled { get; set; } = true;
}
