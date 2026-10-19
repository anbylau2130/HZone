using System.Security.Cryptography;
using System.Text;

namespace HZone.Api.Authentication;

/// <summary>
/// PAT 明文格式与哈希的唯一出口：明文 = hz_ + 22 位 base62，落库仅存 SHA-256 hex。
/// </summary>
public static class PatToken
{
    public const string Prefix = "hz_";

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int BodyLength = 22;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(BodyLength);
        return Prefix + new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
    }

    public static string Sha256Hex(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
