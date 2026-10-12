using System.Security.Cryptography;
using System.Text;

namespace JianghuGo.Modules.Identity.Application;

/// <summary>
/// Opaque bearer tokens (256 bits from a CSPRNG). Prefixes make leaked tokens recognisable by secret
/// scanners. Only the SHA-256 hash is ever persisted. See docs/decisions/0003-opaque-session-tokens.md.
/// </summary>
internal static class Tokens
{
    public const string AccessPrefix = "jga_";
    public const string RefreshPrefix = "jgr_";

    public static string NewAccessToken() => AccessPrefix + RandomPart();
    public static string NewRefreshToken() => RefreshPrefix + RandomPart();

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string RandomPart() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
