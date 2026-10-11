namespace Arcanist.Core.Auth;

/// <summary>Bound from <c>Arcanist:Auth</c>.</summary>
public sealed class AuthOptions
{
    public const string Section = "Arcanist:Auth";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Sliding: each refresh extends the session by this much.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How recently the password must have been entered for sensitive admin actions.</summary>
    public TimeSpan StepUpWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Failed logins per account before it is temporarily locked.</summary>
    public int MaxFailedLogins { get; set; } = 5;

    /// <summary>Login/refresh/setup requests per client IP per minute.</summary>
    public int RequestsPerMinute { get; set; } = 20;

    /// <summary>How long permission and session state is cached between database reads.</summary>
    public TimeSpan AccessCacheDuration { get; set; } = TimeSpan.FromSeconds(30);
}

public static class ArcanistClaims
{
    public const string UserId = "sub";
    public const string SessionId = "sid";
    public const string SecurityStamp = "sstamp";
    public const string AuthenticatedAt = "auth_time";
    public const string Name = "name";
}
