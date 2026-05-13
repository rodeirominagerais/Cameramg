using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cameramg.Data;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/backups")]
[Authorize(Policy = "Admin")]
public class BackupsController(AppDbContext db, IWebHostEnvironment env, IConfiguration config) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [HttpGet("diagnostico")]
    public IActionResult Diagnostico()
    {
        var roots = UploadRoots().Select(x => new
        {
            caminho = x,
            existe = Directory.Exists(x),
            totalArquivos = Directory.Exists(x) ? Directory.EnumerateFiles(x, "*.*", SearchOption.AllDirectories).Count() : 0
        });

        return Ok(new
        {
            contentRoot = env.ContentRootPath,
            webRoot = env.WebRootPath,
            storageBasePath = config["Storage:BasePath"] ?? config["Storage__BasePath"] ?? "uploads",
            storageBaseUrl = config["Storage:BaseUrl"] ?? config["Storage__BaseUrl"] ?? "/uploads",
            uploadRoots = roots
        });
    }

    [HttpGet("gerar")]
    public async Task<IActionResult> Gerar(CancellationToken ct)
    {
        var nomeBackup = $"backup-total-portal-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        var temp = Path.Combine(Path.GetTempPath(), nomeBackup);
        if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);

        var manifest = new List<object>();
        var arquivosNaoEncontrados = new List<object>();

        await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            await AddJson(zip, "manifesto.json", new
            {
                geradoEmUtc = DateTime.UtcNow,
                descricao = "Backup total do portal: banco em JSON + pacotes individuais por publicação + arquivos atrelados.",
                rotasIncluidas = new[]
                {
                    "/api/publicacoes",
                    "/api/licitacoes",
                    "/api/editais/processos-seletivos",
                    "/api/editais/concursos",
                    "/api/leis",
                    "/api/atividade-parlamentar/{categoria}",
                    "/api/paginas",
                    "/api/submenus"
                }
            }, ct);

            await BackupBancoJson(zip, ct);

            foreach (var item in await MontarPublicacoes(ct))
            {
                var pacoteNome = $"pacotes/{Safe(item.Tipo)}/{Safe(item.Titulo)}-{item.Id}.zip";
                await AddPackage(zip, pacoteNome, item, manifest, arquivosNaoEncontrados, ct);
            }

            await AddJson(zip, "relatorio/pacotes-gerados.json", manifest, ct);
            await AddJson(zip, "relatorio/arquivos-nao-encontrados.json", arquivosNaoEncontrados, ct);
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(temp, ct);
        System.IO.File.Delete(temp);
        return File(bytes, "application/zip", nomeBackup);
    }

    private async Task BackupBancoJson(ZipArchive zip, CancellationToken ct)
    {
        await AddJson(zip, "banco/publicacoes.json", await db.Publicacoes.AsNoTracking().Include(x => x.Arquivos).Include(x => x.Imagens).ToListAsync(ct), ct);
        await AddJson(zip, "banco/licitacoes.json", await db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
        await AddJson(zip, "banco/processos-seletivos.json", await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
        await AddJson(zip, "banco/concursos.json", await db.Concursos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
        await AddJson(zip, "banco/admin-registros.json", await db.AdminRegistros.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/submenus.json", await db.SubmenuPaginas.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/paginas-institucionais.json", await db.PaginasInstitucionais.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/arquivos.json", await db.Arquivos.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/imagens.json", await db.Imagens.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/atas-reunioes.json", await db.AtasReunioes.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/portarias.json", await db.Portarias.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/requerimentos.json", await db.Requerimentos.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/convocacoes.json", await db.Convocacoes.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/indicacoes.json", await db.Indicacoes.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/mocoes.json", await db.Mocoes.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/resolucoes.json", await db.Resolucoes.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/projetos-resolucoes.json", await db.ProjetosResolucao.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/diplomas.json", await db.Diplomas.AsNoTracking().ToListAsync(ct), ct);
        await AddJson(zip, "banco/decretos.json", await db.Decretos.AsNoTracking().ToListAsync(ct), ct);
    }

    private async Task<List<BackupItem>> MontarPublicacoes(CancellationToken ct)
    {
        var lista = new List<BackupItem>();

        var publicacoes = await db.Publicacoes.AsNoTracking().Include(x => x.Arquivos).Include(x => x.Imagens).ToListAsync(ct);
        lista.AddRange(publicacoes.Select(x => new BackupItem("publicacoes", x.Id, x.Titulo, x, Paths(x.ImagemCapa).Concat(x.Arquivos.Select(a => a.CaminhoRelativo)).Concat(x.Imagens.Select(i => i.CaminhoRelativo)).ToList())));

        var licitacoes = await db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct);
        lista.AddRange(licitacoes.Select(x => new BackupItem("licitacoes", x.Id, TituloLicitacao(x), x, x.Arquivos.Select(a => a.CaminhoRelativo).ToList())));

        var processos = await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct);
        lista.AddRange(processos.Select(x => new BackupItem("editais-processos-seletivos", x.Id, x.Titulo, x, x.Arquivos.Select(a => a.CaminhoRelativo).ToList())));

        var concursos = await db.Concursos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct);
        lista.AddRange(concursos.Select(x => new BackupItem("editais-concursos", x.Id, x.Titulo, x, x.Arquivos.Select(a => a.CaminhoRelativo).ToList())));

        AddAtividade(lista, "atas-reunioes", await db.AtasReunioes.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "portarias", await db.Portarias.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "requerimentos", await db.Requerimentos.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "convocacoes", await db.Convocacoes.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "indicacoes", await db.Indicacoes.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "mocoes", await db.Mocoes.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "resolucoes", await db.Resolucoes.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "projetos-resolucoes", await db.ProjetosResolucao.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "diplomas", await db.Diplomas.AsNoTracking().ToListAsync(ct));
        AddAtividade(lista, "decretos", await db.Decretos.AsNoTracking().ToListAsync(ct));

        var admin = await db.AdminRegistros.AsNoTracking().ToListAsync(ct);
        lista.AddRange(admin.Select(x => new BackupItem($"admin-registros-{Safe(x.Tipo)}", x.Id, x.Titulo, x, ExtractPathsFromText(x.DadosJson))));

        var submenus = await db.SubmenuPaginas.AsNoTracking().ToListAsync(ct);
        lista.AddRange(submenus.Select(x => new BackupItem("submenus-paginas", x.Id, x.Titulo, x, Paths(x.Imagem, x.Arquivo).Concat(ExtractPathsFromText(x.ConteudoHtml)).ToList())));

        var paginas = await db.PaginasInstitucionais.AsNoTracking().ToListAsync(ct);
        lista.AddRange(paginas.Select(x => new BackupItem("paginas-institucionais", x.Id, x.Titulo, x, Paths(x.ImagemCapa).Concat(ExtractPathsFromText(x.DadosJson)).Concat(ExtractPathsFromText(x.ConteudoHtml)).ToList())));

        return lista;
    }

    private static void AddAtividade<T>(List<BackupItem> lista, string tipo, List<T> itens) where T : AtividadeParlamentarBase
    {
        lista.AddRange(itens.Select(x => new BackupItem($"atividade-parlamentar-{tipo}", x.Id, x.Titulo, x, Paths(x.Arquivo).Concat(ExtractPathsFromText(x.Conteudo)).ToList())));
    }

    private async Task AddPackage(ZipArchive zip, string pacoteNome, BackupItem item, List<object> manifest, List<object> arquivosNaoEncontrados, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        using (var pacote = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddJson(pacote, "dados.json", item.Dados, ct);
            await AddJson(pacote, "metadados.json", new { item.Tipo, item.Id, item.Titulo, arquivos = item.Arquivos }, ct);

            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var caminho in item.Arquivos.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var full = ResolveFile(caminho);
                if (full is null)
                {
                    arquivosNaoEncontrados.Add(new { item.Tipo, item.Id, item.Titulo, caminho });
                    continue;
                }

                var nome = Path.GetFileName(full);
                var destino = $"arquivos/{nome}";
                var n = 1;
                while (!usados.Add(destino))
                {
                    destino = $"arquivos/{Path.GetFileNameWithoutExtension(nome)}-{n++}{Path.GetExtension(nome)}";
                }

                pacote.CreateEntryFromFile(full, destino, CompressionLevel.Optimal);
            }
        }

        ms.Position = 0;
        var entry = zip.CreateEntry(pacoteNome, CompressionLevel.Optimal);
        await using var es = entry.Open();
        await ms.CopyToAsync(es, ct);

        manifest.Add(new { item.Tipo, item.Id, item.Titulo, pacote = pacoteNome, totalArquivosReferenciados = item.Arquivos.Count });
    }

    private static async Task AddJson(ZipArchive zip, string entryName, object? value, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
    }

    private string? ResolveFile(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        var clean = caminho.Trim().Replace("\\", "/");
        if (Uri.TryCreate(clean, UriKind.Absolute, out var uri)) clean = uri.AbsolutePath;
        clean = clean.Split('?', '#')[0].TrimStart('/');
        if (clean.Contains("..")) return null;
        if (clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)) clean = clean[8..];

        foreach (var root in UploadRoots().Where(Directory.Exists))
        {
            var full = Path.GetFullPath(Path.Combine(root, clean));
            if (full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(full)) return full;

            var fileName = Path.GetFileName(clean);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var found = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        }
        return null;
    }

    private string[] UploadRoots()
    {
        var webRoot = string.IsNullOrWhiteSpace(env.WebRootPath) ? Path.Combine(env.ContentRootPath, "wwwroot") : env.WebRootPath;
        var storage = config["Storage:BasePath"] ?? config["Storage__BasePath"] ?? "uploads";
        var configured = Path.IsPathRooted(storage) ? storage : Path.Combine(webRoot, storage.Trim('/', '\\'));
        return new[]
        {
            configured,
            Path.Combine(env.ContentRootPath, "uploads"),
            Path.Combine(webRoot, "uploads"),
            "/var/data/uploads",
            Path.Combine(env.ContentRootPath, "src", "uploads")
        }.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> Paths(params string?[] values)
        => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!);

    private static List<string> ExtractPathsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        const string pattern = "(?i)(/uploads/[^\\\"'\\s<>]+|uploads/[^\\\"'\\s<>]+|https?://[^\\\"'\\s<>]+/uploads/[^\\\"'\\s<>]+)";

        return Regex.Matches(text, pattern)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TituloLicitacao(Licitacao x)
        => !string.IsNullOrWhiteSpace(x.Objeto) ? x.Objeto : $"Licitação {x.NumeroLicitacao ?? x.Numero ?? x.Id.ToString()}";

    private static string Safe(string? value)
    {
        var s = string.IsNullOrWhiteSpace(value) ? "sem-titulo" : value.Trim().ToLowerInvariant();
        s = s.Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
             .Replace("é", "e").Replace("ê", "e")
             .Replace("í", "i")
             .Replace("ó", "o").Replace("ô", "o").Replace("õ", "o")
             .Replace("ú", "u").Replace("ç", "c");
        s = Regex.Replace(s, "[^a-z0-9\\-_]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "sem-titulo" : s[..Math.Min(s.Length, 90)];
    }

    private sealed record BackupItem(string Tipo, long Id, string Titulo, object Dados, List<string> Arquivos);
}
