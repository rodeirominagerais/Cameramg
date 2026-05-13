using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/paginas")]
public class PaginasController(AppDbContext db) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Listar()
    {
        // PÚBLICO: lista páginas ativas sem filtrar por usuario_id.
        var q = db.PaginasInstitucionais.AsNoTracking().Where(x => x.Ativo);
        return Ok(await q.OrderBy(x => x.Titulo).ToListAsync());
    }

    [HttpGet("{chave}"), AllowAnonymous]
    public async Task<IActionResult> Obter(string chave)
    {
        var k = NormalizarChave(chave);
        // PÚBLICO: preferir a página global/ativa, sem depender do usuário logado.
        var p = await db.PaginasInstitucionais.AsNoTracking()
            .Where(x => x.Ativo && x.Chave == k)
            .OrderBy(x => x.UsuarioId != null)
            .FirstOrDefaultAsync();

        // Compatibilidade: a página pública de Localização usa o mapa cadastrado
        // em Configurações > Mapa / iframe / URL. Se não existir uma página
        // institucional chamada localizacao, não retorna 404: devolve o iframe
        // salvo na configuração global.
        if (p is null && k == "localizacao")
        {
            var mapa = await db.ConfiguracoesSite.AsNoTracking()
                .Where(x => x.UsuarioId == null &&
                    (x.Chave == "mapa" || x.Chave == "mapa_iframe" || x.Chave == "iframe_mapa" || x.Chave == "localizacao_mapa"))
                .OrderBy(x => x.Chave == "mapa" ? 0 : 1)
                .Select(x => x.Valor)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(mapa))
            {
                return Ok(new
                {
                    id = 0,
                    chave = "localizacao",
                    titulo = "Localização",
                    conteudoHtml = mapa,
                    imagemCapa = "",
                    ativo = true,
                    dadosJson = "{}"
                });
            }
        }

        return p is null ? NotFound() : Ok(p);
    }

    [HttpPost, Authorize(Policy = "Editor")]
    public async Task<IActionResult> Salvar(PaginaDto dto)
    {
        var p = await SalvarOuAtualizarAsync(null, dto);
        return Ok(p);
    }

    [HttpPut("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(long id, PaginaDto dto)
    {
        var p = await SalvarOuAtualizarAsync(id, dto);
        return Ok(p);
    }

    [HttpDelete("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Remover(long id)
    {
        var p = await db.PaginasInstitucionais.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NoContent();
        if (!PodeAlterar(p.UsuarioId)) return Forbid();

        p.Ativo = false;
        p.AtualizadoEm = DateTime.UtcNow;

        var slug = p.Chave;
        var rota = $"/o-municipio/{slug}";
        var submenus = await db.SubmenuPaginas.Where(x => x.Slug == slug || x.Rota == rota).ToListAsync();
        foreach (var s in submenus)
        {
            s.Ativo = false;
            s.Status = "Inativo";
            s.AtualizadoEm = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<PaginaInstitucional> SalvarOuAtualizarAsync(long? id, PaginaDto dto)
    {
        var uid = Uid();
        var chave = NormalizarChave(dto.Chave ?? dto.Titulo);
        var titulo = string.IsNullOrWhiteSpace(dto.Titulo) ? chave : dto.Titulo.Trim();
        PaginaInstitucional? p = null;

        if (id.HasValue)
            p = await db.PaginasInstitucionais.FirstOrDefaultAsync(x => x.Id == id.Value);

        // ATENÇÃO:
        // A tabela possui índice único somente por CHAVE (IX_paginas_institucionais_chave).
        // Portanto NÃO podemos procurar filtrando usuario_id aqui, senão o backend não encontra
        // um registro já existente de outro usuario_id e tenta inserir outro com a mesma chave,
        // causando erro 23505 duplicate key.
        p ??= await db.PaginasInstitucionais
            .FirstOrDefaultAsync(x => x.Chave == chave);

        if (p is null)
        {
            p = new PaginaInstitucional
            {
                Chave = chave,
                UsuarioId = uid,
                CriadoEm = DateTime.UtcNow
            };
            db.PaginasInstitucionais.Add(p);
        }
        else if (!PodeAlterar(p.UsuarioId))
        {
            throw new UnauthorizedAccessException("Você só pode editar páginas criadas por você.");
        }

        p.Chave = chave;
        p.Titulo = titulo;
        p.ConteudoHtml = dto.ConteudoHtml ?? string.Empty;
        p.ImagemCapa = dto.ImagemCapa ?? string.Empty;
        p.DadosJson = dto.DadosJson;
        p.Ativo = dto.Ativo;
        p.AtualizadoEm = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Segurança extra: se dois salvamentos tentarem criar a mesma chave ao mesmo tempo,
            // limpa a inserção pendente e atualiza o registro que já existe.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
                entry.State = EntityState.Detached;

            var existente = await db.PaginasInstitucionais.FirstOrDefaultAsync(x => x.Chave == chave);
            if (existente is null) throw;
            if (!PodeAlterar(existente.UsuarioId))
                throw new UnauthorizedAccessException("Você só pode editar páginas criadas por você.");

            existente.Titulo = titulo;
            existente.ConteudoHtml = dto.ConteudoHtml ?? string.Empty;
            existente.ImagemCapa = dto.ImagemCapa ?? string.Empty;
            existente.DadosJson = dto.DadosJson;
            existente.Ativo = dto.Ativo;
            existente.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
            p = existente;
        }

        return p;
    }

    private static string NormalizarChave(string? c)
    {
        var valor = string.IsNullOrWhiteSpace(c) ? "pagina" : c.Trim().ToLowerInvariant();
        var normalized = valor.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray();
        valor = new string(chars).Normalize(System.Text.NormalizationForm.FormC);
        valor = System.Text.RegularExpressions.Regex.Replace(valor, @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(valor) ? "pagina" : valor;
    }

    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
    private bool PodeAlterar(long? usuarioId)
    {
        if (IsAdmin()) return true;
        var uid = Uid();
        return uid.HasValue && usuarioId == uid.Value;
    }
}
