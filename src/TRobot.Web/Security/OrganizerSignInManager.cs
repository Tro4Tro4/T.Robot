using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TRobot.Infrastructure.Identity;

namespace TRobot.Web.Security;

public sealed class OrganizerSignInManager(UserManager<OrganizerUser> users, IHttpContextAccessor context,
    IUserClaimsPrincipalFactory<OrganizerUser> claims, IOptions<IdentityOptions> options,
    ILogger<SignInManager<OrganizerUser>> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<OrganizerUser> confirmation)
    : SignInManager<OrganizerUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(OrganizerUser user) => user.IsEnabled && await base.CanSignInAsync(user);
}
