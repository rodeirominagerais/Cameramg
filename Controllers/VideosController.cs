using System.Data;
using System.Text.Json;
using Cameramg.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/videos")]
[Route("videos")]
[AllowAnonymous]
public class VideosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool? ativo = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var todos = await BuscarVideosAsync(ativo);
        var total = todos.Count;
        var itens = todos
            .OrderByDescending(x => x.AtualizadoEm ?? x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new { itens, page, pageSize, total });
    }

    [HttpGet("destaque")]
    public async Task<IActionResult> Destaque()
    {
        var todos = await BuscarVideosAsync(true);
        var item = todos
            .Where(x => !StatusInativo(x.Status))
            .OrderByDescending(x => EhDestaque(x.DadosJson))
            .ThenByDescending(x => x.AtualizadoEm ?? x.CriadoEm)
            .FirstOrDefault(x => TemUrlVideo(x.DadosJson));

        return item is null ? NotFound(new { erro = "Nenhum vídeo cadastrado com URL válida." }) : Ok(item);
    }

    private async Task<List<VideoPortalDto>> BuscarVideosAsync(bool? ativo)
    {
        var lista = new List<VideoPortalDto>();

        await using var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, titulo, status, dados_json, ativo, criado_em, atualizado_em, 'admin_registros' AS origem
              FROM admin_registros
             WHERE tipo = 'VIDEO' AND (@ativo IS NULL OR ativo = @ativo)
            UNION ALL
            SELECT id, titulo, status, dados_json, ativo, criado_em, atualizado_em, 'videos' AS origem
              FROM videos
             WHERE (@ativo IS NULL OR ativo = @ativo)
             ORDER BY atualizado_em DESC NULLS LAST, criado_em DESC";

        var p = cmd.CreateParameter();
        p.ParameterName = "@ativo";
        p.Value = ativo.HasValue ? ativo.Value : DBNull.Value;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lista.Add(new VideoPortalDto
            {
                Id = reader.GetInt64(0),
                Titulo = reader.GetString(1),
                Status = reader.IsDBNull(2) ? null : reader.GetString(2),
                DadosJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3),
                Ativo = reader.GetBoolean(4),
                CriadoEm = reader.GetDateTime(5),
                AtualizadoEm = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Origem = reader.GetString(7)
            });
        }

        return lista;
    }

    private static bool StatusInativo(string? status) =>
        new[] { "Arquivado", "Inativo", "Bloqueado", "Cancelada" }
            .Contains(status ?? "", StringComparer.OrdinalIgnoreCase);

    private static bool EhDestaque(string? json)
    {
        var d = LerJson(json);
        return d.TryGetValue("destaque", out var v) && string.Equals(v, "Sim", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TemUrlVideo(string? json)
    {
        var d = LerJson(json);
        return Valor(d, "url", "video", "link", "youtube", "youtubeUrl", "urlYoutube").Length > 0;
    }

    private static Dictionary<string, string> LerJson(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json ?? "{}")?
                .ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") ?? new();
        }
        catch { return new(); }
    }

    private static string Valor(Dictionary<string, string> d, params string[] keys) =>
        keys.Select(k => d.TryGetValue(k, out var v) ? v : "").FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}

public class VideoPortalDto
{
    public long Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string DadosJson { get; set; } = "{}";
    public bool Ativo { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public string Origem { get; set; } = string.Empty;
}
