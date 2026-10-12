namespace JianghuGo.Modules.Identity.Application;

public sealed class IdentityModuleOptions
{
    public const string Section = "Identity";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
    public int MaxFailedLogins { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    public int PasswordMinLength { get; set; } = 10;
    public int PasswordMaxLength { get; set; } = 128;
}
