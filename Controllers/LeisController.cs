using System.Text.Json;
using Cameramg.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/leis")]
[AllowAnonymous]
public class LeisController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? categoria, [FromQuery] string? busca, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.AdminRegistros.AsNoTracking()
            .Where(x => x.Tipo == "LEI" && x.Ativo);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            q = q.Where(x => x.Titulo.Contains(termo) || (x.DadosJson != null && x.DadosJson.Contains(termo)));
        }

        var lista = await q.OrderByDescending(x => x.AtualizadoEm ?? x.CriadoEm).ToListAsync();

        var normalizada = lista
            .Select(x => NormalizarLei(x.Id, x.Titulo, x.Status, x.DadosJson, x.CriadoEm, x.AtualizadoEm))
            .Where(x => string.IsNullOrWhiteSpace(categoria) || CategoriaIgual(x.Categoria, categoria))
            .ToList();

        var total = normalizada.Count;
        var itens = normalizada.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new { itens, page, pageSize, total });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obter(long id)
    {
        var item = await db.AdminRegistros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Tipo == "LEI" && x.Ativo);
        if (item is null) return NotFound(new { erro = "Lei não encontrada." });
        return Ok(NormalizarLei(item.Id, item.Titulo, item.Status, item.DadosJson, item.CriadoEm, item.AtualizadoEm));
    }

    private sealed class LeiDto
    {
        public long Id { get; set; }
        public string Titulo { get; set; } = "";
        public string Categoria { get; set; } = "";
        public string Numero { get; set; } = "";
        public string Data { get; set; } = "";
        public string Resumo { get; set; } = "";
        public string Conteudo { get; set; } = "";
        public string Arquivo { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? AtualizadoEm { get; set; }
    }

    private static LeiDto NormalizarLei(long id, string tituloBase, string? status, string? dadosJson, DateTime criadoEm, DateTime? atualizadoEm)
    {
        var d = Json(dadosJson);
        var tituloJson = Get(d, "titulo");
        var titulo = string.IsNullOrWhiteSpace(tituloJson) ? tituloBase : tituloJson;
        var data = Get(d, "data", "dataCriacao", "dataPublicacao");

        return new LeiDto
        {
            Id = id,
            Titulo = titulo,
            Categoria = Get(d, "categoria", "tipo", "modalidade"),
            Numero = Get(d, "numero"),
            Data = string.IsNullOrWhiteSpace(data) ? criadoEm.ToString("yyyy-MM-dd") : data,
            Resumo = Get(d, "resumo", "ementa", "descricao"),
            Conteudo = Get(d, "conteudo", "detalhes", "ementa", "descricao"),
            Arquivo = Get(d, "arquivo", "pdf", "documento", "anexo"),
            Status = string.IsNullOrWhiteSpace(status) ? "Publicado" : status,
            AtualizadoEm = atualizadoEm
        };
    }

    private static bool CategoriaIgual(string? a, string? b)
    {
        static string N(string? v) => (v ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
            .Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i")
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
