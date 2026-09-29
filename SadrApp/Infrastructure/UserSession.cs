namespace SadrApp.Infrastructure;

using SadrApp.Data;

/// <summary>Holds the signed-in application user for the whole session.</summary>
public static class UserSession
{
    public static Guid? CurrentUserId { get; private set; }
    public static string? Username { get; private set; }
    public static string? Role { get; private set; }
    public static bool IsLoggedIn => CurrentUserId is not null;

    /// <summary>True for Admin (always) or the exact role; roles are plain strings in Users.Role.</summary>
    public static bool HasRole(string role) =>
        Role == RoleConsts.Admin || Role == role;

    /// <summary>Accepting a transfer as really done — warehouse keeper or admin only.</summary>
    public static bool CanAcceptTransfers => HasRole(RoleConsts.Warehouse);

    /// <summary>Warehouse page: registering transfers (keepers, admins, and general staff).</summary>
    public static bool CanManageWarehouse =>
        HasRole(RoleConsts.Warehouse) || Role == "User";

    /// <summary>Invoice registration (sales person, admin, or general staff).</summary>
    public static bool CanRegisterInvoices =>
        HasRole(RoleConsts.Sales) || Role == "User";

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
