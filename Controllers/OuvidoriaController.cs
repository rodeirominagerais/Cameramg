using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/ouvidoria")]
public class OuvidoriaController(AppDbContext db, OuvidoriaEmailService emailService) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Criar(OuvidoriaCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome)) return BadRequest(new { mensagem = "Informe o nome." });
        if (string.IsNullOrWhiteSpace(dto.Email)) return BadRequest(new { mensagem = "Informe o e-mail." });
        if (string.IsNullOrWhiteSpace(dto.Assunto)) return BadRequest(new { mensagem = "Informe o assunto." });
        if (string.IsNullOrWhiteSpace(dto.Mensagem)) return BadRequest(new { mensagem = "Informe a mensagem." });

        var assunto = dto.Assunto.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Tipo)) assunto = $"{dto.Tipo.Trim()} - {assunto}";
        if (!string.IsNullOrWhiteSpace(dto.Prioridade)) assunto = $"{assunto} [{dto.Prioridade.Trim()}]";

        var chamado = new OuvidoriaChamado
        {
            CategoriaId = dto.CategoriaId,
            Nome = dto.Nome.Trim(),
            Email = dto.Email.Trim(),
            Telefone = dto.Telefone?.Trim(),
            Assunto = assunto,
            Mensagem = dto.Mensagem.Trim(),
            Protocolo = await GerarProtocoloUnico(),
            Status = "ABERTO",
            CriadoEm = DateTime.UtcNow
        };

        db.OuvidoriaChamados.Add(chamado);
        await db.SaveChangesAsync();

        await emailService.EnviarNovoChamadoAsync(chamado);

        return Ok(new
        {
            chamado.Id,
            chamado.Protocolo,
            chamado.Status,
            chamado.CriadoEm
        });
    }

    [HttpGet("consulta")]
    [HttpGet("consultar")]
    [AllowAnonymous]
    public Task<IActionResult> Consultar([FromQuery] string? protocolo, [FromQuery] string? email)
        => ConsultarChamado(protocolo, email);

    // Compatibilidade com versões antigas do frontend que montavam a URL por caminho,
    // evitando 404 em /api/ouvidoria/consulta/PROTOCOLO/email@dominio.com.
    [HttpGet("consulta/{protocolo}/{email}")]
    [HttpGet("consultar/{protocolo}/{email}")]
    [HttpGet("{protocolo}/{email}")]
    [AllowAnonymous]
    public Task<IActionResult> ConsultarPorRota(string? protocolo, string? email)
        => ConsultarChamado(protocolo, email);

    private async Task<IActionResult> ConsultarChamado(string? protocolo, string? email)
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

        if (chamado == null) return NotFound(new { mensagem = "Chamado não encontrado para o protocolo e e-mail informados." });

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

    private static string NormalizarProtocolo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var chars = valor.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }

    [HttpGet]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Listar([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = db.OuvidoriaChamados.AsNoTracking().Include(x => x.Categoria)
            .Where(x => x.Assunto == null || !x.Assunto.ToUpper().StartsWith("E-SIC"))
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { itens, page, pageSize, total });
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(long id, OuvidoriaUpdateDto dto)
    {
        var c = await db.OuvidoriaChamados.FindAsync(id) ?? throw new InvalidOperationException("Chamado não encontrado.");
        c.Status = string.IsNullOrWhiteSpace(dto.Status) ? c.Status : dto.Status;
        c.Resposta = dto.Resposta ?? c.Resposta;
        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Excluir(long id)
    {
        var c = await db.OuvidoriaChamados.FindAsync(id);
        if (c == null) return NotFound(new { mensagem = "Registro não encontrado." });

        db.OuvidoriaChamados.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string> GerarProtocoloUnico()
    {
        for (var i = 0; i < 10; i++)
        {
            var protocolo = $"OUV-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            if (!await db.OuvidoriaChamados.AnyAsync(x => x.Protocolo == protocolo)) return protocolo;
        }

        return $"OUV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
    }
}
