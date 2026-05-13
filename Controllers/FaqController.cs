using System.Text.Json;
using Cameramg.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/faq")]
[AllowAnonymous]
public class FaqController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? categoria, [FromQuery] string? busca, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.AdminRegistros.AsNoTracking()
            .Where(x => x.Tipo == "FAQ" && x.Ativo && x.Status != "Arquivado" && x.Status != "Inativo");

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            q = q.Where(x => x.Titulo.Contains(termo) || (x.DadosJson != null && x.DadosJson.Contains(termo)));
        }

        var lista = await q.OrderByDescending(x => x.AtualizadoEm ?? x.CriadoEm).ToListAsync();
        var normalizada = lista
            .Select(x => NormalizarFaq(x.Id, x.Titulo, x.Status, x.DadosJson, x.CriadoEm, x.AtualizadoEm))
            .Where(x => string.IsNullOrWhiteSpace(categoria) || Igual(x.Categoria, categoria))
            .OrderBy(x => x.Ordem)
            .ThenByDescending(x => x.AtualizadoEm ?? x.CriadoEm)
            .ToList();

        var total = normalizada.Count;
        var itens = normalizada.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new { itens, page, pageSize, total });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obter(long id)
    {
        var item = await db.AdminRegistros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Tipo == "FAQ" && x.Ativo);
        if (item is null) return NotFound(new { erro = "Pergunta frequente não encontrada." });
        return Ok(NormalizarFaq(item.Id, item.Titulo, item.Status, item.DadosJson, item.CriadoEm, item.AtualizadoEm));
    }

    private sealed class FaqDto
    {
        public long Id { get; set; }
        public string Pergunta { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string Categoria { get; set; } = "Geral";
        public string Resposta { get; set; } = "";
        public string Resumo { get; set; } = "";
        public string Icone { get; set; } = "pergunta";
        public int Ordem { get; set; }
        public string Status { get; set; } = "Publicado";
        public DateTime CriadoEm { get; set; }
        public DateTime? AtualizadoEm { get; set; }
    }

    private static FaqDto NormalizarFaq(long id, string tituloBase, string? status, string? dadosJson, DateTime criadoEm, DateTime? atualizadoEm)
    {
        var d = Json(dadosJson);
        var pergunta = Get(d, "pergunta", "titulo");
        if (string.IsNullOrWhiteSpace(pergunta)) pergunta = tituloBase;
        var resposta = Get(d, "resposta", "conteudo", "descricao");
        var resumo = Get(d, "resumo");
        if (string.IsNullOrWhiteSpace(resumo)) resumo = resposta.Length > 150 ? resposta[..150] + "..." : resposta;

        return new FaqDto
        {
            Id = id,
            Pergunta = pergunta,
            Titulo = pergunta,
            Categoria = string.IsNullOrWhiteSpace(Get(d, "categoria", "grupo")) ? "Geral" : Get(d, "categoria", "grupo"),
            Resposta = resposta,
            Resumo = resumo,
            Icone = string.IsNullOrWhiteSpace(Get(d, "icone")) ? "pergunta" : Get(d, "icone"),
            Ordem = int.TryParse(Get(d, "ordem"), out var ordem) ? ordem : 999,
            Status = string.IsNullOrWhiteSpace(status) ? "Publicado" : status,
            CriadoEm = criadoEm,
            AtualizadoEm = atualizadoEm
        };
    }

    private static bool Igual(string? a, string? b)
    {
        static string N(string? v) => (v ?? "").Trim().ToLowerInvariant()
            .Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
            .Replace("é", "e").Replace("ê", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ô", "o").Replace("õ", "o")
            .Replace("ú", "u").Replace("ç", "c");
        return N(a) == N(b);
    }

    private static Dictionary<string, string> Json(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json ?? "{}")?
                .ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") ?? new();
        }
        catch { return new(); }
    }

    private static string Get(Dictionary<string, string> d, params string[] keys) =>
        keys.Select(k => d.TryGetValue(k, out var v) ? v : "").FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
