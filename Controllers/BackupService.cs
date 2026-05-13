using System.Collections;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.Encodings.Web;
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] ExtensoesArquivosReais =
    [
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".rar", ".7z", ".csv", ".txt", ".rtf", ".odt", ".ods",
        ".mp4", ".mov", ".avi", ".webm", ".mp3", ".wav"
    ];

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
        var arquivosIncluidos = new List<object>();
        var entradasGlobais = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var fs = new FileStream(caminho, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        await AddJsonAsync(zip, "manifesto.json", new
        {
            sistema = "Cameramg",
            tipo = "backup-total-inteligente",
            geradoEmUtc = DateTime.UtcNow,
            contentRoot = env.ContentRootPath,
            webRoot = env.WebRootPath,
            storage = new { _storage.BasePath, _storage.BaseUrl },
            inclui = new[]
            {
                "banco-de-dados-total-em-json",
                "arquivos-reais-referenciados-no-banco-html-json",
                "uploads-completos-detectados",
                "imagens-videos-pdfs-documentos-anexos",
                "pacote-zip-individual-por-modulo",
                "relatorio-de-arquivos-encontrados-e-nao-encontrados"
            },
            observacao = "Backup inteligente: rastreia caminhos em campos do banco, HTML, JSON, src, href, URLs e pastas reais de upload."
        }, ct);

        await BackupBancoCompleto(zip, ct);
        await BackupModulos(zip, modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await BackupArquivosReaisGlobais(zip, arquivosIncluidos, entradasGlobais, ct);
        await BackupUploadsInteiros(zip, entradasGlobais, arquivosIncluidos, ct);
        await BackupArquivosSoltosDoProjeto(zip, entradasGlobais, arquivosIncluidos, ct);

        await AddJsonAsync(zip, "relatorio/modulos-gerados.json", modulos, ct);
        await AddJsonAsync(zip, "relatorio/arquivos-incluidos.json", arquivosIncluidos, ct);
        await AddJsonAsync(zip, "relatorio/arquivos-nao-encontrados.json", arquivosNaoEncontrados, ct);

        return (caminho, nome);
    }

    public async Task<object> GerarEEnviarGoogleDriveAsync(GoogleDriveBackupService drive, CancellationToken ct = default)
    {
        var arquivo = await GerarBackupArquivoAsync(ct);
        try
        {
            var driveResult = await drive.UploadAsync(arquivo.CaminhoArquivo, arquivo.NomeArquivo, ct);
            return new { mensagem = "Backup real gerado e enviado ao Google Drive com sucesso.", arquivo.NomeArquivo, googleDrive = driveResult };
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
        var arquivos = pastas.Where(Directory.Exists).SelectMany(p => Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)).ToList();
        return new
        {
            googleDriveConfigurado,
            totalPastasUploadsDetectadas = pastas.Count,
            totalArquivosReaisDetectados = arquivos.Count,
            tamanhoTotalBytes = arquivos.Sum(f => new FileInfo(f).Length),
            pastasEncontradas = pastas.Select(x => new
            {
                nome = Path.GetFileName(x.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
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

    private async Task BackupModulos(ZipArchive zip, List<object> modulos, List<object> arquivosNaoEncontrados, List<object> arquivosIncluidos, CancellationToken ct)
    {
        await AddModulo(zip, "noticias-publicacoes", await db.Publicacoes.AsNoTracking().Include(x => x.Arquivos).Include(x => x.Imagens).ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.ImagemCapa).Concat(x.Arquivos.Select(a => a.CaminhoRelativo)).Concat(x.Imagens.Select(i => i.CaminhoRelativo)).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "licitacoes", await db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct),
            x => x.Id, x => TituloLicitacao(x), x => x.Arquivos.Select(a => a.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "editais-processos-seletivos", await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => x.Arquivos.Select(a => a.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "editais-concursos", await db.Concursos.AsNoTracking().Include(x => x.Arquivos).ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => x.Arquivos.Select(a => a.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "a-camara-estrutura-administrativa", await db.EstruturasAdministrativas.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.Imagem, x.Arquivo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "a-camara-calendario-de-reunioes", await db.CalendarioReunioes.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.Arquivo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "paginas-institucionais", await db.PaginasInstitucionais.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.ImagemCapa).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "submenus-paginas", await db.SubmenuPaginas.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => Paths(x.Imagem, x.Arquivo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "documentos-arquivos", await db.Arquivos.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo ?? x.NomeArquivo, x => Paths(x.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "imagens", await db.Imagens.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo ?? x.NomeArquivo, x => Paths(x.CaminhoRelativo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "ouvidoria", await db.OuvidoriaChamados.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Assunto ?? x.Protocolo ?? $"chamado-{x.Id}", x => ExtractPathsFromObject(x), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "configuracoes-site", await db.ConfiguracoesSite.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Chave, x => ExtractPathsFromObject(x), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddAtividade(zip, "atividade-parlamentar-atas-reunioes", await db.AtasReunioes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-portarias", await db.Portarias.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-requerimentos", await db.Requerimentos.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-convocacoes", await db.Convocacoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-indicacoes", await db.Indicacoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-mocoes", await db.Mocoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-resolucoes", await db.Resolucoes.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-projetos-resolucoes", await db.ProjetosResolucao.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-diplomas", await db.Diplomas.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
        await AddAtividade(zip, "atividade-parlamentar-decretos", await db.Decretos.AsNoTracking().ToListAsync(ct), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

        await AddModulo(zip, "admin-registros-transparencia-e-outros", await db.AdminRegistros.AsNoTracking().ToListAsync(ct),
            x => x.Id, x => x.Titulo, x => ExtractPathsFromObject(x), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);
    }

    private async Task AddAtividade<T>(ZipArchive zip, string modulo, List<T> itens, List<object> modulos, List<object> arquivosNaoEncontrados, List<object> arquivosIncluidos, CancellationToken ct) where T : AtividadeParlamentarBase
        => await AddModulo(zip, modulo, itens, x => x.Id, x => x.Titulo, x => Paths(x.Arquivo).Concat(ExtractPathsFromObject(x)), modulos, arquivosNaoEncontrados, arquivosIncluidos, ct);

    private async Task AddModulo<T>(ZipArchive zip, string modulo, List<T> itens, Func<T, long> id, Func<T, string?> titulo, Func<T, IEnumerable<string?>> arquivos, List<object> modulos, List<object> arquivosNaoEncontrados, List<object> arquivosIncluidos, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        var entradasModulo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalArquivos = 0;

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

                var caminhos = arquivos(item)
                    .Concat(ExtractPathsFromText(JsonSerializer.Serialize(item, JsonOptions)))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizarCaminhoReferencia)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await AddJsonAsync(moduloZip, $"{basePath}/arquivos-referenciados.json", caminhos, ct);

                foreach (var caminho in caminhos)
                {
                    var full = ResolveFile(caminho);
                    if (full is null)
                    {
                        arquivosNaoEncontrados.Add(new { modulo, id = itemId, titulo = itemTitulo, caminho });
                        continue;
                    }

                    var destino = $"{basePath}/arquivos/{BuildRelativeZipPath(full, caminho)}";
                    if (AddFileToZip(moduloZip, full, destino, entradasModulo))
                    {
                        totalArquivos++;
                        arquivosIncluidos.Add(new { modulo, id = itemId, titulo = itemTitulo, origem = caminho, arquivo = full, destino = $"modulos/{Safe(modulo)}.zip/{destino}" });
                    }
                }
            }
        }

        ms.Position = 0;
        var entry = zip.CreateEntry($"modulos/{Safe(modulo)}.zip", CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await ms.CopyToAsync(entryStream, ct);
        modulos.Add(new { modulo, totalRegistros = itens.Count, totalArquivosReais = totalArquivos, pacote = $"modulos/{Safe(modulo)}.zip" });
    }

    private async Task BackupArquivosReaisGlobais(ZipArchive zip, List<object> arquivosIncluidos, HashSet<string> entradasGlobais, CancellationToken ct)
    {
        var todos = arquivosIncluidos
            .Select(x => x.GetType().GetProperty("arquivo")?.GetValue(x) as string)
            .Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in todos)
        {
            ct.ThrowIfCancellationRequested();
            AddFileToZip(zip, file!, $"arquivos-reais-referenciados/{BuildRelativeZipPath(file!, file!)}", entradasGlobais);
        }
    }

    private async Task BackupUploadsInteiros(ZipArchive zip, HashSet<string> entradasGlobais, List<object> arquivosIncluidos, CancellationToken ct)
    {
        var pastas = ResolverPastasArquivos().ToList();
        await AddJsonAsync(zip, "uploads/pastas-incluidas.json", pastas, ct);

        foreach (var pasta in pastas)
        {
            ct.ThrowIfCancellationRequested();
            AddDirectory(zip, pasta, $"uploads-completos/{Safe(Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))}", entradasGlobais, arquivosIncluidos);
        }
    }

    private Task BackupArquivosSoltosDoProjeto(ZipArchive zip, HashSet<string> entradasGlobais, List<object> arquivosIncluidos, CancellationToken ct)
    {
        var roots = new[]
        {
            env.ContentRootPath,
            env.WebRootPath,
            Path.Combine(env.ContentRootPath, "public"),
            Path.Combine(env.ContentRootPath, "src", "public"),
            Path.Combine(env.ContentRootPath, "wwwroot")
        }
        .Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (DeveIgnorarArquivo(file)) continue;
                if (!ExtensoesArquivosReais.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var destino = $"arquivos-detectados-no-projeto/{Safe(Path.GetFileName(root))}/{rel}";
                if (AddFileToZip(zip, file, destino, entradasGlobais))
                {
                    arquivosIncluidos.Add(new { modulo = "arquivos-detectados-no-projeto", arquivo = file, destino });
                }
            }
        }

        return Task.CompletedTask;
    }

    private IEnumerable<string> ResolverPastasArquivos()
    {
        var candidatos = new List<string>();
        var basePath = string.IsNullOrWhiteSpace(_storage.BasePath) ? "uploads" : _storage.BasePath.Trim('/', '\\');

        void AddCandidate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { candidatos.Add(Path.GetFullPath(path)); } catch { }
        }

        if (Path.IsPathRooted(basePath)) AddCandidate(basePath);
        else
        {
            AddCandidate(Path.Combine(env.ContentRootPath, basePath));
            AddCandidate(Path.Combine(env.ContentRootPath, "src", basePath));
            AddCandidate(Path.Combine(env.ContentRootPath, "wwwroot", basePath));
            AddCandidate(Path.Combine(env.ContentRootPath, "public", basePath));
            AddCandidate(Path.Combine(env.ContentRootPath, "src", "public", basePath));
        }

        AddCandidate(Path.Combine(env.ContentRootPath, "uploads"));
        AddCandidate(Path.Combine(env.ContentRootPath, "src", "uploads"));
        AddCandidate(Path.Combine(env.ContentRootPath, "wwwroot", "uploads"));
        AddCandidate(Path.Combine(env.ContentRootPath, "public", "uploads"));
        AddCandidate(Path.Combine(env.ContentRootPath, "src", "public", "uploads"));
        if (!string.IsNullOrWhiteSpace(env.WebRootPath)) AddCandidate(Path.Combine(env.WebRootPath, "uploads"));
        AddCandidate("/var/data/uploads");
        AddCandidate("/opt/render/project/src/uploads");
        AddCandidate("/opt/render/project/src/src/uploads");
        AddCandidate("/opt/render/project/src/wwwroot/uploads");

        return candidatos.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string? ResolveFile(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;

        var referencias = ExpandirReferencias(caminho).ToList();
        var roots = ResolverPastasArquivos().ToList();
        var searchRoots = roots.Concat(new[]
        {
            env.ContentRootPath,
            env.WebRootPath,
            Path.Combine(env.ContentRootPath, "public"),
            Path.Combine(env.ContentRootPath, "src", "public"),
            Path.Combine(env.ContentRootPath, "wwwroot")
        })
        .Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var clean in referencias)
        {
            if (string.IsNullOrWhiteSpace(clean)) continue;

            if (Path.IsPathRooted(clean))
            {
                try
                {
                    var rooted = Path.GetFullPath(clean);
                    if (File.Exists(rooted) && !DeveIgnorarArquivo(rooted)) return rooted;
                }
                catch { }
            }

            foreach (var root in searchRoots)
            {
                var candidate = clean.TrimStart('/', '\\');
                foreach (var prefix in new[] { "uploads/", "wwwroot/", "public/", "src/", "src/uploads/", "src/public/", "src/public/uploads/" })
                {
                    if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var semPrefixo = candidate[prefix.Length..];
                        var foundPrefix = TryCombine(root, semPrefixo);
                        if (foundPrefix is not null) return foundPrefix;
                    }
                }

                var found = TryCombine(root, candidate);
                if (found is not null) return found;

                var fileName = Path.GetFileName(candidate);
                if (!string.IsNullOrWhiteSpace(fileName) && TemExtensaoArquivoReal(fileName))
                {
                    try
                    {
                        var localizado = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                            .FirstOrDefault(x => !DeveIgnorarArquivo(x));
                        if (localizado is not null) return localizado;
                    }
                    catch { }
                }
            }
        }

        return null;
    }

    private static string? TryCombine(string root, string relative)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var rootFull = Path.GetFullPath(root);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(full) && !DeveIgnorarArquivo(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ExpandirReferencias(string caminho)
    {
        var clean = NormalizarCaminhoReferencia(caminho);
        if (string.IsNullOrWhiteSpace(clean)) yield break;

        yield return clean;

        var semUploads = clean.TrimStart('/', '\\');
        if (semUploads.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            yield return semUploads[8..];

        if (semUploads.StartsWith("src/uploads/", StringComparison.OrdinalIgnoreCase))
            yield return semUploads[12..];

        if (semUploads.StartsWith("wwwroot/uploads/", StringComparison.OrdinalIgnoreCase))
            yield return semUploads[16..];

        var fileName = Path.GetFileName(semUploads);
        if (!string.IsNullOrWhiteSpace(fileName)) yield return fileName;
    }

    private static IEnumerable<string?> ExtractPathsFromObject(object? value, int depth = 0)
    {
        if (value is null || depth > 6) yield break;

        if (value is string s)
        {
            foreach (var path in ExtractPathsFromText(s)) yield return path;
            yield break;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
                foreach (var path in ExtractPathsFromObject(item, depth + 1)) yield return path;
            yield break;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(Guid))
            yield break;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;

            object? propValue;
            try { propValue = prop.GetValue(value); } catch { continue; }

            foreach (var path in ExtractPathsFromObject(propValue, depth + 1))
                yield return path;
        }
    }

    private static IEnumerable<string?> Paths(params string?[] values) => values;

    private static List<string> ExtractPathsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var decoded = WebUtility.HtmlDecode(text).Replace("\\/", "/").Replace("\\u002F", "/").Replace("\\u002f", "/");
        var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            var normalized = NormalizarCaminhoReferencia(value);
            if (!string.IsNullOrWhiteSpace(normalized) && PareceArquivoReal(normalized)) encontrados.Add(normalized);
        }

        foreach (Match m in Regex.Matches(decoded, "(?i)(?:src|href|data-src|poster|url|arquivo|imagem|caminho|caminhoRelativo|file|path)\\s*[:=]\\s*[\\\"']([^\\\"']+)[\\\"']"))
            Add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(decoded, "(?i)url\\(([^)]+)\\)"))
            Add(m.Groups[1].Value.Trim(' ', '\'', '"'));

        foreach (Match m in Regex.Matches(decoded, "(?i)https?://[^\\s\\\"'<>\\)]+"))
            Add(m.Value);

        foreach (Match m in Regex.Matches(decoded, "(?i)(?:/)?(?:uploads|arquivos|files|storage|imagens|images|documentos|docs|videos|banners|editor|conteudo|noticias|licitacoes|editais|transparencia|ouvidoria)[^\\s\\\"'<>\\)]*\\.(?:pdf|png|jpe?g|webp|gif|svg|docx?|xlsx?|pptx?|zip|rar|7z|csv|txt|rtf|odt|ods|mp4|mov|avi|webm|mp3|wav)"))
            Add(m.Value);

        foreach (Match m in Regex.Matches(decoded, "(?i)[A-Za-z0-9_./\\\\%-]+\\.(?:pdf|png|jpe?g|webp|gif|svg|docx?|xlsx?|pptx?|zip|rar|7z|csv|txt|rtf|odt|ods|mp4|mov|avi|webm|mp3|wav)"))
            Add(m.Value);

        return encontrados.ToList();
    }

    private static string NormalizarCaminhoReferencia(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return string.Empty;
        var clean = caminho.Trim().Trim('"', '\'', '`').Replace("\\", "/");
        clean = WebUtility.HtmlDecode(clean).Replace("\\/", "/").Replace("\\u002F", "/").Replace("\\u002f", "/");

        if (Uri.TryCreate(clean, UriKind.Absolute, out var uri)) clean = uri.AbsolutePath;
        try { clean = Uri.UnescapeDataString(clean); } catch { }

        clean = clean.Split('?', '#')[0].Trim();
        while (clean.StartsWith("./", StringComparison.Ordinal)) clean = clean[2..];
        if (clean.Contains("..", StringComparison.Ordinal)) return string.Empty;
        return clean;
    }

    private static bool PareceArquivoReal(string value)
        => TemExtensaoArquivoReal(value) || value.Contains("/uploads/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase);

    private static bool TemExtensaoArquivoReal(string value)
        => ExtensoesArquivosReais.Contains(Path.GetExtension(value.Split('?', '#')[0]), StringComparer.OrdinalIgnoreCase);

    private static async Task AddJsonAsync<T>(ZipArchive zip, string entryName, T dados, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, dados, JsonOptions, ct);
    }

    private static bool AddFileToZip(ZipArchive zip, string sourceFile, string entryName, HashSet<string> entradasAdicionadas)
    {
        if (!File.Exists(sourceFile) || DeveIgnorarArquivo(sourceFile)) return false;
        entryName = SanitizeEntryName(entryName);
        if (!entradasAdicionadas.Add(entryName)) return false;
        zip.CreateEntryFromFile(sourceFile, entryName, CompressionLevel.Optimal);
        return true;
    }

    private static void AddDirectory(ZipArchive zip, string sourceDir, string zipRoot, HashSet<string> entradasAdicionadas, List<object> arquivosIncluidos)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (DeveIgnorarArquivo(file)) continue;
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            var entryName = $"{zipRoot}/{relative}";
            if (AddFileToZip(zip, file, entryName, entradasAdicionadas))
                arquivosIncluidos.Add(new { modulo = "uploads-completos", arquivo = file, destino = entryName });
        }
    }

    private static bool DeveIgnorarArquivo(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/backups-temp/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/App_Data/backups/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRelativeZipPath(string fullPath, string referencia)
    {
        var refClean = NormalizarCaminhoReferencia(referencia).TrimStart('/', '\\');
        if (!string.IsNullOrWhiteSpace(refClean) && refClean.Contains('/')) return SanitizeEntryName(refClean);

        var parts = fullPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var idx = Array.FindLastIndex(parts, p => p.Equals("uploads", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx < parts.Length - 1) return SanitizeEntryName(string.Join('/', parts.Skip(idx + 1)));

        return SanitizeEntryName(Path.GetFileName(fullPath));
    }

    private static string SanitizeEntryName(string entry)
    {
        var clean = entry.Replace('\\', '/').TrimStart('/');
        clean = clean.Replace("../", "").Replace("..", "");
        return clean;
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
