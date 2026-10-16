using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TRobot.Application.Accounts;
using TRobot.Infrastructure.Identity;

namespace TRobot.Web.Security;

public sealed class OrganizerClaimsFactory(UserManager<OrganizerUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<OrganizerUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(OrganizerUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new(AccountClaims.PersonId, user.PersonId.ToString()));
        if (user.IsSystemAdministrator) identity.AddClaim(new(AccountClaims.SystemAdministrator, "true"));
        return identity;
    }
}
