using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/configuracoes")]
public class ConfiguracoesController : ControllerBase
{
    private readonly AppDbContext db;
    private readonly FileStorageService storage;
    private readonly IWebHostEnvironment env;

    public ConfiguracoesController(AppDbContext db, FileStorageService storage, IWebHostEnvironment env)
    {
        this.db = db;
        this.storage = storage;
        this.env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        // PÚBLICO: configurações do portal são globais.
        // Não mistura registros por usuário, mesmo que exista token salvo no navegador.
        var lista = await db.ConfiguracoesSite
            .AsNoTracking()
            .Where(x => x.UsuarioId == null)
            .OrderBy(x => x.Chave)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        // Remove duplicados e entrega apenas um valor por chave.
        // Para o portal público, o valor usado é o GLOBAL: usuario_id NULL.
        var limpa = lista
            .GroupBy(x => x.Chave)
            .Select(g => g.First())
            .OrderBy(x => x.Chave)
            .ToList();

        return Ok(limpa);
    }

    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Salvar([FromBody] ConfiguracaoDto dto)
    {
        try
        {
            if (dto == null)
                return BadRequest(new { erro = "Dados da configuração não enviados." });

            var chave = (dto.Chave ?? "").Trim();

            if (string.IsNullOrWhiteSpace(chave))
                return BadRequest(new { erro = "A chave da configuração é obrigatória." });

            // CONFIGURAÇÕES DO PORTAL SÃO GLOBAIS.
            // Não pode salvar com usuario_id, porque o cabeçalho público lê usuario_id NULL.
            var novoValor = dto.Valor ?? "";
            var config = await SalvarGlobalAsync(chave, novoValor, dto.Descricao ?? "");
            await db.SaveChangesAsync();

            return Ok(config);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                erro = "Erro ao salvar configuração.",
                detalhe = ex.InnerException?.Message ?? ex.Message
            });
        }
    }

    // Rota extra para trocar diretamente o brasão/logo pelo campo Brasão/logo.
    // Mesmo que o frontend use upload + salvar configuração, esta rota fica disponível
    // para evitar 404 caso alguma tela chame /api/configuracoes/trocar-brasao.
    [HttpPost("trocar-brasao")]
    [HttpPost("logo")]
    [HttpPost("brasao")]
    [Authorize(Policy = "Admin")]
    [RequestSizeLimit(80_000_000)]
    public async Task<IActionResult> TrocarBrasao(
        [FromForm(Name = "brasao")] IFormFile? brasao,
        [FromForm(Name = "logo")] IFormFile? logo,
        [FromForm(Name = "imagem")] IFormFile? imagem,
        CancellationToken ct = default)
    {
        try
        {
            var arquivo = brasao ?? logo ?? imagem;
            if (arquivo == null || arquivo.Length == 0)
                return BadRequest(new { erro = "Selecione a imagem no campo Brasão/logo." });

            if (!string.IsNullOrWhiteSpace(arquivo.ContentType) && !arquivo.ContentType.StartsWith("image/"))
                return BadRequest(new { erro = "O arquivo enviado precisa ser uma imagem." });

            var salvo = await storage.SalvarAsync(arquivo, "configuracoes", ct);
            var config = await SalvarGlobalAsync("logo", salvo.caminho, "Caminho da imagem do brasão/logo");

            db.Imagens.Add(new Imagem
            {
                UsuarioId = Uid(),
                PublicacaoId = null,
                Titulo = "Brasão/logo do cabeçalho",
                NomeArquivo = salvo.nome,
                CaminhoRelativo = salvo.caminho,
                Origem = "configuracoes_site.logo",
                Visivel = true
            });

            await db.SaveChangesAsync(ct);

            return Ok(new
            {
                chave = config.Chave,
                valor = config.Valor,
                logo = config.Valor,
                caminhoRelativo = config.Valor
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                erro = "Erro ao trocar brasão/logo.",
                detalhe = ex.InnerException?.Message ?? ex.Message
            });
        }
    }


    // Rota para trocar o brasão exibido no rodapé.
    // Usa a mesma lógica da rota trocar-brasao, porém grava na chave footer_brasao.
    [HttpPost("trocar-brasao-rodape")]
    [HttpPost("footer-brasao")]
    [HttpPost("brasao-rodape")]
    // Somente administrador pode trocar o brasão do rodapé, igual ao brasão/logo superior.
    [Authorize(Policy = "Admin")]
    [RequestSizeLimit(80_000_000)]
    public async Task<IActionResult> TrocarBrasaoRodape(
        [FromForm(Name = "brasaoRodape")] IFormFile? brasaoRodape,
        [FromForm(Name = "footer_brasao")] IFormFile? footerBrasao,
        [FromForm(Name = "brasao")] IFormFile? brasao,
        [FromForm(Name = "logo")] IFormFile? logo,
        [FromForm(Name = "imagem")] IFormFile? imagem,
        CancellationToken ct = default)
    {
        try
        {
            var arquivo = brasaoRodape ?? footerBrasao ?? brasao ?? logo ?? imagem;
            if (arquivo == null || arquivo.Length == 0)
                return BadRequest(new { erro = "Selecione a imagem no campo Brasão do rodapé." });

            if (!string.IsNullOrWhiteSpace(arquivo.ContentType) && !arquivo.ContentType.StartsWith("image/"))
                return BadRequest(new { erro = "O arquivo enviado precisa ser uma imagem." });

            var salvo = await storage.SalvarAsync(arquivo, "configuracoes", ct);
            var config = await SalvarGlobalAsync("footer_brasao", salvo.caminho, "Caminho da imagem do brasão do rodapé");

            db.Imagens.Add(new Imagem
            {
                UsuarioId = Uid(),
                PublicacaoId = null,
                Titulo = "Brasão do rodapé",
                NomeArquivo = salvo.nome,
                CaminhoRelativo = salvo.caminho,
                Origem = "configuracoes_site.footer_brasao",
                Visivel = true
            });

            await db.SaveChangesAsync(ct);

            return Ok(new
            {
                chave = config.Chave,
                valor = config.Valor,
                footer_brasao = config.Valor,
                brasaoRodape = config.Valor,
                caminhoRelativo = config.Valor
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                erro = "Erro ao trocar brasão do rodapé.",
                detalhe = ex.InnerException?.Message ?? ex.Message
            });
        }
    }

    private async Task<ConfiguracaoSite> SalvarGlobalAsync(string chave, string novoValor, string descricao)
    {
        var configs = await db.ConfiguracoesSite
            .Where(x => x.Chave == chave && x.UsuarioId == null)
            .OrderBy(x => x.Id)
            .ToListAsync();

        ConfiguracaoSite config;
        if (configs.Count == 0)
        {
            config = new ConfiguracaoSite { Chave = chave, UsuarioId = null };
            db.ConfiguracoesSite.Add(config);
        }
        else
        {
            config = configs[0];
            if (configs.Count > 1)
                db.ConfiguracoesSite.RemoveRange(configs.Skip(1));
        }

        if ((chave.Equals("logo", StringComparison.OrdinalIgnoreCase)
                || chave.Equals("footer_brasao", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(config.Valor)
            && !string.Equals(config.Valor.Trim(), novoValor.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            await RemoverLogoAntigoAsync(config.Valor);
        }

        // Apaga configurações duplicadas por usuário para a mesma chave, pois quebram o cabeçalho público.
        var duplicadasUsuario = await db.ConfiguracoesSite
            .Where(x => x.Chave == chave && x.UsuarioId != null)
            .ToListAsync();
        if (duplicadasUsuario.Count > 0)
            db.ConfiguracoesSite.RemoveRange(duplicadasUsuario);

        config.Valor = novoValor;
        config.Descricao = descricao;
        config.UsuarioId = null;
        return config;
    }

    private async Task RemoverLogoAntigoAsync(string caminhoAntigo)
    {
        var caminho = (caminhoAntigo ?? "").Trim().Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(caminho)) return;

        var imagens = await db.Imagens
            .Where(x => x.CaminhoRelativo == caminho || x.CaminhoRelativo == caminho.TrimStart('/'))
            .ToListAsync();

        foreach (var img in imagens)
            img.Visivel = false;

        var relativo = caminho;
        if (relativo.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(relativo, UriKind.Absolute, out var uri)) return;
            relativo = uri.AbsolutePath;
        }

        relativo = relativo.TrimStart('/');
        if (!relativo.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)) return;

        var webRoot = string.IsNullOrWhiteSpace(env.WebRootPath)
            ? Path.Combine(env.ContentRootPath, "wwwroot")
            : env.WebRootPath;

        var candidates = new[]
        {
            Path.Combine(webRoot, relativo),
            Path.Combine(env.ContentRootPath, relativo)
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            var allowedWebRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads"));
            var allowedLegacyRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "uploads"));
            if (!full.StartsWith(allowedWebRoot, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(allowedLegacyRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (System.IO.File.Exists(full))
                System.IO.File.Delete(full);
        }
    }

    private long? Uid()
    {
        return long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
    }
}
