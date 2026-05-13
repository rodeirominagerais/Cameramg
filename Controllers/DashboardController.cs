using System.Security.Claims;
using Cameramg.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = "Editor")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    private long? Uid()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id) ? id : null;
    }

    private bool IsAdmin()
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return role.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
               role.Equals("administrador", StringComparison.OrdinalIgnoreCase) ||
               role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public async Task<IActionResult> Resumo()
    {
        var uid = Uid();
        var admin = IsAdmin();

        var publicacoes = db.Publicacoes.AsNoTracking().AsQueryable();
        var arquivos = db.Arquivos.AsNoTracking().AsQueryable();
        var imagens = db.Imagens.AsNoTracking().AsQueryable();
        var ouvidoria = db.OuvidoriaChamados.AsNoTracking().AsQueryable();
        var usuarios = db.Usuarios.AsNoTracking().AsQueryable();

        if (!admin)
        {
            if (uid is null)
                return Unauthorized(new { mensagem = "Usuário não identificado no token." });

            publicacoes = publicacoes.Where(x => x.UsuarioId == uid.Value);
            arquivos = arquivos.Where(x => x.UsuarioId == uid.Value);
            imagens = imagens.Where(x => x.UsuarioId == uid.Value);
            ouvidoria = ouvidoria.Where(x => x.UsuarioId == uid.Value);
            usuarios = usuarios.Where(x => x.Id == uid.Value);
        }

        // Não use CancellationToken do request aqui. O navegador/Vite cancela chamadas ao recarregar,
        // e isso derruba o debug com OperationCanceledException sem ser erro real do banco.
        var totalPublicacoes = await publicacoes.CountAsync();
        var totalDocumentos = await arquivos.CountAsync();
        var totalImagens = await imagens.CountAsync();
        var totalUsuarios = await usuarios.CountAsync();
        var totalOuvidoriaAberta = await ouvidoria.CountAsync(x => x.Status == "ABERTO");
        var totalTransparenciaPublicacoes = await publicacoes.CountAsync(x => x.Tipo == "TRANSPARENCIA");
        var totalTransparenciaArquivos = await arquivos.CountAsync(x => x.Tipo == "TRANSPARENCIA");

        return Ok(new
        {
            noticiasPublicacoes = totalPublicacoes,
            documentos = totalDocumentos,
            ouvidoriaAberta = totalOuvidoriaAberta,
            usuarios = totalUsuarios,
            imagens = totalImagens,
            transparencia = totalTransparenciaPublicacoes + totalTransparenciaArquivos,

            // Mantém compatibilidade com o frontend antigo, se alguma tela ainda usar estes nomes.
            publicacoes = totalPublicacoes,
            arquivos = totalDocumentos,
            ouvidoriaAbertos = totalOuvidoriaAberta
        });
    }
}
