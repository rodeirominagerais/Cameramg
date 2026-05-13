using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Models;
using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/arquivos")]
public class ArquivosController(AppDbContext db, FileStorageService storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] long? publicacaoId,
        [FromQuery] string? busca,
        [FromQuery] string? tipo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 50 : pageSize;
        pageSize = pageSize > 200 ? 200 : pageSize;

        // PÚBLICO: não filtra por usuario_id, para o portal carregar arquivos mesmo com token salvo.
        var q = db.Arquivos.AsNoTracking().Where(x => x.Visivel).AsQueryable();

        if (publicacaoId.HasValue)
            q = q.Where(x => x.PublicacaoId == publicacaoId.Value);

        if (!string.IsNullOrWhiteSpace(tipo))
            q = q.Where(x => x.Tipo == tipo);

        if (!string.IsNullOrWhiteSpace(busca))
            q = q.Where(x =>
                x.Titulo.Contains(busca) ||
                x.NomeArquivo.Contains(busca));

        var total = await q.CountAsync();

        var itens = await q
            .OrderByDescending(x => x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { itens, page, pageSize, total });
    }

    [HttpPost("upload")]
    [Authorize(Policy = "Editor")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] UploadArquivoRequest request,
        CancellationToken ct = default)
    {
        if (request.Arquivo == null || request.Arquivo.Length == 0)
            return BadRequest("Arquivo inválido.");

        if (string.IsNullOrWhiteSpace(request.Titulo))
            request.Titulo = Path.GetFileNameWithoutExtension(request.Arquivo.FileName);

        if (string.IsNullOrWhiteSpace(request.Tipo))
            request.Tipo = "DOCUMENTO";

        if (string.IsNullOrWhiteSpace(request.Pasta))
            request.Pasta = "documentos";

        var s = await storage.SalvarAsync(request.Arquivo, request.Pasta, ct);

        var a = new Arquivo
        {
            UsuarioId = Uid(),
            PublicacaoId = request.PublicacaoId,
            Tipo = request.Tipo,
            Titulo = request.Titulo,
            NomeArquivo = s.nome,
            CaminhoRelativo = s.caminho,
            Extensao = s.extensao,
            MimeType = s.mime,
            TamanhoBytes = s.tamanho,
            Origem = "portal",
            Visivel = true
        };

        db.Arquivos.Add(a);
        await db.SaveChangesAsync(ct);

        return Ok(new { a.Id, a.UsuarioId, a.PublicacaoId, a.Tipo, a.Titulo, a.NomeArquivo, a.CaminhoRelativo, caminho = a.CaminhoRelativo, url = a.CaminhoRelativo, arquivo = a.CaminhoRelativo, a.Extensao, a.MimeType, a.TamanhoBytes, a.Origem, a.Visivel, a.CriadoEm });
    }

    [HttpPut("{id:long}/visibilidade")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Visibilidade(long id, [FromQuery] bool visivel)
    {
        var a = await db.Arquivos.FindAsync(id)
            ?? throw new InvalidOperationException("Arquivo não encontrado.");
        if (Uid().HasValue && a.UsuarioId != Uid()) return Forbid();

        a.Visivel = visivel;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> Remover(long id)
    {
        var a = await db.Arquivos.FindAsync(id)
            ?? throw new InvalidOperationException("Arquivo não encontrado.");
        if (Uid().HasValue && a.UsuarioId != Uid()) return Forbid();

        a.Visivel = false;
        await db.SaveChangesAsync();

        return NoContent();
    }
    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public class UploadArquivoRequest
{
    public IFormFile Arquivo { get; set; } = default!;
    public string Titulo { get; set; } = string.Empty;
    public string Tipo { get; set; } = "DOCUMENTO";
    public long? PublicacaoId { get; set; }
    public string Pasta { get; set; } = "documentos";
}
