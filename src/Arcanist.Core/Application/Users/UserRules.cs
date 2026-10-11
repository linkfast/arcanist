using System.Text.RegularExpressions;
using Arcanist.Core.Api;
using Arcanist.Core.Application;

namespace Arcanist.Core.Application.Users;

public static partial class UserRules
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 64;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 256;

    public static void ValidateUserName(string? userName, string field, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(userName))
            errors.Add(field, ErrorCodes.Required);
        else if (userName.Length < UserNameMinLength)
            errors.Add(field, ErrorCodes.TooShort);
        else if (userName.Length > UserNameMaxLength || !UserNamePattern().IsMatch(userName))
            errors.Add(field, ErrorCodes.InvalidFormat);
    }

    public static void ValidatePassword(string? password, string field, ValidationErrors errors)
    {
        if (string.IsNullOrEmpty(password))
            errors.Add(field, ErrorCodes.Required);
        else if (password.Length < PasswordMinLength)
            errors.Add(field, ErrorCodes.TooShort);
        else if (password.Length > PasswordMaxLength)
            errors.Add(field, ErrorCodes.InvalidFormat);
    }

    /// <summary>Letters (any script), digits, dot, underscore and hyphen.</summary>
    [GeneratedRegex(@"^[\p{L}\p{N}._-]+$")]
    private static partial Regex UserNamePattern();
}
