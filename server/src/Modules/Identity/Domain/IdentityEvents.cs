namespace JianghuGo.Modules.Identity.Domain;

/// <summary>Published by Identity (2.5 GDPR): other modules erase or anonymise what they hold about the account.</summary>
public static class IdentityEvents
{
    public const string AccountDeleted = "identity.account_deleted";

    public sealed record AccountDeletedV1(Guid AccountId);
}
