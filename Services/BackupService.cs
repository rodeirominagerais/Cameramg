using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cameramg.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cameramg.Services;

public class BackupService(AppDbContext db, IWebHostEnvironment env, IOptions<StorageOptions> storageOptions)
{
    private readonly StorageOptions _storage = storageOptions.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public async Task<(byte[] Conteudo, string NomeArquivo)> GerarBackupCompletoAsync(CancellationToken ct = default)
    {
        await using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dataUtc = DateTime.UtcNow;

            await AddJsonAsync(zip, "manifesto.json", new
            {
                sistema = "Cameramg",
                tipo = "backup-completo",
                geradoEmUtc = dataUtc,
                inclui = new[] { "banco-de-dados-json", "uploads", "documentos", "imagens", "arquivos-publicos" }
            }, ct);

            await AddJsonAsync(zip, "banco/usuarios.json", await db.Usuarios.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/categorias.json", await db.Categorias.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/publicacoes.json", await db.Publicacoes.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/licitacoes.json", await db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/licitacao_arquivos.json", await db.LicitacaoArquivos.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/arquivos.json", await db.Arquivos.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/imagens.json", await db.Imagens.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/paginas_institucionais.json", await db.PaginasInstitucionais.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/telefones_uteis.json", await db.TelefonesUteis.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/ouvidoria_categorias.json", await db.OuvidoriaCategorias.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/ouvidoria_chamados.json", await db.OuvidoriaChamados.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/configuracoes_site.json", await db.ConfiguracoesSite.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/admin_registros.json", await db.AdminRegistros.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/submenu_paginas.json", await db.SubmenuPaginas.AsNoTracking().ToListAsync(ct), ct);

            await AddJsonAsync(zip, "banco/atividade_parlamentar/atas_reunioes.json", await db.AtasReunioes.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/portarias.json", await db.Portarias.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/requerimentos.json", await db.Requerimentos.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/convocacoes.json", await db.Convocacoes.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/indicacoes.json", await db.Indicacoes.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/mocoes.json", await db.Mocoes.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/resolucoes.json", await db.Resolucoes.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/projetos_resolucoes.json", await db.ProjetosResolucao.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/diplomas.json", await db.Diplomas.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/atividade_parlamentar/decretos.json", await db.Decretos.AsNoTracking().ToListAsync(ct), ct);

            await AddJsonAsync(zip, "banco/editais/processos_seletivos.json", await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/editais/processos_seletivos_arquivos.json", await db.ProcessosSeletivosArquivos.AsNoTracking().ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/editais/concursos.json", await db.Concursos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
            await AddJsonAsync(zip, "banco/editais/concursos_arquivos.json", await db.ConcursosArquivos.AsNoTracking().ToListAsync(ct), ct);

            var pastas = ResolverPastasArquivos().ToList();
            await AddJsonAsync(zip, "arquivos/pastas_incluidas.json", pastas.Select(x => new { caminho = x }).ToList(), ct);

            var entradasAdicionadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pasta in pastas)
                AddDirectory(zip, pasta, $"arquivos/{Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}", entradasAdicionadas);
        }

        var nome = $"backup-cameramg-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        return (ms.ToArray(), nome);
    }

    public object Diagnostico()
    {
        var pastas = ResolverPastasArquivos().ToList();
        return new
        {
            contentRoot = env.ContentRootPath,
            webRoot = env.WebRootPath,
            storageBasePath = _storage.BasePath,
            storageBaseUrl = _storage.BaseUrl,
            pastasEncontradas = pastas,
            totalPastasEncontradas = pastas.Count
        };
    }

    private IEnumerable<string> ResolverPastasArquivos()
    {
        var candidatos = new List<string>();
        var basePath = string.IsNullOrWhiteSpace(_storage.BasePath) ? "uploads" : _storage.BasePath.Trim('/', '\\');

        if (Path.IsPathRooted(basePath))
            candidatos.Add(basePath);
        else
        {
            candidatos.Add(Path.Combine(env.ContentRootPath, basePath));
            candidatos.Add(Path.Combine(env.ContentRootPath, "src", basePath));
            candidatos.Add(Path.Combine(env.ContentRootPath, "wwwroot", "uploads"));
            candidatos.Add(Path.Combine(env.ContentRootPath, "src", "uploads"));
            candidatos.Add(Path.Combine(env.ContentRootPath, "public", "uploads"));
            candidatos.Add(Path.Combine(env.ContentRootPath, "src", "public", "uploads"));
        }

        if (!string.IsNullOrWhiteSpace(env.WebRootPath))
            candidatos.Add(Path.Combine(env.WebRootPath, "uploads"));

        return candidatos
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task AddJsonAsync<T>(ZipArchive zip, string entryName, T dados, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, dados, JsonOptions, ct);
    }

    private static void AddDirectory(ZipArchive zip, string sourceDir, string zipRoot, HashSet<string> entradasAdicionadas)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            var entryName = $"{zipRoot}/{relative}";

            if (entryName.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
                entryName.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                entryName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                entryName.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!entradasAdicionadas.Add(entryName))
                continue;

            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }
}
