namespace SadrApp.Infrastructure;

/// <summary>Holds the signed-in application user for the whole session.</summary>
public static class UserSession
{
    public static Guid? CurrentUserId { get; private set; }
    public static string? Username { get; private set; }
    public static string? Role { get; private set; }
    public static bool IsLoggedIn => CurrentUserId is not null;

    public static void SignIn(Guid userId, string username, string role)
    {
        CurrentUserId = userId;
        Username = username;
        Role = role;
    }

    public static void SignOut()
    {
        CurrentUserId = null;
        Username = null;
        Role = null;
    }
}
