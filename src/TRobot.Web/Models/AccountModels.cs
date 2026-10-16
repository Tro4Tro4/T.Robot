using System.ComponentModel.DataAnnotations;
using TRobot.Application.Accounts;

namespace TRobot.Web.Models;

public sealed class CreateOrganizerInput
{
    [Required] public Guid EventId { get; set; }
    [Required, StringLength(100)] public string UserName { get; set; } = "";
    [Required, StringLength(150)] public string DisplayName { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
public sealed record AccountsViewModel(AccountDirectory Directory, CreateOrganizerInput Input);

public sealed class AssignmentInput
{
    [Required] public Guid PersonId { get; set; }
    [Required] public Guid EventId { get; set; }
    [Required] public bool? Active { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AccountEnabledInput
{
    [Required] public Guid PersonId { get; set; }
    [Required] public bool? Enabled { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class ChangePasswordInput
{
    [Required, StringLength(128), DataType(DataType.Password)] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
