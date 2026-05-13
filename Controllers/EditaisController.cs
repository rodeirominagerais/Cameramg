using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/editais")]
public class EditaisController(AppDbContext db) : ControllerBase
{
    [HttpGet("processos-seletivos")]
    [AllowAnonymous]
    public Task<IActionResult> ListarProcessosSeletivos([FromQuery] string? busca, [FromQuery] DateTime? dataInicial, [FromQuery] DateTime? dataFinal, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        => Listar<ProcessoSeletivo, ProcessoSeletivoArquivo>(db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos), busca, dataInicial, dataFinal, status, page, pageSize, a => a.ProcessoSeletivoId, modoAdmin: false);

    [HttpGet("processos-seletivos/{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> ObterProcessoSeletivo(long id)
    {
        var item = await db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).FirstOrDefaultAsync(x => x.Id == id && x.Ativo);
        return item is null ? NotFound(new { erro = "Processo seletivo não encontrado." }) : Ok(Mapear(item, item.Arquivos, a => a.ProcessoSeletivoId));
    }

    [HttpGet("concursos")]
    [AllowAnonymous]
    public Task<IActionResult> ListarConcursos([FromQuery] string? busca, [FromQuery] DateTime? dataInicial, [FromQuery] DateTime? dataFinal, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        => Listar<Concurso, ConcursoArquivo>(db.Concursos.AsNoTracking().Include(x => x.Arquivos), busca, dataInicial, dataFinal, status, page, pageSize, a => a.ConcursoId, modoAdmin: false);

    [HttpGet("concursos/{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> ObterConcurso(long id)
    {
        var item = await db.Concursos.AsNoTracking().Include(x => x.Arquivos).FirstOrDefaultAsync(x => x.Id == id && x.Ativo);
        return item is null ? NotFound(new { erro = "Concurso não encontrado." }) : Ok(Mapear(item, item.Arquivos, a => a.ConcursoId));
    }

    // ADMIN: segue o mesmo padrão das outras janelas: editor cadastra, lista e altera seus próprios registros; admin vê/altera todos.
    [HttpGet("admin")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> ListarAdmin([FromQuery] string? busca, [FromQuery] string? status, [FromQuery] bool? ativo = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var uid = Uid();

        var processos = db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos).AsQueryable();
        var concursos = db.Concursos.AsNoTracking().Include(x => x.Arquivos).AsQueryable();

        if (!IsAdmin())
        {
            if (!uid.HasValue) return Unauthorized(new { erro = "Autenticação necessária." });
            processos = processos.Where(x => x.UsuarioId == uid.Value);
            concursos = concursos.Where(x => x.UsuarioId == uid.Value);
        }

        processos = AplicarFiltros(processos, busca, null, null, status, ativo);
        concursos = AplicarFiltros(concursos, busca, null, null, status, ativo);

        var listaProcessos = await processos.ToListAsync();
        var listaConcursos = await concursos.ToListAsync();

        var itens = listaProcessos.Select(x => new
            {
                Categoria = "Processos Seletivos",
                Tipo = "EDITAL",
                Dados = Mapear(x, x.Arquivos, a => a.ProcessoSeletivoId)
            })
            .Concat(listaConcursos.Select(x => new
            {
                Categoria = "Concursos",
                Tipo = "EDITAL",
                Dados = Mapear(x, x.Arquivos, a => a.ConcursoId)
            }))
            .OrderByDescending(x => x.Dados.DataPublicacao ?? x.Dados.CriadoEm)
            .ToList();

        var total = itens.Count;
        var pageItens = itens.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
        {
            x.Dados.Id,
            x.Dados.UsuarioId,
            x.Tipo,
            x.Categoria,
            x.Dados.Titulo,
            x.Dados.Resumo,
            x.Dados.Conteudo,
            x.Dados.Numero,
            x.Dados.DataPublicacao,
            x.Dados.DataInicio,
            x.Dados.DataFim,
            x.Dados.Status,
            x.Dados.Ativo,
            x.Dados.CriadoEm,
            x.Dados.AtualizadoEm,
            x.Dados.Arquivos
        }).ToList();

        return Ok(new ApiPage<object>(pageItens.Cast<object>().ToList(), page, pageSize, total));
    }

    [HttpGet("admin/processos-seletivos")]
    [Authorize(Policy = "Editor")]
    public Task<IActionResult> ListarAdminProcessosSeletivos([FromQuery] string? busca, [FromQuery] DateTime? dataInicial, [FromQuery] DateTime? dataFinal, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        => Listar<ProcessoSeletivo, ProcessoSeletivoArquivo>(db.ProcessosSeletivos.AsNoTracking().Include(x => x.Arquivos), busca, dataInicial, dataFinal, status, page, pageSize, a => a.ProcessoSeletivoId, modoAdmin: true);

    [HttpGet("admin/concursos")]
    [Authorize(Policy = "Editor")]
    public Task<IActionResult> ListarAdminConcursos([FromQuery] string? busca, [FromQuery] DateTime? dataInicial, [FromQuery] DateTime? dataFinal, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        => Listar<Concurso, ConcursoArquivo>(db.Concursos.AsNoTracking().Include(x => x.Arquivos), busca, dataInicial, dataFinal, status, page, pageSize, a => a.ConcursoId, modoAdmin: true);

    [HttpPost("processos-seletivos")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> CriarProcessoSeletivo(EditalDto dto)
    {
        var item = new ProcessoSeletivo { CriadoEm = DateTime.UtcNow, UsuarioId = Uid() };
        Aplicar(item, dto, item.UsuarioId);
        db.ProcessosSeletivos.Add(item);
        await db.SaveChangesAsync();
        await SincronizarProcessoArquivos(item, dto.Arquivos);
        NormalizarDatasUtc(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(ObterProcessoSeletivo), new { id = item.Id }, Mapear(item, item.Arquivos, a => a.ProcessoSeletivoId));
    }

    [HttpPut("processos-seletivos/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> AtualizarProcessoSeletivo(long id, EditalDto dto)
    {
        var item = await db.ProcessosSeletivos.Include(x => x.Arquivos).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Processo seletivo não encontrado.");
        if (!PodeAlterar(item)) return Forbid();
        Aplicar(item, dto, item.UsuarioId ?? Uid());
        item.AtualizadoEm = DateTime.UtcNow;
        await SincronizarProcessoArquivos(item, dto.Arquivos);
        NormalizarDatasUtc(item);
        await db.SaveChangesAsync();
        return Ok(Mapear(item, item.Arquivos, a => a.ProcessoSeletivoId));
    }

    [HttpDelete("processos-seletivos/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> RemoverProcessoSeletivo(long id)
    {
        var item = await db.ProcessosSeletivos.FindAsync(id);
        if (item is null) return NoContent();
        if (!PodeAlterar(item)) return Forbid();
        var arquivos = await db.ProcessosSeletivosArquivos.Where(x => x.ProcessoSeletivoId == id).ToListAsync();
        db.ProcessosSeletivosArquivos.RemoveRange(arquivos);
        db.ProcessosSeletivos.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("concursos")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> CriarConcurso(EditalDto dto)
    {
        var item = new Concurso { CriadoEm = DateTime.UtcNow, UsuarioId = Uid() };
        Aplicar(item, dto, item.UsuarioId);
        db.Concursos.Add(item);
        await db.SaveChangesAsync();
        await SincronizarConcursoArquivos(item, dto.Arquivos);
        NormalizarDatasUtc(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(ObterConcurso), new { id = item.Id }, Mapear(item, item.Arquivos, a => a.ConcursoId));
    }

    [HttpPut("concursos/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> AtualizarConcurso(long id, EditalDto dto)
    {
        var item = await db.Concursos.Include(x => x.Arquivos).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Concurso não encontrado.");
        if (!PodeAlterar(item)) return Forbid();
        Aplicar(item, dto, item.UsuarioId ?? Uid());
        item.AtualizadoEm = DateTime.UtcNow;
        await SincronizarConcursoArquivos(item, dto.Arquivos);
        NormalizarDatasUtc(item);
        await db.SaveChangesAsync();
        return Ok(Mapear(item, item.Arquivos, a => a.ConcursoId));
    }

    [HttpDelete("concursos/{id:long}")]
    [Authorize(Policy = "Editor")]
    public async Task<IActionResult> RemoverConcurso(long id)
    {
        var item = await db.Concursos.FindAsync(id);
        if (item is null) return NoContent();
        if (!PodeAlterar(item)) return Forbid();
        var arquivos = await db.ConcursosArquivos.Where(x => x.ConcursoId == id).ToListAsync();
        db.ConcursosArquivos.RemoveRange(arquivos);
        db.Concursos.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    async Task<IActionResult> Listar<TEntity, TArquivo>(IQueryable<TEntity> query, string? busca, DateTime? dataInicial, DateTime? dataFinal, string? status, int page, int pageSize, Func<TArquivo, long> registroId, bool modoAdmin)
        where TEntity : EditalBase
        where TArquivo : EditalArquivoBase
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        if (modoAdmin)
        {
            var uid = Uid();
            if (!IsAdmin())
            {
                if (!uid.HasValue) return Unauthorized(new { erro = "Autenticação necessária." });
                query = query.Where(x => x.UsuarioId == uid.Value);
            }
        }
        else
        {
            query = query.Where(x => x.Ativo && !new[] { "Arquivado", "Inativo", "Bloqueado", "Cancelada" }.Contains(x.Status));
        }

        query = AplicarFiltros(query, busca, dataInicial, dataFinal, status, ativo: null);

        var total = await query.CountAsync();
        var itens = await query.OrderByDescending(x => x.DataPublicacao ?? x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new OkObjectResult(new ApiPage<EditalResponse>(itens.Select(x => Mapear(x, GetArquivos<TEntity, TArquivo>(x), registroId)).ToList(), page, pageSize, total));
    }

    static IQueryable<TEntity> AplicarFiltros<TEntity>(IQueryable<TEntity> query, string? busca, DateTime? dataInicial, DateTime? dataFinal, string? status, bool? ativo)
        where TEntity : EditalBase
    {
        if (ativo.HasValue)
            query = query.Where(x => x.Ativo == ativo.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status.ToLower() == status.ToLower());

        if (dataInicial.HasValue)
            query = query.Where(x => x.DataPublicacao == null || x.DataPublicacao >= dataInicial.Value.Date);

        if (dataFinal.HasValue)
        {
            var final = dataFinal.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.DataPublicacao == null || x.DataPublicacao <= final);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.ToLower();
            query = query.Where(x =>
                x.Titulo.ToLower().Contains(b) ||
                (x.Resumo != null && x.Resumo.ToLower().Contains(b)) ||
                (x.Conteudo != null && x.Conteudo.ToLower().Contains(b)) ||
                (x.Numero != null && x.Numero.ToLower().Contains(b)));
        }

        return query;
    }

    static IReadOnlyList<TArquivo> GetArquivos<TEntity, TArquivo>(TEntity item)
        where TEntity : EditalBase
        where TArquivo : EditalArquivoBase
    {
        return item switch
        {
            ProcessoSeletivo ps when typeof(TArquivo) == typeof(ProcessoSeletivoArquivo) => ps.Arquivos.Cast<TArquivo>().ToList(),
            Concurso c when typeof(TArquivo) == typeof(ConcursoArquivo) => c.Arquivos.Cast<TArquivo>().ToList(),
            _ => []
        };
    }

    static EditalResponse Mapear<TArquivo>(EditalBase item, IEnumerable<TArquivo> arquivos, Func<TArquivo, long> registroId)
        where TArquivo : EditalArquivoBase
    {
        return new EditalResponse(
            item.Id,
            item.UsuarioId,
            item.Titulo,
            item.Resumo,
            item.Conteudo,
            item.Numero,
            item.DataPublicacao,
            item.DataInicio,
            item.DataFim,
            item.Status,
            item.Ativo,
            item.CriadoEm,
            item.AtualizadoEm,
            arquivos.OrderByDescending(a => a.DataArquivo ?? a.CriadoEm)
                .Select(a => new EditalArquivoResponse(a.Id, registroId(a), a.Descricao, a.DataArquivo, a.CaminhoRelativo, a.NomeArquivo, a.Extensao, a.CriadoEm))
                .ToList()
        );
    }

    static void Aplicar(EditalBase item, EditalDto dto, long? usuarioId)
    {
        item.UsuarioId ??= usuarioId;
        item.Titulo = string.IsNullOrWhiteSpace(dto.Titulo) ? "Título não informado" : dto.Titulo.Trim();
        item.Resumo = dto.Resumo;
        item.Conteudo = dto.Conteudo;
        item.Numero = dto.Numero;
        item.DataPublicacao = Utc(dto.DataPublicacao);
        item.DataInicio = Utc(dto.DataInicio);
        item.DataFim = Utc(dto.DataFim);
        item.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Publicado" : dto.Status.Trim();
        item.Ativo = dto.Ativo;
    }

    async Task SincronizarProcessoArquivos(ProcessoSeletivo item, List<EditalArquivoDto>? arquivos)
    {
        if (arquivos is null) return;
        var atuais = await db.ProcessosSeletivosArquivos.Where(x => x.ProcessoSeletivoId == item.Id).ToListAsync();
        db.ProcessosSeletivosArquivos.RemoveRange(atuais);
        foreach (var a in arquivos.Where(x => !string.IsNullOrWhiteSpace(x.CaminhoRelativo)))
        {
            var caminho = a.CaminhoRelativo!.Trim();
            db.ProcessosSeletivosArquivos.Add(new ProcessoSeletivoArquivo
            {
                ProcessoSeletivoId = item.Id,
                Descricao = string.IsNullOrWhiteSpace(a.Descricao) ? Path.GetFileNameWithoutExtension(caminho) : a.Descricao.Trim(),
                DataArquivo = Utc(a.DataArquivo),
                CaminhoRelativo = caminho,
                NomeArquivo = Path.GetFileName(caminho),
                Extensao = Path.GetExtension(caminho),
                CriadoEm = DateTime.UtcNow
            });
        }
    }

    async Task SincronizarConcursoArquivos(Concurso item, List<EditalArquivoDto>? arquivos)
    {
        if (arquivos is null) return;
        var atuais = await db.ConcursosArquivos.Where(x => x.ConcursoId == item.Id).ToListAsync();
        db.ConcursosArquivos.RemoveRange(atuais);
        foreach (var a in arquivos.Where(x => !string.IsNullOrWhiteSpace(x.CaminhoRelativo)))
        {
            var caminho = a.CaminhoRelativo!.Trim();
            db.ConcursosArquivos.Add(new ConcursoArquivo
            {
                ConcursoId = item.Id,
                Descricao = string.IsNullOrWhiteSpace(a.Descricao) ? Path.GetFileNameWithoutExtension(caminho) : a.Descricao.Trim(),
                DataArquivo = Utc(a.DataArquivo),
                CaminhoRelativo = caminho,
                NomeArquivo = Path.GetFileName(caminho),
                Extensao = Path.GetExtension(caminho),
                CriadoEm = DateTime.UtcNow
            });
        }
    }

    static DateTime? Utc(DateTime? data)
    {
        if (data == null) return null;
        return data.Value.Kind switch
        {
            DateTimeKind.Utc => data.Value,
            DateTimeKind.Local => data.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(data.Value, DateTimeKind.Utc)
        };
    }

    static void NormalizarDatasUtc(EditalBase item)
    {
        item.DataPublicacao = Utc(item.DataPublicacao);
        item.DataInicio = Utc(item.DataInicio);
        item.DataFim = Utc(item.DataFim);
    }

    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
    private bool PodeAlterar(EditalBase item)
    {
        if (IsAdmin()) return true;
        var uid = Uid();
        return uid.HasValue && item.UsuarioId == uid.Value;
    }
}
