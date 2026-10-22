namespace HZone.Api;

/// <summary>
/// 认证配置的单一读取入口。JWT 签名键此前在 Program.cs 与 AuthEndpoints.MintJwt
/// 各自内联回退默认键，生产漏配可被伪造 token——收敛到一处，生产未配置启动即抛（fail-fast）。
/// </summary>
public static class AuthConfig
{
    public const string DevDefaultJwtKey = "dev-only-insecure-jwt-key-0246802468";

    /// <summary>
    /// 读取 JWT 签名键（单一来源）：dev 未配置用默认键；生产未配置直接抛异常拒绝启动。
    /// </summary>
    public static string GetJwtKey(IConfiguration config, bool isProduction) =>
        config["HZone:Auth:JwtKey"]
        ?? (!isProduction
            ? DevDefaultJwtKey
            : throw new InvalidOperationException(
                "HZone:Auth:JwtKey 未配置：生产环境必须显式配置 JWT 签名键，否则 token 可被伪造"));
}
