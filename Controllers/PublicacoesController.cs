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
[Route("api/publicacoes")]
public class PublicacoesController(AppDbContext db, SlugService slug) : ControllerBase
{
    // PÚBLICO: não filtra por usuario_id. O portal precisa listar notícias/publicações
    // mesmo quando existe token salvo no navegador do painel administrativo.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Listar(
        [FromQuery] string? tipo,
        [FromQuery] string? busca,
        [FromQuery] bool? destaque,
        [FromQuery] bool? ativo = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Publicacoes
            .AsNoTracking()
            .Include(x => x.Categoria)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(tipo))
            q = q.Where(x => x.Tipo == tipo);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(x =>
                x.Titulo.Contains(b) ||
                (x.Resumo != null && x.Resumo.Contains(b)) ||
                (x.ConteudoHtml != null && x.ConteudoHtml.Contains(b)));
        }

        if (destaque.HasValue)
            q = q.Where(x => x.Destaque == destaque.Value);

        if (ativo.HasValue)
            q = q.Where(x => x.Ativo == ativo.Value);

        var total = await q.CountAsync();
        var itens = await q
            .OrderByDescending(x => x.DataPublicacao ?? x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ApiPage<Publicacao>(itens, page, pageSize, total));
    }

    // PÚBLICO: não filtra por usuario_id.
    [HttpGet("{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Obter(long id)
    {
        var p = await db.Publicacoes
            .AsNoTracking()
            .Include(x => x.Categoria)
            .Include(x => x.Arquivos.Where(a => a.Visivel))
            .Include(x => x.Imagens.Where(i => i.Visivel))
            .FirstOrDefaultAsync(x => x.Id == id && x.Ativo);

        return p is null ? NotFound() : Ok(p);
    }

    [HttpGet("slug/{slugText}")]
    [AllowAnonymous]
    public async Task<IActionResult> PorSlug(string slugText)
    {
        var p = await db.Publicacoes
            .AsNoTracking()
            .Include(x => x.Arquivos.Where(a => a.Visivel))
            .Include(x => x.Imagens.Where(i => i.Visivel))
            .FirstOrDefaultAsync(x => x.Slug == slugText && x.Ativo);

        return p is null ? NotFound() : Ok(p);
    }

    [HttpPost]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Criar(PublicacaoDto dto)
    {
        var p = new Publicacao
        {
            Tipo = dto.Tipo,
            Destaque = dto.Destaque,
            Titulo = dto.Titulo,
            Resumo = dto.Resumo,
            ConteudoHtml = dto.ConteudoHtml,
            ImagemCapa = dto.ImagemCapa,
            Modalidade = dto.Modalidade,
            Fornecedor = dto.Fornecedor,
            Situacao = dto.Situacao,
            DataPublicacao = dto.DataPublicacao ?? DateTime.UtcNow,
            DataAbertura = dto.DataAbertura,
            DataEncerramento = dto.DataEncerramento,
            CategoriaId = dto.CategoriaId,
            Ativo = dto.Ativo,
            Slug = slug.Gerar(dto.Titulo),
            UsuarioId = Uid()
        };

        db.Publicacoes.Add(p);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = p.Id }, p);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(long id, PublicacaoDto dto)
    {
        var p = await db.Publicacoes.FindAsync(id)
            ?? throw new InvalidOperationException("Publicação não encontrada.");

        if (!IsAdmin() && Uid().HasValue && p.UsuarioId != Uid()) return Forbid();

        p.Tipo = dto.Tipo;
        p.Destaque = dto.Destaque;
        p.Titulo = dto.Titulo;
        p.Resumo = dto.Resumo;
        p.ConteudoHtml = dto.ConteudoHtml;
        p.ImagemCapa = dto.ImagemCapa;
        p.Modalidade = dto.Modalidade;
        p.Fornecedor = dto.Fornecedor;
        p.Situacao = dto.Situacao;
        p.DataPublicacao = dto.DataPublicacao;
        p.DataAbertura = dto.DataAbertura;
        p.DataEncerramento = dto.DataEncerramento;
        p.CategoriaId = dto.CategoriaId;
        p.Ativo = dto.Ativo;
        p.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Remover(long id)
    {
        var p = await db.Publicacoes.FindAsync(id);
        if (p is null) return NoContent();
        if (!IsAdmin() && Uid().HasValue && p.UsuarioId != Uid()) return Forbid();
        p.Ativo = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
}
