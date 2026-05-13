using System.Text.Json;
using Cameramg.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/vereadores")]
public class VereadoresController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var itens = new List<object>();
        var total = 0;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync();

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = @"
                SELECT COUNT(*)
                FROM vereadores
                WHERE ativo = TRUE
                  AND COALESCE(status, '') NOT IN ('Inativo','Arquivado','Cancelada','Bloqueado')";

            total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, titulo, status, dados_json, ativo, criado_em, atualizado_em
            FROM vereadores
            WHERE ativo = TRUE
              AND COALESCE(status, '') NOT IN ('Inativo','Arquivado','Cancelada','Bloqueado')
            ORDER BY id ASC
            LIMIT @limit OFFSET @offset";

        var pLimit = cmd.CreateParameter();
        pLimit.ParameterName = "limit";
        pLimit.Value = pageSize;
        cmd.Parameters.Add(pLimit);

        var pOffset = cmd.CreateParameter();
        pOffset.ParameterName = "offset";
        pOffset.Value = (page - 1) * pageSize;
        cmd.Parameters.Add(pOffset);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var dadosJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3);
            var dados = ParseJson(dadosJson);

            var titulo = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var nome = GetString(dados, "nome");
            var nomeParlamentar = GetString(dados, "nomeParlamentar");
            var foto = GetString(dados, "foto");

            itens.Add(MontarVereador(
                reader.GetInt64(0),
                titulo,
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                dadosJson,
                dados,
                !reader.IsDBNull(4) && reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6)));
        }

        // Compatibilidade: se a tabela pública vereadores ainda não recebeu a sincronização,
        // publica os vereadores cadastrados no painel administrativo sem depender de outra rota.
        if (itens.Count == 0)
        {
            var registros = await db.AdminRegistros.AsNoTracking()
                .Where(x => x.Tipo == "VEREADOR" && x.Ativo &&
                    (x.Status == null || !new[] { "Inativo", "Arquivado", "Cancelada", "Bloqueado" }.Contains(x.Status)))
                .OrderBy(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            total = await db.AdminRegistros.AsNoTracking()
                .CountAsync(x => x.Tipo == "VEREADOR" && x.Ativo &&
                    (x.Status == null || !new[] { "Inativo", "Arquivado", "Cancelada", "Bloqueado" }.Contains(x.Status)));

            foreach (var r in registros)
            {
                var dadosJson = string.IsNullOrWhiteSpace(r.DadosJson) ? "{}" : r.DadosJson!;
                var dados = ParseJson(dadosJson);
                itens.Add(MontarVereador(r.Id, r.Titulo, r.Status ?? "", dadosJson, dados, r.Ativo, r.CriadoEm, r.AtualizadoEm));
            }
        }

        return Ok(new { itens, page, pageSize, total });
    }

    private static object MontarVereador(long id, string titulo, string status, string dadosJson, Dictionary<string, object?> dados, bool ativo, DateTime? criadoEm, DateTime? atualizadoEm)
    {
        var nome = GetString(dados, "nome");
        var nomeParlamentar = GetString(dados, "nomeParlamentar");
        var foto = GetString(dados, "foto");

        if (string.IsNullOrWhiteSpace(nome)) nome = titulo;
        if (string.IsNullOrWhiteSpace(nomeParlamentar)) nomeParlamentar = nome;

        return new
        {
            id,
            titulo,
            status,
            dadosJson,
            dados,
            ativo,
            criadoEm,
            atualizadoEm,
            nome,
            nomeParlamentar,
            partido = GetString(dados, "partido"),
            cargo = GetString(dados, "cargo"),
            email = GetString(dados, "email"),
            telefone = GetString(dados, "telefone"),
            biografia = GetString(dados, "biografia"),
            foto,
            imagem = foto
        };
    }

    private static Dictionary<string, object?> ParseJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static string GetString(Dictionary<string, object?> dados, string key)
    {
        if (!dados.TryGetValue(key, out var value) || value is null)
            return "";

        if (value is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "",
                JsonValueKind.Number => el.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            };
        }

        return Convert.ToString(value) ?? "";
    }
}
