using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Cameramg.Services;

public class GoogleDriveBackupService(
    HttpClient http,
    IOptions<GoogleDriveBackupOptions> options,
    IWebHostEnvironment env,
    ILogger<GoogleDriveBackupService> logger)
{
    private readonly GoogleDriveBackupOptions _options = options.Value;
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";

    public bool IsOAuthMode => string.Equals(_options.Mode, "OAuth", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured
    {
        get
        {
            if (!_options.Enabled) return false;
            if (string.IsNullOrWhiteSpace(_options.FolderId)) return false;

            if (IsOAuthMode)
            {
                return !string.IsNullOrWhiteSpace(_options.ClientId)
                    && !string.IsNullOrWhiteSpace(_options.ClientSecret)
                    && !string.IsNullOrWhiteSpace(_options.RedirectUri)
                    && HasOAuthToken();
            }

            return TryReadServiceAccountCredentials(out _, out _);
        }
    }

    public object Status()
        => new
        {
            habilitado = _options.Enabled,
            modo = IsOAuthMode ? "OAuth" : "ServiceAccount",
            folderIdConfigurado = !string.IsNullOrWhiteSpace(_options.FolderId),
            clientIdConfigurado = !string.IsNullOrWhiteSpace(_options.ClientId),
            redirectUri = _options.RedirectUri,
            tokenSalvo = HasOAuthToken(),
            configuradoParaUpload = IsConfigured,
            tokenStoragePath = ResolvePath(_options.TokenStoragePath ?? "App_Data/google-drive-token.json")
        };

    public string BuildAuthorizationUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("GoogleDriveBackup:ClientId não configurado.");

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
            throw new InvalidOperationException("GoogleDriveBackup:RedirectUri não configurado.");

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        return "https://accounts.google.com/o/oauth2/v2/auth?" +
               string.Join("&", query.Select(x => $"{WebUtility.UrlEncode(x.Key)}={WebUtility.UrlEncode(x.Value)}"));
    }

    public async Task<object> ExchangeCodeAndSaveTokenAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Código OAuth não informado.", nameof(code));

        var body = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _options.ClientId ?? throw new InvalidOperationException("GoogleDriveBackup:ClientId não configurado."),
            ["client_secret"] = _options.ClientSecret ?? throw new InvalidOperationException("GoogleDriveBackup:ClientSecret não configurado."),
            ["redirect_uri"] = _options.RedirectUri ?? throw new InvalidOperationException("GoogleDriveBackup:RedirectUri não configurado."),
            ["grant_type"] = "authorization_code"
        };

        using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(body), ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao trocar código OAuth por token: {(int)res.StatusCode} - {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var token = new GoogleOAuthToken
        {
            AccessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null,
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            TokenType = root.TryGetProperty("token_type", out var type) ? type.GetString() : "Bearer",
            Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() : DriveScope,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600)
        };

        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Google não retornou access_token.");

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            var old = await ReadOAuthTokenAsync(ct);
            token.RefreshToken = old?.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Google não retornou refresh_token. Acesse /api/google-drive/login novamente e autorize com prompt=consent.");

        await SaveOAuthTokenAsync(token, ct);

        return new
        {
            sucesso = true,
            mensagem = "Google Drive conectado com sucesso. O token foi salvo no backend.",
            expiraEmUtc = token.ExpiresAtUtc,
            folderIdConfigurado = !string.IsNullOrWhiteSpace(_options.FolderId)
        };
    }

    public async Task<object> UploadAsync(string filePath, string fileName, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo de backup não encontrado.", filePath);

        var token = IsOAuthMode
            ? await GetOAuthAccessTokenAsync(ct)
            : await GetServiceAccountAccessTokenAsync(ct);

        using var form = new MultipartFormDataContent();
        var metadata = new
        {
            name = fileName,
            parents = string.IsNullOrWhiteSpace(_options.FolderId) ? Array.Empty<string>() : new[] { _options.FolderId }
        };

        form.Add(new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json"), "metadata");

        await using var fileStream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(fileContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,webViewLink,webContentLink,size,createdTime");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = form;

        using var res = await http.SendAsync(req, ct);
        var responseBody = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao enviar backup para o Google Drive: {(int)res.StatusCode} - {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        return new
        {
            id = root.TryGetProperty("id", out var id) ? id.GetString() : null,
            nome = root.TryGetProperty("name", out var name) ? name.GetString() : fileName,
            link = root.TryGetProperty("webViewLink", out var link) ? link.GetString() : null,
            tamanho = root.TryGetProperty("size", out var size) ? size.GetString() : null,
            criadoEm = root.TryGetProperty("createdTime", out var created) ? created.GetString() : null
        };
    }

    private async Task<string> GetOAuthAccessTokenAsync(CancellationToken ct)
    {
        var token = await ReadOAuthTokenAsync(ct)
            ?? throw new InvalidOperationException("Google Drive ainda não foi conectado. Acesse /api/google-drive/login e autorize a conta Google.");

        if (!string.IsNullOrWhiteSpace(token.AccessToken) && token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            return token.AccessToken;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Refresh token não encontrado. Acesse /api/google-drive/login novamente.");

        var body = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId ?? throw new InvalidOperationException("GoogleDriveBackup:ClientId não configurado."),
            ["client_secret"] = _options.ClientSecret ?? throw new InvalidOperationException("GoogleDriveBackup:ClientSecret não configurado."),
            ["refresh_token"] = token.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(body), ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao renovar token do Google Drive: {(int)res.StatusCode} - {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        token.AccessToken = root.GetProperty("access_token").GetString();
        token.TokenType = root.TryGetProperty("token_type", out var type) ? type.GetString() : "Bearer";
        token.Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() : token.Scope;
        token.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600);

        await SaveOAuthTokenAsync(token, ct);

        return token.AccessToken ?? throw new InvalidOperationException("Google não retornou access_token ao renovar token.");
    }

    private async Task<GoogleOAuthToken?> ReadOAuthTokenAsync(CancellationToken ct)
    {
        var path = ResolvePath(_options.TokenStoragePath ?? "App_Data/google-drive-token.json");
        if (!File.Exists(path)) return null;

        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GoogleOAuthToken>(fs, cancellationToken: ct);
    }

    private async Task SaveOAuthTokenAsync(GoogleOAuthToken token, CancellationToken ct)
    {
        var path = ResolvePath(_options.TokenStoragePath ?? "App_Data/google-drive-token.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(fs, token, new JsonSerializerOptions { WriteIndented = true }, ct);
    }

    private bool HasOAuthToken()
    {
        var path = ResolvePath(_options.TokenStoragePath ?? "App_Data/google-drive-token.json");
        return File.Exists(path);
    }

    private async Task<string> GetServiceAccountAccessTokenAsync(CancellationToken ct)
    {
        if (!TryReadServiceAccountCredentials(out var email, out var privateKey))
            throw new InvalidOperationException("GoogleDriveBackup Service Account não configurada. Informe ServiceAccountJson/ServiceAccountJsonPath ou ServiceAccountEmail + PrivateKey.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = email,
            scope = DriveScope,
            aud = "https://oauth2.googleapis.com/token",
            exp = now + 3600,
            iat = now
        }));

        var unsignedJwt = $"{header}.{payload}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey.Replace("\\n", "\n"));
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsignedJwt), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var assertion = $"{unsignedJwt}.{Base64Url(signature)}";

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion
        });

        using var res = await http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao autenticar Service Account no Google Drive: {(int)res.StatusCode} - {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google não retornou access_token.");
    }

    private bool TryReadServiceAccountCredentials(out string email, out string privateKey)
    {
        email = _options.ServiceAccountEmail ?? "";
        privateKey = _options.PrivateKey ?? "";

        var json = _options.ServiceAccountJson;
        if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(_options.ServiceAccountJsonPath))
        {
            var path = ResolvePath(_options.ServiceAccountJsonPath);
            if (File.Exists(path)) json = File.ReadAllText(path);
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            email = root.TryGetProperty("client_email", out var ce) ? ce.GetString() ?? email : email;
            privateKey = root.TryGetProperty("private_key", out var pk) ? pk.GetString() ?? privateKey : privateKey;
        }

        return !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(privateKey);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = "App_Data/google-drive-token.json";
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class GoogleOAuthToken
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("expires_at_utc")]
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}
