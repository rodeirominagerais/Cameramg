using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/atividade-parlamentar")]
public class AtividadeParlamentarController(AppDbContext db) : ControllerBase
{
    [HttpGet("{categoria}")]
    [AllowAnonymous]
    public async Task<IActionResult> Listar(
        string categoria,
        [FromQuery] string? busca,
        [FromQuery] DateTime? dataInicial,
        [FromQuery] DateTime? dataFinal,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = Query(categoria).AsNoTracking()
            .Where(x => x.Ativo && !new[] { "Arquivado", "Inativo", "Bloqueado", "Cancelada" }.Contains(x.Status));

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            q = q.Where(x => x.Titulo.Contains(termo) ||
                             (x.Resumo != null && x.Resumo.Contains(termo)) ||
                             (x.Conteudo != null && x.Conteudo.Contains(termo)) ||
                             (x.Numero != null && x.Numero.Contains(termo)));
        }

        var dataInicialUtc = InicioDiaUtc(dataInicial);
        var dataFinalUtc = ProximoDiaUtc(dataFinal);

        if (dataInicialUtc.HasValue) q = q.Where(x => (x.DataCriacao ?? x.CriadoEm) >= dataInicialUtc.Value);
        if (dataFinalUtc.HasValue) q = q.Where(x => (x.DataCriacao ?? x.CriadoEm) < dataFinalUtc.Value);

        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.DataCriacao ?? x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AtividadeParlamentarResponse(
                x.Id,
                x.UsuarioId,
                x.Titulo,
                x.Resumo,
                x.Conteudo,
                x.Arquivo,
                x.Numero,
                x.DataCriacao,
                x.Status,
                x.Ativo,
                x.CriadoEm,
                x.AtualizadoEm))
            .ToListAsync();

        return Ok(new ApiPage<AtividadeParlamentarResponse>(itens, page, pageSize, total));
    }

    [HttpGet("{categoria}/{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Obter(string categoria, long id)
    {
        var x = await Query(categoria).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Ativo && !new[] { "Arquivado", "Inativo", "Bloqueado", "Cancelada" }.Contains(x.Status));

        return x is null ? NotFound(new { erro = "Registro não encontrado." }) : Ok(ToResponse(x));
    }

    [HttpGet("admin/{categoria}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> ListarAdmin(string categoria, [FromQuery] string? busca, [FromQuery] string? status, [FromQuery] bool? ativo = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = Query(categoria).AsNoTracking();
        var uid = Uid();
        if (!IsAdmin())
        {
            if (!uid.HasValue) return Unauthorized(new { erro = "Autenticação necessária." });
            q = q.Where(x => x.UsuarioId == uid.Value);
        }
        if (ativo.HasValue) q = q.Where(x => x.Ativo == ativo.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            q = q.Where(x => x.Titulo.Contains(termo) ||
                             (x.Resumo != null && x.Resumo.Contains(termo)) ||
                             (x.Conteudo != null && x.Conteudo.Contains(termo)) ||
                             (x.Numero != null && x.Numero.Contains(termo)));
        }

        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.DataCriacao ?? x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AtividadeParlamentarResponse(x.Id, x.UsuarioId, x.Titulo, x.Resumo, x.Conteudo, x.Arquivo, x.Numero, x.DataCriacao, x.Status, x.Ativo, x.CriadoEm, x.AtualizadoEm))
            .ToListAsync();

        return Ok(new ApiPage<AtividadeParlamentarResponse>(itens, page, pageSize, total));
    }

    [HttpPost("admin/{categoria}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Criar(string categoria, AtividadeParlamentarDto dto)
    {
        var item = Novo(categoria);
        Aplicar(item, dto, Uid());
        db.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(ObterAdmin), new { categoria, id = item.Id }, ToResponse(item));
    }

    [HttpGet("admin/{categoria}/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> ObterAdmin(string categoria, long id)
    {
        var item = await Query(categoria).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound(new { erro = "Registro não encontrado." });
        if (!PodeAlterar(item)) return Forbid();
        return Ok(ToResponse(item));
    }

    [HttpPut("admin/{categoria}/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(string categoria, long id, AtividadeParlamentarDto dto)
    {
        var item = await Query(categoria).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound(new { erro = "Registro não encontrado." });
        if (!PodeAlterar(item)) return Forbid();

        Aplicar(item, dto, item.UsuarioId ?? Uid());
        item.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToResponse(item));
    }

    [HttpDelete("admin/{categoria}/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Remover(string categoria, long id)
    {
        var item = await Query(categoria).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NoContent();
        if (!PodeAlterar(item)) return Forbid();
        item.Ativo = false;
        item.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<AtividadeParlamentarBase> Query(string categoria) => NormalizarCategoria(categoria) switch
    {
        "atas-de-reunioes" => db.AtasReunioes,
        "portarias" => db.Portarias,
        "requerimentos" => db.Requerimentos,
        "convocacoes" => db.Convocacoes,
        "indicacoes" => db.Indicacoes,
        "mocoes" => db.Mocoes,
        "resolucoes" => db.Resolucoes,
        "projeto-de-resolucoes" => db.ProjetosResolucao,
        "diplomas" => db.Diplomas,
        "decretos" => db.Decretos,
        _ => throw new InvalidOperationException("Categoria de atividade parlamentar inválida.")
    };

    private static AtividadeParlamentarBase Novo(string categoria) => NormalizarCategoria(categoria) switch
    {
        "atas-de-reunioes" => new AtaReuniao(),
        "portarias" => new Portaria(),
        "requerimentos" => new Requerimento(),
        "convocacoes" => new Convocacao(),
        "indicacoes" => new Indicacao(),
        "mocoes" => new Mocao(),
        "resolucoes" => new Resolucao(),
        "projeto-de-resolucoes" => new ProjetoResolucao(),
        "diplomas" => new Diploma(),
        "decretos" => new Decreto(),
        _ => throw new InvalidOperationException("Categoria de atividade parlamentar inválida.")
    };

    private static void Aplicar(AtividadeParlamentarBase item, AtividadeParlamentarDto dto, long? usuarioId)
    {
        if (string.IsNullOrWhiteSpace(dto.Titulo)) throw new InvalidOperationException("Título é obrigatório.");
        item.UsuarioId ??= usuarioId;
        item.Titulo = dto.Titulo.Trim();
        item.Resumo = dto.Resumo;
        item.Conteudo = dto.Conteudo;
        item.Arquivo = dto.Arquivo;
        item.Numero = dto.Numero;
        item.DataCriacao = ParaUtcOuNulo(dto.DataCriacao);
        item.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Publicado" : dto.Status.Trim();
        item.Ativo = dto.Ativo;
    }

    private static AtividadeParlamentarResponse ToResponse(AtividadeParlamentarBase x) =>
        new(x.Id, x.UsuarioId, x.Titulo, x.Resumo, x.Conteudo, x.Arquivo, x.Numero, x.DataCriacao, x.Status, x.Ativo, x.CriadoEm, x.AtualizadoEm);

    private static string NormalizarCategoria(string valor) => valor.Trim().ToLowerInvariant()
        .Replace("_", "-")
        .Replace(" ", "-")
        .Replace("atas-reunioes", "atas-de-reunioes")
        .Replace("projetos-de-resolucoes", "projeto-de-resolucoes")
        .Replace("projetos-resolucoes", "projeto-de-resolucoes")
        .Replace("projeto-resolucao", "projeto-de-resolucoes");


    private static DateTime? ParaUtcOuNulo(DateTime? valor)
    {
        if (!valor.HasValue) return null;

        var data = valor.Value;
        return data.Kind switch
        {
            DateTimeKind.Utc => data,
            DateTimeKind.Local => data.ToUniversalTime(),
            _ => DateTime.SpecifyKind(data, DateTimeKind.Utc)
        };
    }

    private static DateTime? InicioDiaUtc(DateTime? valor)
    {
        var data = ParaUtcOuNulo(valor);
        return data?.Date;
    }

    private static DateTime? ProximoDiaUtc(DateTime? valor)
    {
        var data = ParaUtcOuNulo(valor);
        return data?.Date.AddDays(1);
    }

    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
    private bool PodeAlterar(AtividadeParlamentarBase item)
    {
        if (IsAdmin()) return true;
        var uid = Uid();
        return uid.HasValue && item.UsuarioId == uid.Value;
    }
}
