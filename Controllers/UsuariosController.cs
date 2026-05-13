using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize(Policy = "Editor")]
public class UsuariosController(AppDbContext db) : ControllerBase
{
    private long Uid() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? busca, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var uid = Uid();
        var q = db.Usuarios.AsNoTracking().AsQueryable();

        // Admin visualiza todos. Qualquer outro perfil visualiza somente o próprio usuário.
        if (!IsAdmin()) q = q.Where(x => x.Id == uid);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            q = q.Where(x => x.Nome.ToLower().Contains(termo) || x.Email.ToLower().Contains(termo) || (x.CpfCnpj != null && x.CpfCnpj.Contains(termo)));
        }

        var total = await q.CountAsync();
        var itens = await q.OrderBy(x => x.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new { x.Id, x.Nome, x.Email, x.Perfil, x.CpfCnpj, x.Ativo, x.CriadoEm })
            .ToListAsync();

        return Ok(new ApiPage<object>(itens, page, pageSize, total));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obter(long id)
    {
        if (!IsAdmin() && id != Uid()) return Forbid();
        var u = await db.Usuarios.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Nome, x.Email, x.Perfil, x.CpfCnpj, x.Ativo, x.CriadoEm, x.AtualizadoEm })
            .FirstOrDefaultAsync();
        return u is null ? NotFound(new { erro = "Usuário não encontrado." }) : Ok(u);
    }

    [HttpPost]
    public async Task<IActionResult> Criar(UsuarioCreateDto dto)
    {
        if (!IsAdmin()) return Forbid();

        var email = (dto.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(dto.Nome)) return BadRequest(new { erro = "Informe o nome." });
        if (!email.Contains('@')) return BadRequest(new { erro = "E-mail inválido." });
        if (string.IsNullOrWhiteSpace(dto.Senha) || dto.Senha.Length < 6) return BadRequest(new { erro = "A senha deve ter pelo menos 6 caracteres." });
        if (await db.Usuarios.AnyAsync(x => x.Email.ToLower() == email)) return Conflict(new { erro = "E-mail já cadastrado." });

        var perfil = NormalizarPerfil(dto.Perfil);
        var u = new Usuario
        {
            Nome = dto.Nome.Trim(),
            Email = email,
            CpfCnpj = SomenteDigitos(dto.CpfCnpj),
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha),
            Perfil = perfil,
            Ativo = dto.Ativo
        };

        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = u.Id }, new { u.Id, u.Nome, u.Email, u.Perfil, u.CpfCnpj, u.Ativo, u.CriadoEm });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Atualizar(long id, UsuarioUpdateDto dto)
    {
        var uid = Uid();
        if (!IsAdmin() && id != uid) return Forbid();

        var u = await db.Usuarios.FindAsync(id) ?? throw new InvalidOperationException("Usuário não encontrado.");
        var email = (dto.Email ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(dto.Nome)) return BadRequest(new { erro = "Informe o nome." });
        if (!email.Contains('@')) return BadRequest(new { erro = "E-mail inválido." });
        if (await db.Usuarios.AnyAsync(x => x.Id != id && x.Email.ToLower() == email)) return Conflict(new { erro = "E-mail já cadastrado para outro usuário." });

        u.Nome = dto.Nome.Trim();
        u.Email = email;
        u.CpfCnpj = SomenteDigitos(dto.CpfCnpj);

        if (IsAdmin())
        {
            u.Perfil = NormalizarPerfil(dto.Perfil);
            // Ninguém pode bloquear o próprio usuário logado.
            u.Ativo = id == uid ? true : dto.Ativo;
        }
        else
        {
            // Perfil restrito pode editar os próprios dados, mas não muda perfil nem bloqueia a própria conta.
            u.Ativo = true;
        }

        if (!string.IsNullOrWhiteSpace(dto.Senha))
        {
            if (dto.Senha.Length < 6) return BadRequest(new { erro = "A senha deve ter pelo menos 6 caracteres." });
            u.SenhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha);
        }

        u.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/bloquear")]
    public async Task<IActionResult> Bloquear(long id)
    {
        if (!IsAdmin()) return Forbid();
        if (id == Uid()) return BadRequest(new { erro = "Você não pode bloquear o próprio usuário logado." });

        var u = await db.Usuarios.FindAsync(id) ?? throw new InvalidOperationException("Usuário não encontrado.");
        u.Ativo = false;
        u.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/desbloquear")]
    public async Task<IActionResult> Desbloquear(long id)
    {
        if (!IsAdmin()) return Forbid();
        var u = await db.Usuarios.FindAsync(id) ?? throw new InvalidOperationException("Usuário não encontrado.");
        u.Ativo = true;
        u.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Remover(long id)
    {
        var uid = Uid();
        if (!IsAdmin() && id != uid) return Forbid();

        var u = await db.Usuarios.FindAsync(id) ?? throw new InvalidOperationException("Usuário não encontrado.");

        await using var tx = await db.Database.BeginTransactionAsync();
        await ApagarDadosVinculadosAoUsuario(id);
        db.Usuarios.Remove(u);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return NoContent();
    }

    private async Task ApagarDadosVinculadosAoUsuario(long usuarioId)
    {
        var publicacoes = await db.Publicacoes.Where(x => x.UsuarioId == usuarioId).ToListAsync();
        var publicacaoIds = publicacoes.Select(x => x.Id).ToList();

        db.Arquivos.RemoveRange(await db.Arquivos.Where(x => x.UsuarioId == usuarioId || (x.PublicacaoId != null && publicacaoIds.Contains(x.PublicacaoId.Value))).ToListAsync());
        db.Imagens.RemoveRange(await db.Imagens.Where(x => x.UsuarioId == usuarioId || (x.PublicacaoId != null && publicacaoIds.Contains(x.PublicacaoId.Value))).ToListAsync());
        db.Publicacoes.RemoveRange(publicacoes);
        db.Categorias.RemoveRange(await db.Categorias.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.PaginasInstitucionais.RemoveRange(await db.PaginasInstitucionais.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.TelefonesUteis.RemoveRange(await db.TelefonesUteis.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.OuvidoriaChamados.RemoveRange(await db.OuvidoriaChamados.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.ConfiguracoesSite.RemoveRange(await db.ConfiguracoesSite.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.AdminRegistros.RemoveRange(await db.AdminRegistros.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.AtasReunioes.RemoveRange(await db.AtasReunioes.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Portarias.RemoveRange(await db.Portarias.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Requerimentos.RemoveRange(await db.Requerimentos.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Convocacoes.RemoveRange(await db.Convocacoes.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Indicacoes.RemoveRange(await db.Indicacoes.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Mocoes.RemoveRange(await db.Mocoes.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Resolucoes.RemoveRange(await db.Resolucoes.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.ProjetosResolucao.RemoveRange(await db.ProjetosResolucao.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Diplomas.RemoveRange(await db.Diplomas.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        db.Decretos.RemoveRange(await db.Decretos.Where(x => x.UsuarioId == usuarioId).ToListAsync());
    }

    private static string NormalizarPerfil(string? perfil)
    {
        var p = (perfil ?? "editor").Trim().ToLowerInvariant();
        return p is "admin" or "editor" or "transparencia" or "juridico" or "comunicacao" ? p : "editor";
    }

    private static string? SomenteDigitos(string? v)
    {
        var digitos = new string((v ?? "").Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digitos) ? null : digitos;
    }
}
