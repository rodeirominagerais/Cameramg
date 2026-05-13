using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

public record SubmenuPaginaDto(string Menu, string Pagina, string Slug, string Rota, string Titulo, string? ConteudoHtml, string? Imagem, string? Arquivo, string? Status, bool Ativo);

[ApiController, Route("api/submenus")]
public class SubmenusController(AppDbContext db) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Listar([FromQuery] string? busca, [FromQuery] string? status, [FromQuery] bool? ativo = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.SubmenuPaginas.AsNoTracking().AsQueryable();
        if (ativo.HasValue) q = q.Where(x => x.Ativo == ativo.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Titulo.Contains(busca) || x.Pagina.Contains(busca) || x.Menu.Contains(busca));
        var total = await q.CountAsync();
        var itens = await q.OrderBy(x => x.Menu).ThenBy(x => x.Pagina).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<SubmenuPagina>(itens, page, pageSize, total));
    }

    [HttpPost, Authorize(Policy = "Editor")]
    public async Task<IActionResult> Criar(SubmenuPaginaDto dto)
    {
        var rota = NormalizarRota(dto.Rota, dto.Slug);
        var item = await db.SubmenuPaginas.FirstOrDefaultAsync(x => x.Rota == rota);
        if (item is null)
        {
            item = new SubmenuPagina { UsuarioId = Uid(), CriadoEm = DateTime.UtcNow };
            db.SubmenuPaginas.Add(item);
        }
        else if (!PodeAlterar(item.UsuarioId)) return Forbid();

        Aplicar(item, dto);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = item.Id }, item);
    }

    [HttpGet("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Obter(long id)
    {
        var item = await db.SubmenuPaginas.FindAsync(id);
        if (item is null) return NotFound(new { erro = "Submenu não encontrado." });
        if (!PodeAlterar(item.UsuarioId)) return Forbid();
        return Ok(item);
    }

    [HttpGet("pagina"), AllowAnonymous]
    public async Task<IActionResult> ObterPorRota([FromQuery] string rota)
    {
        if (string.IsNullOrWhiteSpace(rota)) return BadRequest(new { erro = "Informe a rota." });
        var rotaNormalizada = NormalizarRota(rota, null);
        var item = await db.SubmenuPaginas.AsNoTracking().FirstOrDefaultAsync(x => x.Rota == rotaNormalizada && x.Ativo);
        return item is null ? NotFound(new { erro = "Página de submenu não encontrada." }) : Ok(item);
    }

    [HttpGet("slug/{slug}"), AllowAnonymous]
    public async Task<IActionResult> ObterPorSlug(string slug)
    {
        var item = await db.SubmenuPaginas.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Ativo);
        return item is null ? NotFound(new { erro = "Página de submenu não encontrada." }) : Ok(item);
    }

    [HttpPut("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(long id, SubmenuPaginaDto dto)
    {
        var item = await db.SubmenuPaginas.FindAsync(id);

        // Evita exceção quando a tela tenta atualizar item que foi removido/recriado.
        if (item is null)
        {
            item = new SubmenuPagina { UsuarioId = Uid(), CriadoEm = DateTime.UtcNow };
            db.SubmenuPaginas.Add(item);
        }
        else if (!PodeAlterar(item.UsuarioId)) return Forbid();

        Aplicar(item, dto);
        item.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Remover(long id)
    {
        var item = await db.SubmenuPaginas.FindAsync(id);
        if (item is null) return NoContent();
        if (!PodeAlterar(item.UsuarioId)) return Forbid();
        item.Ativo = false; item.AtualizadoEm = DateTime.UtcNow; await db.SaveChangesAsync(); return NoContent();
    }

    void Aplicar(SubmenuPagina item, SubmenuPaginaDto dto)
    {
        item.Menu = string.IsNullOrWhiteSpace(dto.Menu) ? "A Câmara" : dto.Menu.Trim();
        item.Pagina = string.IsNullOrWhiteSpace(dto.Pagina) ? dto.Titulo.Trim() : dto.Pagina.Trim();
        item.Slug = string.IsNullOrWhiteSpace(dto.Slug) ? GerarSlug(item.Pagina) : dto.Slug.Trim().Trim('/');
        item.Rota = NormalizarRota(dto.Rota, item.Slug);
        item.Titulo = (dto.Titulo ?? "").Trim();
        if (string.IsNullOrWhiteSpace(item.Titulo)) return;
        item.ConteudoHtml = dto.ConteudoHtml;
        item.Imagem = dto.Imagem;
        item.Arquivo = dto.Arquivo;
        item.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Publicado" : dto.Status.Trim();
        item.Ativo = dto.Ativo;
    }

    static string NormalizarRota(string? rota, string? slug)
    {
        var r = string.IsNullOrWhiteSpace(rota) ? "/" + (slug ?? "submenu").Trim('/') : rota.Trim();
        return r.StartsWith('/') ? r : "/" + r;
    }

    static string GerarSlug(string texto)
    {
        var normalizado = texto.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalizado.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray();
        var limpo = new string(chars);
        limpo = System.Text.RegularExpressions.Regex.Replace(limpo, @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(limpo) ? "submenu" : limpo;
    }
    long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
    bool PodeAlterar(long? usuarioId)
    {
        if (IsAdmin()) return true;
        var uid = Uid();
        return uid.HasValue && usuarioId == uid.Value;
    }
}
