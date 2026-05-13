using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/licitacoes")]
public class LicitacoesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? busca, [FromQuery] string? status, [FromQuery] string? modalidade, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(x => x.Status.ToLower() == status.ToLower());

        if (!string.IsNullOrWhiteSpace(modalidade))
            q = q.Where(x => x.Modalidade.ToLower().Contains(modalidade.ToLower()));

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.ToLower();
            q = q.Where(x =>
                (x.Numero != null && x.Numero.ToLower().Contains(b)) ||
                (x.ProcessoAdministrativo != null && x.ProcessoAdministrativo.ToLower().Contains(b)) ||
                (x.NumeroLicitacao != null && x.NumeroLicitacao.ToLower().Contains(b)) ||
                x.Modalidade.ToLower().Contains(b) ||
                x.Objeto.ToLower().Contains(b));
        }

        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.DataAbertura ?? x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<LicitacaoResponse>(itens.Select(Mapear).ToList(), page, pageSize, total));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obter(long id)
    {
        var item = await db.Licitacoes.AsNoTracking().Include(x => x.Arquivos).FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound(new { erro = "Licitação não encontrada." }) : Ok(Mapear(item));
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Criar(LicitacaoDto dto)
    {
        var item = new Licitacao { UsuarioId = null, CriadoEm = DateTime.UtcNow };
        Aplicar(item, dto);
        db.Licitacoes.Add(item);
        await db.SaveChangesAsync();
        await SincronizarArquivos(item, dto.Arquivos);
        NormalizarDatasUtc(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = item.Id }, Mapear(item));
    }

    [HttpPut("{id:long}"), AllowAnonymous]
    public async Task<IActionResult> Atualizar(long id, LicitacaoDto dto)
    {
        var item = await db.Licitacoes
            .Include(x => x.Arquivos)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Licitação não encontrada.");

        // Não bloquear edição de licitação por usuario_id.
        // O painel administrativo usa token do usuário logado, mas algumas licitações
        // antigas ou inseridas direto no banco podem ter usuario_id diferente.
        // Esse bloqueio causava 403 Forbidden no PUT /api/licitacoes/{id}.

        Aplicar(item, dto);

        item.AtualizadoEm = DateTime.UtcNow;

        await SincronizarArquivos(item, dto.Arquivos);

        NormalizarDatasUtc(item);

        await db.SaveChangesAsync();

        return Ok(Mapear(item));
    }

    private static LicitacaoResponse Mapear(Licitacao item)
    {
        return new LicitacaoResponse(
            item.Id,
            item.UsuarioId,
            item.Orgao,
            item.Telefone,
            item.EmailPrincipal,
            item.Modalidade,
            item.ProcessoAdministrativo,
            item.NumeroLicitacao,
            item.Numero,
            item.Exercicio,
            item.Fornecedor,
            item.Objeto,
            item.Valor,
            item.DataAbertura,
            item.DataEncerramento,
            item.EmailPropostas,
            item.PropostasInicio,
            item.PropostasFim,
            item.Julgamento,
            item.Informacoes,
            item.Status,
            item.CriadoEm,
            item.AtualizadoEm,
            item.Arquivos
                .OrderByDescending(a => a.DataArquivo ?? a.CriadoEm)
                .Select(a => new LicitacaoArquivoResponse(
                    a.Id,
                    a.LicitacaoId,
                    a.Descricao,
                    a.DataArquivo,
                    a.CaminhoRelativo,
                    a.NomeArquivo,
                    a.Extensao,
                    a.CriadoEm
                ))
                .ToList()
        );
    }

    private static DateTime? Utc(DateTime? data)
    {
        if (data == null)
            return null;

        return data.Value.Kind switch
        {
            DateTimeKind.Utc => data.Value,
            DateTimeKind.Local => data.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(data.Value, DateTimeKind.Utc)
        };
    }

    private static void NormalizarDatasUtc(Licitacao item)
    {
        item.DataAbertura = Utc(item.DataAbertura);
        item.DataEncerramento = Utc(item.DataEncerramento);
        item.PropostasInicio = Utc(item.PropostasInicio);
        item.PropostasFim = Utc(item.PropostasFim);
        item.Julgamento = Utc(item.Julgamento);

        if (item.Arquivos == null)
            return;

        foreach (var arq in item.Arquivos)
        {
            arq.DataArquivo = Utc(arq.DataArquivo);
        }
    }

    [HttpDelete("{id:long}"), AllowAnonymous]
    public async Task<IActionResult> Remover(long id)
    {
        var item = await db.Licitacoes.FindAsync(id)
            ?? throw new InvalidOperationException("Licitação não encontrada.");
        db.Licitacoes.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static void Aplicar(Licitacao item, LicitacaoDto dto)
    {
        item.Orgao = dto.Orgao;
        item.Telefone = dto.Telefone;
        item.EmailPrincipal = dto.EmailPrincipal;
        item.Modalidade = string.IsNullOrWhiteSpace(dto.Modalidade)
            ? "Licitação"
            : dto.Modalidade.Trim();

        item.ProcessoAdministrativo = dto.ProcessoAdministrativo;
        item.NumeroLicitacao = dto.NumeroLicitacao;
        item.Numero = string.IsNullOrWhiteSpace(dto.Numero)
            ? dto.Titulo
            : dto.Numero;

        item.Exercicio = dto.Exercicio;
        item.Fornecedor = dto.Fornecedor;

        item.Objeto = string.IsNullOrWhiteSpace(dto.Objeto)
            ? (dto.Titulo ?? "Objeto não informado")
            : dto.Objeto.Trim();

        item.Valor = dto.Valor;

        item.DataAbertura = Utc(dto.DataAbertura);
        item.DataEncerramento = Utc(dto.DataEncerramento);

        item.EmailPropostas = dto.EmailPropostas;

        item.PropostasInicio = Utc(dto.PropostasInicio);
        item.PropostasFim = Utc(dto.PropostasFim);

        item.Julgamento = Utc(dto.Julgamento);

        item.Informacoes = dto.Informacoes;

        item.Status = string.IsNullOrWhiteSpace(dto.Status)
            ? "EM ANDAMENTO"
            : dto.Status.Trim();
    }



    private async Task SincronizarArquivos(Licitacao item, List<LicitacaoArquivoDto>? arquivos)
    {
        if (arquivos is null) return;
        var validos = arquivos.Where(x => !string.IsNullOrWhiteSpace(x.CaminhoRelativo)).ToList();
        var atuais = await db.LicitacaoArquivos.Where(x => x.LicitacaoId == item.Id).ToListAsync();
        db.LicitacaoArquivos.RemoveRange(atuais);
        foreach (var a in validos)
        {
            var caminho = a.CaminhoRelativo!.Trim();
            db.LicitacaoArquivos.Add(new LicitacaoArquivo
            {
                LicitacaoId = item.Id,
                Descricao = string.IsNullOrWhiteSpace(a.Descricao)
          ? Path.GetFileNameWithoutExtension(caminho)
          : a.Descricao.Trim(),

                DataArquivo = Utc(a.DataArquivo),

                CaminhoRelativo = caminho,
                NomeArquivo = Path.GetFileName(caminho),
                Extensao = Path.GetExtension(caminho),
                CriadoEm = DateTime.UtcNow
            });
       
        }
    }

    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
