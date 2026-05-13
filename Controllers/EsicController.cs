using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/esic")]
public class EsicController(AppDbContext db, OuvidoriaEmailService emailService, IWebHostEnvironment env) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> Criar([FromForm] EsicCreateFormDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome)) return BadRequest(new { mensagem = "Informe o nome." });
        if (string.IsNullOrWhiteSpace(dto.Email)) return BadRequest(new { mensagem = "Informe o e-mail." });
        if (string.IsNullOrWhiteSpace(dto.Assunto)) return BadRequest(new { mensagem = "Informe o assunto." });
        if (string.IsNullOrWhiteSpace(dto.Mensagem)) return BadRequest(new { mensagem = "Informe o pedido de acesso à informação." });

        var protocolo = await GerarProtocoloUnico();
        var anexos = await SalvarAnexosAsync(dto.Anexos, protocolo);

        var mensagemFinal = dto.Mensagem.Trim();
        if (anexos.Count > 0)
        {
            mensagemFinal += "\n\nAnexos do pedido E-Sic:";
            foreach (var anexo in anexos)
                mensagemFinal += $"\n- {anexo.NomeOriginal}: {anexo.CaminhoRelativo}";
        }

        var chamado = new OuvidoriaChamado
        {
            CategoriaId = dto.CategoriaId,
            Nome = dto.Nome.Trim(),
            Email = dto.Email.Trim(),
            Telefone = dto.Telefone?.Trim(),
            Assunto = $"E-SIC - {dto.Assunto.Trim()}",
            Mensagem = mensagemFinal,
            Protocolo = protocolo,
            Status = "ABERTO",
            CriadoEm = DateTime.UtcNow
        };

        db.OuvidoriaChamados.Add(chamado);
        await db.SaveChangesAsync();
        await emailService.EnviarNovoChamadoAsync(chamado, anexos.Select(a => new OuvidoriaEmailAnexo(a.NomeOriginal, a.CaminhoFisico, a.MimeType)));

        return Ok(new { chamado.Id, chamado.Protocolo, chamado.Status, chamado.CriadoEm, Anexos = anexos.Select(a => new { a.NomeOriginal, a.CaminhoRelativo, a.MimeType, a.TamanhoBytes }) });
    }

    [HttpGet]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Listar([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = db.OuvidoriaChamados.AsNoTracking()
            .Include(x => x.Categoria)
            .Where(x => x.Assunto != null && x.Assunto.ToUpper().StartsWith("E-SIC"));

        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = await q.CountAsync();
        var registros = await q.OrderByDescending(x => x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var itens = registros.Select(x => new
        {
            x.Id,
            x.Protocolo,
            x.Nome,
            x.Email,
            x.Telefone,
            x.Assunto,
            x.Mensagem,
            x.Resposta,
            x.Status,
            x.CriadoEm,
            x.AtualizadoEm,
            Categoria = x.Categoria != null ? x.Categoria.Nome : null,
            Anexos = ExtrairAnexos(x.Mensagem)
        }).ToList();

        return Ok(new { itens, page, pageSize, total });
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(long id, OuvidoriaUpdateDto dto)
    {
        var c = await db.OuvidoriaChamados.FindAsync(id) ?? throw new InvalidOperationException("Pedido E-Sic não encontrado.");

        if (!string.IsNullOrWhiteSpace(dto.Nome)) c.Nome = dto.Nome.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Email)) c.Email = dto.Email.Trim();
        if (dto.Telefone != null) c.Telefone = dto.Telefone.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Assunto)) c.Assunto = dto.Assunto.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Mensagem)) c.Mensagem = dto.Mensagem.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Status)) c.Status = dto.Status.Trim();
        if (dto.Resposta != null) c.Resposta = dto.Resposta.Trim();

        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Excluir(long id)
    {
        var c = await db.OuvidoriaChamados.FindAsync(id);
        if (c == null) return NotFound(new { mensagem = "Pedido E-Sic não encontrado." });

        db.OuvidoriaChamados.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("consulta")]
    [HttpGet("consultar")]
    [AllowAnonymous]
    public Task<IActionResult> Consultar([FromQuery] string? protocolo, [FromQuery] string? email)
        => ConsultarPedido(protocolo, email);

    [HttpGet("consulta/{protocolo}/{email}")]
    [HttpGet("consultar/{protocolo}/{email}")]
    [HttpGet("{protocolo}/{email}")]
    [AllowAnonymous]
    public Task<IActionResult> ConsultarPorRota(string? protocolo, string? email)
        => ConsultarPedido(protocolo, email);

    private async Task<IActionResult> ConsultarPedido(string? protocolo, string? email)
    {
        if (string.IsNullOrWhiteSpace(protocolo) || string.IsNullOrWhiteSpace(email))
            return BadRequest(new { mensagem = "Informe o protocolo e o e-mail." });

        var protocoloBusca = NormalizarProtocolo(protocolo);
        var emailBusca = Uri.UnescapeDataString(email).Trim().ToLowerInvariant();

        var chamados = await db.OuvidoriaChamados
            .AsNoTracking()
            .Include(x => x.Categoria)
            .Where(x => x.Protocolo != null && x.Email != null && x.Email.ToLower() == emailBusca)
            .ToListAsync();

        var chamado = chamados.FirstOrDefault(x => NormalizarProtocolo(x.Protocolo) == protocoloBusca);
        if (chamado == null) return NotFound(new { mensagem = "Pedido não encontrado para o protocolo e e-mail informados." });

        return Ok(new
        {
            chamado.Id,
            chamado.Protocolo,
            chamado.Nome,
            chamado.Email,
            chamado.Telefone,
            chamado.Assunto,
            chamado.Mensagem,
            chamado.Resposta,
            chamado.Status,
            chamado.CriadoEm,
            chamado.AtualizadoEm,
            Categoria = chamado.Categoria?.Nome
        });
    }


    private async Task<List<EsicAnexoDto>> SalvarAnexosAsync(List<IFormFile>? arquivos, string protocolo)
    {
        var salvos = new List<EsicAnexoDto>();
        if (arquivos == null || arquivos.Count == 0) return salvos;

        var permitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".webp", ".txt"
        };

        var webRoot = string.IsNullOrWhiteSpace(env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : env.WebRootPath;

        var pastaRelativa = $"/uploads/esic/{DateTime.UtcNow:yyyyMMdd}";
        var pastaFisica = Path.Combine(webRoot, "uploads", "esic", DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(pastaFisica);

        foreach (var arquivo in arquivos.Where(a => a != null && a.Length > 0).Take(2))
        {
            if (arquivo.Length > 10 * 1024 * 1024)
                throw new InvalidOperationException("Cada anexo deve ter no máximo 10MB.");

            var ext = Path.GetExtension(arquivo.FileName);
            if (string.IsNullOrWhiteSpace(ext) || !permitidas.Contains(ext))
                throw new InvalidOperationException("Tipo de anexo não permitido. Use PDF, DOC, DOCX, XLS, XLSX, PNG, JPG, WEBP ou TXT.");

            var nomeOriginal = Path.GetFileName(arquivo.FileName);
            var nomeSeguro = $"{protocolo.Replace("-", "")}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var destino = Path.Combine(pastaFisica, nomeSeguro);

            await using (var stream = System.IO.File.Create(destino))
                await arquivo.CopyToAsync(stream);

            salvos.Add(new EsicAnexoDto(nomeOriginal, $"{pastaRelativa}/{nomeSeguro}", arquivo.ContentType, arquivo.Length, destino));
        }

        return salvos;
    }

    private static List<object> ExtrairAnexos(string? mensagem)
    {
        var lista = new List<object>();
        if (string.IsNullOrWhiteSpace(mensagem)) return lista;
        foreach (var linha in mensagem.Split('\n'))
        {
            var l = linha.Trim();
            if (!l.StartsWith("-")) continue;
            var semMarcador = l.TrimStart('-', ' ').Trim();
            var partes = semMarcador.Split(":", 2);
            if (partes.Length == 2 && partes[1].Contains("/uploads/"))
                lista.Add(new { nomeOriginal = partes[0].Trim(), caminhoRelativo = partes[1].Trim() });
        }
        return lista;
    }

    private static string NormalizarProtocolo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        return new string(valor.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private async Task<string> GerarProtocoloUnico()
    {
        for (var i = 0; i < 10; i++)
        {
            var protocolo = $"ESIC-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            if (!await db.OuvidoriaChamados.AnyAsync(x => x.Protocolo == protocolo)) return protocolo;
        }
        return $"ESIC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
    }
}


public class EsicCreateFormDto
{
    public long? CategoriaId { get; set; }
    public string? Nome { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Assunto { get; set; }
    public string? Mensagem { get; set; }
    public List<IFormFile>? Anexos { get; set; }
}

public record EsicAnexoDto(string NomeOriginal, string CaminhoRelativo, string? MimeType, long TamanhoBytes, string CaminhoFisico);
