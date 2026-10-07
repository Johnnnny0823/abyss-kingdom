using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace AbyssKingdom.Server.Auth;

public sealed record AuthResult(string PlayerId);

/// <summary>驗證客戶端送來的登入憑證，回傳玩家 ID；失敗回傳 null。</summary>
public interface IAuthVerifier
{
    Task<AuthResult?> VerifyAsync(string token, CancellationToken ct);
}

/// <summary>
/// 本機開發用：token 格式為 "dev:任意名字"，名字就是玩家 ID。
/// 正式環境務必把 Auth:Mode 設成 Firebase。
/// </summary>
public sealed class DevAuthVerifier : IAuthVerifier
{
    public Task<AuthResult?> VerifyAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("dev:", StringComparison.Ordinal))
            return Task.FromResult<AuthResult?>(null);
        var name = token[4..].Trim();
        if (name.Length is 0 or > 32) return Task.FromResult<AuthResult?>(null);
        return Task.FromResult<AuthResult?>(new AuthResult("dev-" + name));
    }
}

/// <summary>
/// 驗證 Firebase Auth 的 ID Token（RS256 JWT），不依賴任何外部套件。
/// 依 Firebase 官方規則檢查：簽章、alg、aud = 專案 ID、iss、exp、iat、sub。
/// 需要設定 Firebase:ProjectId。
/// </summary>
public sealed class FirebaseAuthVerifier : IAuthVerifier
{
    private const string CertUrl = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";

    private readonly IHttpClientFactory _http;
    private readonly string _projectId;
    private readonly ILogger<FirebaseAuthVerifier> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, RSA> _keys = new();
    private DateTimeOffset _keysExpireAt = DateTimeOffset.MinValue;

    public FirebaseAuthVerifier(IHttpClientFactory http, IConfiguration config, ILogger<FirebaseAuthVerifier> log)
    {
        _http = http;
        _log = log;
        _projectId = config["Firebase:ProjectId"]
            ?? throw new InvalidOperationException("Auth:Mode=Firebase 時必須設定 Firebase:ProjectId");
    }

    public async Task<AuthResult?> VerifyAsync(string token, CancellationToken ct)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "RS256") return null;
            var kid = header.RootElement.GetProperty("kid").GetString();
            if (kid == null) return null;

            var keys = await GetKeysAsync(ct);
            if (!keys.TryGetValue(kid, out var rsa)) return null;

            var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var sig = Base64UrlDecode(parts[2]);
            if (!rsa.VerifyData(signed, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var p = payload.RootElement;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const long skew = 60;
            if (p.GetProperty("aud").GetString() != _projectId) return null;
            if (p.GetProperty("iss").GetString() != "https://securetoken.google.com/" + _projectId) return null;
            if (p.GetProperty("exp").GetInt64() < now - skew) return null;
            if (p.GetProperty("iat").GetInt64() > now + skew) return null;
            var sub = p.GetProperty("sub").GetString();
            if (string.IsNullOrEmpty(sub) || sub.Length > 128) return null;

            return new AuthResult(sub);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException or CryptographicException)
        {
            _log.LogDebug(ex, "Invalid Firebase token");
            return null;
        }
    }

    private async Task<Dictionary<string, RSA>> GetKeysAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow < _keysExpireAt) return _keys;
        await _lock.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow < _keysExpireAt) return _keys;
            using var res = await _http.CreateClient().GetAsync(CertUrl, ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(ct);
            var certs = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            var keys = new Dictionary<string, RSA>();
            foreach (var (k, pem) in certs)
            {
                var cert = X509Certificate2.CreateFromPem(pem);
                var rsa = cert.GetRSAPublicKey();
                if (rsa != null) keys[k] = rsa;
            }
            var maxAge = res.Headers.CacheControl?.MaxAge ?? TimeSpan.FromHours(1);
            _keys = keys;
            _keysExpireAt = DateTimeOffset.UtcNow + maxAge;
            return _keys;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
