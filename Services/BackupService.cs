using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cameramg.Data;
using Cameramg.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cameramg.Services;

public class BackupService(AppDbContext db, IWebHostEnvironment env, IOptions<StorageOptions> storageOptions)
{
    private readonly StorageOptions _storage = storageOptions.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<(byte[] Conteudo, string NomeArquivo)> GerarBackupCompletoAsync(CancellationToken ct = default)
    {
        var arquivo = await GerarBackupArquivoAsync(ct);
        var bytes = await File.ReadAllBytesAsync(arquivo.CaminhoArquivo, ct);
        try { File.Delete(arquivo.CaminhoArquivo); } catch { }
        return (bytes, arquivo.NomeArquivo);
    }

    public async Task<(string CaminhoArquivo, string NomeArquivo)> GerarBackupArquivoAsync(CancellationToken ct = default)
    {
        var nome = $"backup-total-cameramg-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        var dir = Path.Combine(env.ContentRootPath, "backups-temp");
        Directory.CreateDirectory(dir);
        var caminho = Path.Combine(dir, nome);
        if (File.Exists(caminho)) File.Delete(caminho);

        var modulos = new List<object>();
        var arquivosNaoEncontrados = new List<object>();

        await using var fs = new FileStream(caminho, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        await AddJsonAsync(zip, "manifesto.json", new
        {
            sistema = "Cameramg",
            tipo = "backup-total",
            geradoEmUtc = DateTime.UtcNow,
            inclui = new[]
            {
                "banco-de-dados-total-em-json",
                "todos-os-uploads",
                "imagens",
                "videos",
                "pdfs",
                "documentos",
                "pacote-zip-individual-por-modulo",
                "pacote-zip-individual-por-registro"
            },
            observacao = "Para backup automático no Google Drive, configure GoogleDriveBackup no appsettings/variáveis de ambiente e compartilhe a pasta do Drive com o e-mail da service account."
        }, ct);

        await BackupBancoCompleto(zip, ct);
        await BackupModulos(zip, modulos, arquivosNaoEncontrados, ct);
        await BackupUploadsInteiros(zip, ct);

        await AddJsonAsync(zip, "relatorio/modulos-gerados.json", modulos, ct);
        await AddJsonAsync(zip, "relatorio/arquivos-nao-encontrados.json", arquivosNaoEncontrados, ct);

        return (caminho, nome);
    }

    public async Task<object> GerarEEnviarGoogleDriveAsync(GoogleDriveBackupService drive, CancellationToken ct = default)
    {
        var arquivo = await GerarBackupArquivoAsync(ct);
        try
        {
            var driveResult = await drive.UploadAsync(arquivo.CaminhoArquivo, arquivo.NomeArquivo, ct);
            return new { mensagem = "Backup gerado e enviado ao Google Drive com sucesso.", arquivo.NomeArquivo, googleDrive = driveResult };
        }
        finally
        {
            if (File.Exists(arquivo.CaminhoArquivo)) File.Delete(arquivo.CaminhoArquivo);
        }
    }

    public Task AplicarRetencaoLocalAsync(int retentionDays, CancellationToken ct = default)
    {
        var dir = Path.Combine(env.ContentRootPath, "backups-temp");
        if (!Directory.Exists(dir)) return Task.CompletedTask;
        var limite = DateTime.UtcNow.AddDays(-Math.Max(retentionDays, 1));
        foreach (var file in Directory.EnumerateFiles(dir, "backup-total-cameramg-*.zip"))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetCreationTimeUtc(file) < limite)
            {
                try { File.Delete(file); } catch { }
            }
        }
        return Task.CompletedTask;
    }

    public object Diagnostico(bool googleDriveConfigurado = false)
    {
        var pastas = ResolverPastasArquivos().ToList();
        return new
        {
            contentRoot = env.ContentRootPath,
            webRoot = env.WebRootPath,
            storageBasePath = _storage.BasePath,
            storageBaseUrl = _storage.BaseUrl,
            googleDriveConfigurado,
            pastasEncontradas = pastas.Select(x => new
            {
                caminho = x,
                totalArquivos = Directory.Exists(x) ? Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories).Count() : 0,
                tamanhoBytes = Directory.Exists(x) ? Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0
            }),
            modulosIncluidos = new[]
            {
                "usuarios", "categorias", "noticias/publicacoes", "licitacoes", "editais/processos-seletivos", "editais/concursos",
                "a-camara/estrutura-administrativa", "a-camara/calendario-de-reunioes", "documentos", "imagens", "videos/uploads",
                "paginas", "submenus", "transparencia", "ouvidoria", "configuracoes", "atividade-parlamentar", "leis"
            }
        };
    }

    private async Task BackupBancoCompleto(ZipArchive zip, CancellationToken ct)
    {
        await AddJsonAsync(zip, "banco/usuarios.json", await db.Usuarios.AsNoTracking().ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/categorias.json", await db.Categorias.AsNoTracking().ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/publicacoes.json", await db.Publicacoes.AsNoTracking().Include(x => x.Arquivos).Include(x => x.Imagens).ToListAsync(ct), ct);
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
        await AddJsonAsync(zip, "banco/a_camara/estrutura_administrativa.json", await db.EstruturasAdministrativas.AsNoTracking().ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/a_camara/calendario_reunioes.json", await db.CalendarioReunioes.AsNoTracking().ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/editais/processos_seletivos.json", await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/editais/processos_seletivos_arquivos.json", await db.ProcessosSeletivosArquivos.AsNoTracking().ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/editais/concursos.json", await db.Concursos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct), ct);
        await AddJsonAsync(zip, "banco/editais/concursos_arquivos.json", await db.ConcursosArquivos.AsNoTracking().ToListAsync(ct), ct);
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
    }

    private async Task BackupModulos(ZipArchive zip, List<object> modulos, List<object> arquivosNaoEncontrados, CancellationToken ct)
    {
        await AddModulo(zip, "noticias-publicacoes", await db.Publicacoes.AsNoTracking().Include(x => x.Arquivos).Include(x => x.Imagens).ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.ImagemCapa).Concat(x.Arquivos.Select(a => a.CaminhoRelativo)).Concat(x.Imagens.Select(i => i.CaminhoRelativo)).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "licitacoes", await db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct),
            x => x.Id, x => TituloLicitacao(x), x => x.Arquivos.Select(a => a.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "editais-processos-seletivos", await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => x.Arquivos.Select(a => a.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "editais-concursos", await db.Concursos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => x.Arquivos.Select(a => a.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "a-camara-estrutura-administrativa", await db.EstruturasAdministrativas.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.Imagem, x.Arquivo).Concat(ExtractPathsFromText(x.ConteudoHtml)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "a-camara-calendario-de-reunioes", await db.CalendarioReunioes.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.Arquivo).Concat(ExtractPathsFromText(x.ConteudoHtml)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "paginas-institucionais", await db.PaginasInstitucionais.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.ImagemCapa).Concat(ExtractPathsFromText(x.DadosJson)).Concat(ExtractPathsFromText(x.ConteudoHtml)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "submenus-paginas", await db.SubmenuPaginas.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.Imagem, x.Arquivo).Concat(ExtractPathsFromText(x.ConteudoHtml)), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "documentos-arquivos", await db.Arquivos.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo ?? x.NomeArquivo, x => Paths(x.CaminhoRelativo), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "imagens", await db.Imagens.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo ?? x.NomeArquivo, x => Paths(x.CaminhoRelativo), modulos, arquivosNaoEncontrados, ct);

        await AddAtividade(zip, "atividade-parlamentar-atas-reunioes", await db.AtasReunioes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-portarias", await db.Portarias.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-requerimentos", await db.Requerimentos.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-convocacoes", await db.Convocacoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-indicacoes", await db.Indicacoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-mocoes", await db.Mocoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-resolucoes", await db.Resolucoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-projetos-resolucoes", await db.ProjetosResolucao.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-diplomas", await db.Diplomas.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);
        await AddAtividade(zip, "atividade-parlamentar-decretos", await db.Decretos.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, ct);

        await AddModulo(zip, "admin-registros-transparencia-e-outros", await db.AdminRegistros.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => ExtractPathsFromText(x.DadosJson), modulos, arquivosNaoEncontrados, ct);
    }

    private async Task AddAtividade<T>(ZipArchive zip, string modulo, List<T> itens, List<object> modulos, List<object> arquivosNaoEncontrados, CancellationToken ct) where T : AtividadeParlamentarBase
        => await AddModulo(zip, modulo, itens, x => x.Id, x => x.Titulo, x => Paths(x.Arquivo).Concat(ExtractPathsFromText(x.Conteudo)), modulos, arquivosNaoEncontrados, ct);

    private async Task AddModulo<T>(ZipArchive zip, string modulo, List<T> itens, Func<T, long> id, Func<T, string?> titulo, Func<T, IEnumerable<string?>> arquivos, List<object> modulos, List<object> arquivosNaoEncontrados, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        using (var moduloZip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddJsonAsync(moduloZip, "dados/modulo.json", new { modulo, totalRegistros = itens.Count, geradoEmUtc = DateTime.UtcNow }, ct);
            await AddJsonAsync(moduloZip, "dados/registros.json", itens, ct);

            foreach (var item in itens)
            {
                ct.ThrowIfCancellationRequested();
                var itemId = id(item);
                var itemTitulo = titulo(item) ?? $"registro-{itemId}";
                var basePath = $"registros/{itemId}-{Safe(itemTitulo)}";
                await AddJsonAsync(moduloZip, $"{basePath}/dados.json", item, ct);

                foreach (var caminho in arquivos(item).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var full = ResolveFile(caminho);
                    if (full is null)
                    {
                        arquivosNaoEncontrados.Add(new { modulo, id = itemId, titulo = itemTitulo, caminho });
                        continue;
                    }
                    moduloZip.CreateEntryFromFile(full, $"{basePath}/arquivos/{Path.GetFileName(full)}", CompressionLevel.Optimal);
                }
            }
        }

        ms.Position = 0;
        var entry = zip.CreateEntry($"modulos/{Safe(modulo)}.zip", CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await ms.CopyToAsync(entryStream, ct);
        modulos.Add(new { modulo, totalRegistros = itens.Count, pacote = $"modulos/{Safe(modulo)}.zip" });
    }

    private async Task BackupUploadsInteiros(ZipArchive zip, CancellationToken ct)
    {
        var pastas = ResolverPastasArquivos().ToList();
        await AddJsonAsync(zip, "uploads/pastas-incluidas.json", pastas, ct);
        var entradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pasta in pastas)
            AddDirectory(zip, pasta, $"uploads-completos/{Safe(Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))}", entradas);
    }

    private IEnumerable<string> ResolverPastasArquivos()
    {
        var candidatos = new List<string>();
        var basePath = string.IsNullOrWhiteSpace(_storage.BasePath) ? "uploads" : _storage.BasePath.Trim('/', '\\');

        if (Path.IsPathRooted(basePath)) candidatos.Add(basePath);
        else
        {
            candidatos.Add(Path.Combine(env.ContentRootPath, basePath));
            candidatos.Add(Path.Combine(env.ContentRootPath, "src", basePath));
            candidatos.Add(Path.Combine(env.ContentRootPath, "wwwroot", basePath));
            candidatos.Add(Path.Combine(env.ContentRootPath, "public", basePath));
            candidatos.Add(Path.Combine(env.ContentRootPath, "src", "public", basePath));
        }

        if (!string.IsNullOrWhiteSpace(env.WebRootPath)) candidatos.Add(Path.Combine(env.WebRootPath, "uploads"));
        candidatos.Add("/var/data/uploads");

        return candidatos.Select(Path.GetFullPath).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string? ResolveFile(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        var clean = caminho.Trim().Replace("\\", "/");
        if (Uri.TryCreate(clean, UriKind.Absolute, out var uri)) clean = uri.AbsolutePath;
        clean = clean.Split('?', '#')[0].TrimStart('/');
        if (clean.Contains("..")) return null;
        if (clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)) clean = clean[8..];

        foreach (var root in ResolverPastasArquivos())
        {
            var full = Path.GetFullPath(Path.Combine(root, clean));
            if (full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) && File.Exists(full)) return full;

            var fileName = Path.GetFileName(clean);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var found = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static IEnumerable<string?> ExtractPathsFromObject(object value)
    {
        foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string)) continue;
            var text = prop.GetValue(value) as string;
            foreach (var path in ExtractPathsFromText(text)) yield return path;
        }
    }

    private static IEnumerable<string?> Paths(params string?[] values) => values;

    private static List<string> ExtractPathsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        const string pattern = "(?i)(/uploads/[^\\\"'\\s<>]+|uploads/[^\\\"'\\s<>]+|https?://[^\\\"'\\s<>]+/uploads/[^\\\"'\\s<>]+)";
        return Regex.Matches(text, pattern).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
            if (entryName.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) || entryName.Contains("/.git/", StringComparison.OrdinalIgnoreCase) || entryName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || entryName.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entradasAdicionadas.Add(entryName)) continue;
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    private static string TituloLicitacao(Licitacao x)
        => !string.IsNullOrWhiteSpace(x.Objeto) ? x.Objeto : $"Licitação {x.NumeroLicitacao ?? x.Numero ?? x.Id.ToString()}";

    private static string Safe(string? value)
    {
        var s = string.IsNullOrWhiteSpace(value) ? "sem-titulo" : value.Trim().ToLowerInvariant();
        s = s.Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
             .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
             .Replace("ó", "o").Replace("ô", "o").Replace("õ", "o")
             .Replace("ú", "u").Replace("ç", "c");
        s = Regex.Replace(s, "[^a-z0-9\\-_]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "sem-titulo" : s[..Math.Min(s.Length, 100)];
    }
}
