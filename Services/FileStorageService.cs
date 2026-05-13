using Microsoft.Extensions.Options;

namespace Cameramg.Services;

public class FileStorageService(IWebHostEnvironment env, IOptions<StorageOptions> options)
{
    private readonly StorageOptions _opt = options.Value;

    public async Task<(string nome, string caminho, string extensao, string mime, long tamanho)> SalvarAsync(
        IFormFile file,
        string pasta,
        CancellationToken ct)
    {
        if (file is null) throw new InvalidOperationException("Arquivo não enviado.");
        if (file.Length <= 0) throw new InvalidOperationException("Arquivo vazio.");

        var max = _opt.MaxFileSizeMb * 1024L * 1024L;
        if (file.Length > max)
            throw new InvalidOperationException($"Arquivo excede o limite de {_opt.MaxFileSizeMb} MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(ext) || !_opt.AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Extensão não permitida: {ext}");

        var safeFolder = string.IsNullOrWhiteSpace(pasta)
            ? "geral"
            : pasta.Replace("..", "").Trim('/', '\\');

        if (string.IsNullOrWhiteSpace(safeFolder))
            safeFolder = "geral";

        var storageRoot = ResolverStorageRoot();

        var folder = Path.Combine(storageRoot, safeFolder);
        Directory.CreateDirectory(folder);

        var nome = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(folder, nome);

        await using var stream = File.Create(full);
        await file.CopyToAsync(stream, ct);

        var baseUrl = string.IsNullOrWhiteSpace(_opt.BaseUrl)
            ? "/uploads"
            : "/" + _opt.BaseUrl.Trim('/');

        var caminho = $"{baseUrl}/{safeFolder.Replace("\\", "/")}/{nome}";

        return (nome, caminho, ext, file.ContentType, file.Length);
    }

    private string ResolverStorageRoot()
    {
        var basePath = string.IsNullOrWhiteSpace(_opt.BasePath)
            ? "uploads"
            : _opt.BasePath.Trim();

        if (Path.IsPathRooted(basePath))
            return Path.GetFullPath(basePath);

        return Path.GetFullPath(
            Path.Combine(
                env.ContentRootPath,
                "src",
                basePath.Trim('/', '\\')
            )
        );
    }

}