using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

public record EstruturaAdministrativaDto(string Titulo, string? ConteudoHtml, string? Imagem, string? Arquivo, string? Status, bool Ativo);
public record CalendarioReuniaoDto(string Titulo, string? Resumo, string? ConteudoHtml, DateTime? DataReuniao, string? Local, string? Arquivo, string? Status, bool Ativo);

[ApiController]
[Route("api/a-camara")]
public class CamaraController(AppDbContext db) : ControllerBase
{
    [HttpGet("estrutura-administrativa")]
    [AllowAnonymous]
    public async Task<IActionResult> ListarEstrutura([FromQuery] string? busca, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.EstruturasAdministrativas.AsNoTracking().Where(x => x.Ativo);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        else q = q.Where(x => x.Status == "Publicado");
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Titulo.Contains(busca) || (x.ConteudoHtml ?? "").Contains(busca));
        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<EstruturaAdministrativa>(itens, page, pageSize, total));
    }

    [HttpGet("estrutura-administrativa/{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> ObterEstrutura(long id)
    {
        var item = await db.EstruturasAdministrativas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo);
        return item is null ? NotFound(new { erro = "Estrutura administrativa não encontrada." }) : Ok(item);
    }

    [HttpGet("calendario-de-reunioes")]
    [AllowAnonymous]
    public async Task<IActionResult> ListarCalendario([FromQuery] string? busca, [FromQuery] DateTime? dataInicial, [FromQuery] DateTime? dataFinal, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.CalendarioReunioes.AsNoTracking().Where(x => x.Ativo);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        else q = q.Where(x => x.Status == "Publicado");
        if (dataInicial.HasValue) q = q.Where(x => x.DataReuniao == null || x.DataReuniao >= dataInicial.Value.ToUniversalTime());
        if (dataFinal.HasValue) q = q.Where(x => x.DataReuniao == null || x.DataReuniao <= dataFinal.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Titulo.Contains(busca) || (x.Resumo ?? "").Contains(busca) || (x.ConteudoHtml ?? "").Contains(busca) || (x.Local ?? "").Contains(busca));
        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.DataReuniao ?? x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<CalendarioReuniao>(itens, page, pageSize, total));
    }

    [HttpGet("calendario-de-reunioes/{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> ObterCalendario(long id)
    {
        var item = await db.CalendarioReunioes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo);
        return item is null ? NotFound(new { erro = "Reunião não encontrada." }) : Ok(item);
    }

    [HttpGet("admin/estrutura-administrativa")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> ListarAdminEstrutura([FromQuery] string? busca, [FromQuery] string? status, [FromQuery] bool? ativo = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.EstruturasAdministrativas.AsNoTracking().AsQueryable();
        if (!IsAdmin()) { var uid = Uid(); if (!uid.HasValue) return Unauthorized(new { erro = "Autenticação necessária." }); q = q.Where(x => x.UsuarioId == uid.Value); }
        if (ativo.HasValue) q = q.Where(x => x.Ativo == ativo.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Titulo.Contains(busca) || (x.ConteudoHtml ?? "").Contains(busca));
        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<EstruturaAdministrativa>(itens, page, pageSize, total));
    }

    [HttpPost("estrutura-administrativa")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> CriarEstrutura(EstruturaAdministrativaDto dto)
    {
        var item = new EstruturaAdministrativa { UsuarioId = Uid(), CriadoEm = DateTime.UtcNow };
        Aplicar(item, dto);
        db.EstruturasAdministrativas.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(ObterEstrutura), new { id = item.Id }, item);
    }

    [HttpPut("estrutura-administrativa/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> AtualizarEstrutura(long id, EstruturaAdministrativaDto dto)
    {
        var item = await db.EstruturasAdministrativas.FindAsync(id) ?? throw new InvalidOperationException("Estrutura administrativa não encontrada.");
        if (!PodeAlterar(item.UsuarioId)) return Forbid();
        Aplicar(item, dto);
        item.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("estrutura-administrativa/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> RemoverEstrutura(long id)
    {
        var item = await db.EstruturasAdministrativas.FindAsync(id);
        if (item is null) return NoContent();
        if (!PodeAlterar(item.UsuarioId)) return Forbid();
        db.EstruturasAdministrativas.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("admin/calendario-de-reunioes")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> ListarAdminCalendario([FromQuery] string? busca, [FromQuery] string? status, [FromQuery] bool? ativo = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.CalendarioReunioes.AsNoTracking().AsQueryable();
        if (!IsAdmin()) { var uid = Uid(); if (!uid.HasValue) return Unauthorized(new { erro = "Autenticação necessária." }); q = q.Where(x => x.UsuarioId == uid.Value); }
        if (ativo.HasValue) q = q.Where(x => x.Ativo == ativo.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Titulo.Contains(busca) || (x.Resumo ?? "").Contains(busca) || (x.ConteudoHtml ?? "").Contains(busca) || (x.Local ?? "").Contains(busca));
        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.DataReuniao ?? x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<CalendarioReuniao>(itens, page, pageSize, total));
    }

    [HttpPost("calendario-de-reunioes")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> CriarCalendario(CalendarioReuniaoDto dto)
    {
        var item = new CalendarioReuniao { UsuarioId = Uid(), CriadoEm = DateTime.UtcNow };
        Aplicar(item, dto);
        db.CalendarioReunioes.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(ObterCalendario), new { id = item.Id }, item);
    }

    [HttpPut("calendario-de-reunioes/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> AtualizarCalendario(long id, CalendarioReuniaoDto dto)
    {
        var item = await db.CalendarioReunioes.FindAsync(id) ?? throw new InvalidOperationException("Reunião não encontrada.");
        if (!PodeAlterar(item.UsuarioId)) return Forbid();
        Aplicar(item, dto);
        item.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("calendario-de-reunioes/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> RemoverCalendario(long id)
    {
        var item = await db.CalendarioReunioes.FindAsync(id);
        if (item is null) return NoContent();
        if (!PodeAlterar(item.UsuarioId)) return Forbid();
        db.CalendarioReunioes.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    void Aplicar(EstruturaAdministrativa item, EstruturaAdministrativaDto dto)
    {
        item.Titulo = string.IsNullOrWhiteSpace(dto.Titulo) ? "Estrutura Administrativa" : dto.Titulo.Trim();
        item.ConteudoHtml = dto.ConteudoHtml;
        item.Imagem = dto.Imagem;
        item.Arquivo = dto.Arquivo;
        item.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Publicado" : dto.Status.Trim();
        item.Ativo = dto.Ativo;
    }

    void Aplicar(CalendarioReuniao item, CalendarioReuniaoDto dto)
    {
        item.Titulo = string.IsNullOrWhiteSpace(dto.Titulo) ? "Calendário de Reuniões" : dto.Titulo.Trim();
        item.Resumo = dto.Resumo;
        item.ConteudoHtml = dto.ConteudoHtml;
        item.DataReuniao = dto.DataReuniao.HasValue ? DateTime.SpecifyKind(dto.DataReuniao.Value, DateTimeKind.Utc) : null;
        item.Local = dto.Local;
        item.Arquivo = dto.Arquivo;
        item.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Publicado" : dto.Status.Trim();
        item.Ativo = dto.Ativo;
    }

    long? Uid()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("id");
        return long.TryParse(id, out var v) ? v : null;
    }

    bool IsAdmin() => User.IsInRole("admin") || string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
    bool PodeAlterar(long? usuarioId) => IsAdmin() || (Uid().HasValue && usuarioId == Uid());
}
