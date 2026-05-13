using System.Security.Claims;
using System.Text.Json;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController, Route("api/admin-registros")]
public class AdminRegistrosController(AppDbContext db, SlugService slug) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Listar(
        [FromQuery] string? tipo,
        [FromQuery] string? busca,
        [FromQuery] string? status,
        [FromQuery] bool? ativo = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var tipoFiltro = string.IsNullOrWhiteSpace(tipo) ? null : Tipo(tipo);
        var autenticado = User.Identity?.IsAuthenticated ?? false;
        var admin = IsAdmin();
        var consultaPublica = !string.IsNullOrWhiteSpace(tipoFiltro) && TipoPodeSerListadoPublicamente(tipoFiltro);

        // MESMO PADRÃO DAS LICITAÇÕES/PÁGINAS PÚBLICAS:
        // GET público não pode travar o portal com 401.
        // Sem login, só lista tipos explicitamente públicos e sempre filtrando Ativo/status visível.
        // Publicar/criar/editar/excluir continua protegido nos métodos POST/PUT/DELETE.
        if (!autenticado && !consultaPublica)
            return Ok(new ApiPage<AdminRegistro>(new List<AdminRegistro>(), page, pageSize, 0));

        if (TipoRestritoAdmin(tipoFiltro) && !admin) return Forbid();

        var q = db.AdminRegistros.AsNoTracking().AsQueryable();
        var uid = Uid();

        if (!autenticado && consultaPublica)
        {
            q = q.Where(x => x.Ativo && !new[] { "Arquivado", "Inativo", "Bloqueado", "Cancelada" }.Contains(x.Status));
        }
        else if (!admin && uid.HasValue)
        {
            q = q.Where(x => x.UsuarioId == uid.Value);
        }
        if (!string.IsNullOrWhiteSpace(tipoFiltro))
        {
            var tiposPermitidos = TiposEquivalentes(tipoFiltro);
            q = q.Where(x => tiposPermitidos.Contains(x.Tipo));
        }
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (ativo.HasValue) q = q.Where(x => x.Ativo == ativo.Value);
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Titulo.Contains(busca) || (x.DadosJson != null && x.DadosJson.Contains(busca)));

        var total = await q.CountAsync();
        var itens = await q.OrderByDescending(x => x.AtualizadoEm ?? x.CriadoEm).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new ApiPage<AdminRegistro>(itens, page, pageSize, total));
    }

    [HttpGet("{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Obter(long id)
    {
        var uid = Uid();
        var item = await db.AdminRegistros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NoContent();

        var consultaPublica = TipoPodeSerListadoPublicamente(item.Tipo) && item.Ativo && !StatusInativo(item.Status);
        var autenticado = User.Identity?.IsAuthenticated ?? false;

        if (!autenticado && !consultaPublica)
            return Unauthorized(new { erro = "Autenticação necessária para consultar este registro." });

        if (TipoRestritoAdmin(item.Tipo) && !IsAdmin()) return Forbid();

        // Registro público pode ser visualizado no portal sem travar por usuário dono.
        if (!consultaPublica && !IsAdmin() && uid.HasValue && item.UsuarioId != uid.Value) return Forbid();

        return Ok(item);
    }

    [HttpPost, Authorize(Policy = "Editor")]
    public async Task<IActionResult> Criar(AdminRegistroDto dto)
    {
        Validar(dto);
        if (TipoExigeAdminParaPublicar(dto.Tipo) && !IsAdmin()) return Forbid();
        var item = new AdminRegistro { UsuarioId = Uid(), Tipo = Tipo(dto.Tipo), Titulo = dto.Titulo.Trim(), Status = NormalizarStatus(dto.Status), DadosJson = dto.DadosJson, Ativo = dto.Ativo };
        db.AdminRegistros.Add(item);
        await db.SaveChangesAsync();
        await SincronizarAsync(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = item.Id }, item);
    }

    [HttpPut("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Atualizar(long id, AdminRegistroDto dto)
    {
        Validar(dto);

        var novoTipo = Tipo(dto.Tipo);
        if (TipoExigeAdminParaPublicar(novoTipo) && !IsAdmin()) return Forbid();

        var uid = Uid();
        var item = await db.AdminRegistros.FirstOrDefaultAsync(x => x.Id == id);

        // Evita exceção quando o painel tenta salvar um registro antigo que não existe mais no banco.
        if (item is null)
        {
            item = new AdminRegistro
            {
                UsuarioId = uid,
                CriadoEm = DateTime.UtcNow
            };
            db.AdminRegistros.Add(item);
        }
        else
        {
            if (TipoExigeAdminParaPublicar(item.Tipo) && !IsAdmin()) return Forbid();
            if (!IsAdmin() && uid.HasValue && item.UsuarioId != uid.Value) return Forbid();
        }

        item.Tipo = novoTipo;
        item.Titulo = dto.Titulo.Trim();
        item.Status = NormalizarStatus(dto.Status);
        item.DadosJson = dto.DadosJson;
        item.Ativo = dto.Ativo;
        item.AtualizadoEm = DateTime.UtcNow;

        await SincronizarAsync(item);
        await db.SaveChangesAsync();

        return Ok(item);
    }

    [HttpDelete("{id:long}"), Authorize(Policy = "Editor")]
    public async Task<IActionResult> Remover(long id)
    {
        var item = await db.AdminRegistros.FindAsync(id);
        if (item is null) return NoContent();
        if (TipoExigeAdminParaPublicar(item.Tipo) && !IsAdmin()) return Forbid();
        var uid = Uid();
        if (!IsAdmin() && uid.HasValue && item.UsuarioId != uid.Value) return Forbid();

        item.Ativo = false;
        item.AtualizadoEm = DateTime.UtcNow;
        await InativarEntidadeAsync(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task SincronizarAsync(AdminRegistro r)
    {
        var d = Json(r.DadosJson);
        switch (r.Tipo)
        {
            case "NOTICIA": case "LICITACAO": case "TRANSPARENCIA": await SyncPublicacao(r, d); break;
            case "DOCUMENTO": case "DIARIO": await SyncArquivo(r, d); break;
            case "PAGINA": await SyncPagina(r, d); break;
            case "SUBMENU": await SyncSubmenuPagina(r, d); break;
            case "USUARIO": await SyncUsuario(r, d); break;
            case "CHAMADO": await SyncChamado(r, d); break;
            case "CONFIG": await SyncConfig(r, d); break;
            case "VEREADOR": await SyncGenerica(r, "vereadores", d); break;
            case "SESSAO": await SyncGenerica(r, "sessoes_legislativas", d); break;
            case "VIDEO": await SyncGenerica(r, "videos", d); break;
            case "EVENTO": await SyncGenerica(r, "eventos_agenda", d); break;
            case "ATAS_DE_REUNIOES": case "PORTARIA": case "REQUERIMENTO": case "CONVOCACAO": case "INDICACAO":
            case "MOCAO": case "RESOLUCAO": case "PROJETO_DE_RESOLUCOES": case "DIPLOMA": case "DECRETO":
                await SyncAtividadeParlamentar(r, d); break;
            default: break;
        }
    }

    private async Task SyncPublicacao(AdminRegistro r, Dictionary<string,string> d)
    {
        Publicacao p;
        if (r.Entidade == "publicacoes" && r.EntidadeId.HasValue) p = await db.Publicacoes.FindAsync(r.EntidadeId.Value) ?? new Publicacao();
        else { p = new Publicacao(); db.Publicacoes.Add(p); }
        p.UsuarioId ??= r.UsuarioId;
        p.Tipo = r.Tipo;
        p.Titulo = r.Titulo;
        p.Resumo = Get(d, "resumo", "objeto", "descricao");
        p.ConteudoHtml = Get(d, "conteudo", "conteudoHtml", "objeto", "descricao");
        p.ImagemCapa = Get(d, "capa", "imagem", "thumbnail");
        p.Modalidade = Get(d, "modalidade");
        p.Fornecedor = Get(d, "orgao", "empresa", "fornecedor");
        p.Situacao = r.Status;
        p.Destaque = Get(d, "destaque").Equals("Sim", StringComparison.OrdinalIgnoreCase) || r.Tipo == "TRANSPARENCIA";
        p.DataPublicacao = ParseDate(Get(d, "data")) ?? p.DataPublicacao ?? DateTime.UtcNow;
        p.DataAbertura = ParseDate(Get(d, "abertura"));
        p.DataEncerramento = ParseDate(Get(d, "encerramento"));
        p.Slug ??= slug.Gerar(r.Titulo);
        p.Ativo = r.Ativo && !StatusInativo(r.Status);
        p.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        r.Entidade = "publicacoes"; r.EntidadeId = p.Id;

        if (r.Tipo == "LICITACAO")
        {
            await SincronizarArquivosLicitacaoAsync(r, p, d);
        }
    }

    private async Task SincronizarArquivosLicitacaoAsync(AdminRegistro r, Publicacao p, Dictionary<string,string> d)
    {
        var arquivos = new List<(string Titulo, string Caminho)>();

        void AddArquivo(string titulo, string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return;
            var partes = valor.Split(new[] { '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (partes.Length == 0) partes = new[] { valor.Trim() };
            for (var i = 0; i < partes.Length; i++)
            {
                var caminho = partes[i];
                if (string.IsNullOrWhiteSpace(caminho)) continue;
                arquivos.Add((partes.Length > 1 ? $"{titulo} {i + 1}" : titulo, caminho));
            }
        }

        AddArquivo("Edital", Get(d, "edital"));
        AddArquivo("Anexo", Get(d, "anexos", "anexo", "arquivo"));
        AddArquivo("Termo de Referência", Get(d, "termo", "termoReferencia"));
        AddArquivo("Estudo Técnico Preliminar", Get(d, "estudo", "etp"));
        AddArquivo("Extrato de Homologação", Get(d, "homologacao", "extratoHomologacao"));

        foreach (var arq in arquivos)
        {
            var existente = await db.Arquivos.FirstOrDefaultAsync(x => x.PublicacaoId == p.Id && x.CaminhoRelativo == arq.Caminho);
            if (existente is null)
            {
                existente = new Arquivo
                {
                    UsuarioId = r.UsuarioId,
                    PublicacaoId = p.Id,
                    CriadoEm = DateTime.UtcNow
                };
                db.Arquivos.Add(existente);
            }

            existente.Tipo = "LICITACAO";
            existente.Titulo = arq.Titulo;
            existente.NomeArquivo = Path.GetFileName(arq.Caminho);
            existente.CaminhoRelativo = arq.Caminho;
            existente.Extensao = Path.GetExtension(arq.Caminho);
            existente.Origem = "portal";
            existente.Visivel = r.Ativo && !StatusInativo(r.Status);
            existente.DataArquivo = DateOnlyFrom(ParseDate(Get(d, "data", "abertura")));
        }

        await db.SaveChangesAsync();
    }

    private async Task SyncArquivo(AdminRegistro r, Dictionary<string,string> d)
    {
        Arquivo a;
        if (r.Entidade == "arquivos" && r.EntidadeId.HasValue) a = await db.Arquivos.FindAsync(r.EntidadeId.Value) ?? new Arquivo();
        else { a = new Arquivo(); db.Arquivos.Add(a); }
        var caminho = Get(d, "arquivo", "edital", "anexos", "pauta", "ata");
        a.Tipo = r.Tipo;
        a.Titulo = r.Titulo;
        a.NomeArquivo = string.IsNullOrWhiteSpace(caminho) ? r.Titulo : Path.GetFileName(caminho);
        a.CaminhoRelativo = caminho;
        a.Extensao = Path.GetExtension(caminho);
        a.Origem = "portal";
        a.Visivel = r.Ativo && !StatusInativo(r.Status);
        a.DataArquivo = DateOnlyFrom(ParseDate(Get(d, "data")));
        await db.SaveChangesAsync();
        r.Entidade = "arquivos"; r.EntidadeId = a.Id;
    }

    private async Task SyncPagina(AdminRegistro r, Dictionary<string,string> d)
    {
        var chave = slug.Gerar(Get(d, "pagina") == "" ? r.Titulo : Get(d, "pagina"));
        var p = r.Entidade == "paginas_institucionais" && r.EntidadeId.HasValue ? await db.PaginasInstitucionais.FindAsync(r.EntidadeId.Value) : await db.PaginasInstitucionais.FirstOrDefaultAsync(x => x.Chave == chave);
        if (p is null) { p = new PaginaInstitucional{ UsuarioId = r.UsuarioId }; db.PaginasInstitucionais.Add(p); }
        p.Chave = chave; p.Titulo = r.Titulo; p.ConteudoHtml = Get(d, "conteudo", "descricao"); p.ImagemCapa = Get(d, "imagem", "capa"); p.Ativo = r.Ativo && !StatusInativo(r.Status); p.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(); r.Entidade = "paginas_institucionais"; r.EntidadeId = p.Id;
    }

    private async Task SyncSubmenuPagina(AdminRegistro r, Dictionary<string,string> d)
    {
        var menu = Get(d, "menu", "menuPai", "grupo");
        var pagina = Get(d, "pagina", "nome", "subtitulo");
        var slugValor = Get(d, "slug");
        var rota = Get(d, "rota", "url", "link");

        if (string.IsNullOrWhiteSpace(menu)) menu = "A Câmara";
        if (string.IsNullOrWhiteSpace(pagina)) pagina = r.Titulo;
        if (string.IsNullOrWhiteSpace(slugValor)) slugValor = slug.Gerar(pagina);
        if (string.IsNullOrWhiteSpace(rota)) rota = "/" + slugValor.Trim('/');
        if (!rota.StartsWith('/')) rota = "/" + rota;

        SubmenuPagina? p = null;
        if (r.Entidade == "submenu_paginas" && r.EntidadeId.HasValue)
            p = await db.SubmenuPaginas.FindAsync(r.EntidadeId.Value);

        p ??= await db.SubmenuPaginas.FirstOrDefaultAsync(x => x.Rota == rota);

        if (p is null)
        {
            p = new SubmenuPagina { UsuarioId = r.UsuarioId, CriadoEm = DateTime.UtcNow };
            db.SubmenuPaginas.Add(p);
        }

        p.UsuarioId ??= r.UsuarioId;
        p.Menu = menu.Trim();
        p.Pagina = pagina.Trim();
        p.Slug = slugValor.Trim().Trim('/');
        p.Rota = rota.Trim();
        p.Titulo = r.Titulo.Trim();
        p.ConteudoHtml = Get(d, "conteudo", "conteudoHtml", "descricao");
        p.Imagem = Get(d, "imagem", "capa", "thumbnail");
        p.Arquivo = Get(d, "arquivo", "anexo");
        p.Status = NormalizarStatus(r.Status);
        p.Ativo = r.Ativo && !StatusInativo(r.Status);
        p.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();
        r.Entidade = "submenu_paginas";
        r.EntidadeId = p.Id;
    }

    private async Task SyncUsuario(AdminRegistro r, Dictionary<string,string> d)
    {
        if (!IsAdmin()) throw new UnauthorizedAccessException("Somente administradores podem sincronizar usuários.");
        var email = Get(d, "email"); if (string.IsNullOrWhiteSpace(email)) return;
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
        if (u is null) { u = new Usuario(); db.Usuarios.Add(u); }
        u.Nome = Get(d, "nome") == "" ? r.Titulo : Get(d, "nome"); u.Email = email; u.Perfil = Get(d, "perfil") == "" ? "editor" : Get(d, "perfil"); u.Ativo = r.Ativo && !Get(d,"status").Equals("Bloqueado", StringComparison.OrdinalIgnoreCase);
        var senha = Get(d, "senha"); if (!string.IsNullOrWhiteSpace(senha)) u.SenhaHash = BCrypt.Net.BCrypt.HashPassword(senha); else if (string.IsNullOrWhiteSpace(u.SenhaHash)) u.SenhaHash = BCrypt.Net.BCrypt.HashPassword("admin123");
        u.AtualizadoEm = DateTime.UtcNow; await db.SaveChangesAsync(); r.Entidade = "usuarios"; r.EntidadeId = u.Id;
    }

    private async Task SyncChamado(AdminRegistro r, Dictionary<string,string> d)
    {
        OuvidoriaChamado c;
        if (r.Entidade == "ouvidoria_chamados" && r.EntidadeId.HasValue) c = await db.OuvidoriaChamados.FindAsync(r.EntidadeId.Value) ?? new OuvidoriaChamado();
        else { c = new OuvidoriaChamado { UsuarioId = r.UsuarioId, Protocolo = $"OUV-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000,9999)}" }; db.OuvidoriaChamados.Add(c); }
        c.Assunto = Get(d, "assunto") == "" ? r.Titulo : Get(d, "assunto"); c.Mensagem = Get(d, "mensagem", "resposta"); c.Status = r.Status ?? "Aberto"; c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(); r.Entidade = "ouvidoria_chamados"; r.EntidadeId = c.Id;
    }

    private async Task SyncConfig(AdminRegistro r, Dictionary<string,string> d)
    {
        foreach (var kv in d.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var c = await db.ConfiguracoesSite.FirstOrDefaultAsync(x => x.Chave == kv.Key && x.UsuarioId == r.UsuarioId);
            if (c is null) { c = new ConfiguracaoSite { Chave = kv.Key, UsuarioId = r.UsuarioId }; db.ConfiguracoesSite.Add(c); }
            c.Valor = kv.Value; c.Descricao = "Configuração editada pelo painel administrativo";
        }
    }

    private async Task SyncAtividadeParlamentar(AdminRegistro r, Dictionary<string,string> d)
    {
        if (!IsAdmin()) throw new UnauthorizedAccessException("Somente administradores podem publicar atividade parlamentar.");

        var tabela = TabelaAtividadeParlamentar(r.Tipo);
        var titulo = r.Titulo;
        var resumo = Get(d, "resumo", "ementa", "descricao");
        var conteudo = Get(d, "conteudo", "conteudoHtml", "detalhes", "ementa", "descricao");
        var arquivo = Get(d, "arquivo", "pdf", "documento", "anexo");
        var numero = Get(d, "numero");
        var data = ParseDate(Get(d, "dataCriacao", "data", "dataPublicacao"));
        var status = NormalizarStatus(r.Status);
        var ativo = r.Ativo && !StatusInativo(status);

        if (r.Entidade == tabela && r.EntidadeId.HasValue)
        {
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE {tabela} SET usuario_id={{0}}, titulo={{1}}, resumo={{2}}, conteudo={{3}}, arquivo={{4}}, numero={{5}}, data_criacao={{6}}, status={{7}}, ativo={{8}}, atualizado_em=NOW() WHERE id={{9}}",
                r.UsuarioId, titulo, resumo, conteudo, arquivo, numero, data, status, ativo, r.EntidadeId.Value);
        }
        else
        {
            await db.Database.OpenConnectionAsync();
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"INSERT INTO {tabela} (usuario_id, titulo, resumo, conteudo, arquivo, numero, data_criacao, status, ativo, criado_em) VALUES (@usuario_id, @titulo, @resumo, @conteudo, @arquivo, @numero, @data_criacao, @status, @ativo, NOW()) RETURNING id";
            AddParam(cmd, "@usuario_id", r.UsuarioId ?? (object)DBNull.Value);
            AddParam(cmd, "@titulo", titulo);
            AddParam(cmd, "@resumo", string.IsNullOrWhiteSpace(resumo) ? DBNull.Value : resumo);
            AddParam(cmd, "@conteudo", string.IsNullOrWhiteSpace(conteudo) ? DBNull.Value : conteudo);
            AddParam(cmd, "@arquivo", string.IsNullOrWhiteSpace(arquivo) ? DBNull.Value : arquivo);
            AddParam(cmd, "@numero", string.IsNullOrWhiteSpace(numero) ? DBNull.Value : numero);
            AddParam(cmd, "@data_criacao", data ?? (object)DBNull.Value);
            AddParam(cmd, "@status", status);
            AddParam(cmd, "@ativo", ativo);
            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            r.Entidade = tabela;
            r.EntidadeId = id;
        }
    }

    private async Task SyncGenerica(AdminRegistro r, string tabela, Dictionary<string,string> d)
    {
        var json = r.DadosJson ?? "{}";
        if (r.Entidade == tabela && r.EntidadeId.HasValue)
        {
            await db.Database.ExecuteSqlRawAsync($"UPDATE {tabela} SET titulo={{0}}, status={{1}}, dados_json={{2}}, ativo={{3}}, atualizado_em=NOW() WHERE id={{4}} AND (usuario_id={{5}} OR usuario_id IS NULL)", r.Titulo, r.Status, json, r.Ativo, r.EntidadeId.Value, r.UsuarioId ?? 0);
        }
        else
        {
            await db.Database.OpenConnectionAsync();
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"INSERT INTO {tabela} (titulo, status, dados_json, ativo, criado_em, usuario_id) VALUES (@titulo, @status, @dados, @ativo, NOW(), @usuario_id) RETURNING id";
            AddParam(cmd, "@titulo", r.Titulo);
            AddParam(cmd, "@status", r.Status ?? (object)DBNull.Value);
            AddParam(cmd, "@dados", json);
            AddParam(cmd, "@ativo", r.Ativo);
            AddParam(cmd, "@usuario_id", r.UsuarioId ?? (object)DBNull.Value);
            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            r.Entidade = tabela; r.EntidadeId = id;
        }
    }

    private async Task InativarEntidadeAsync(AdminRegistro r)
    {
        if (string.IsNullOrWhiteSpace(r.Entidade) || !r.EntidadeId.HasValue) return;
        if (r.Entidade == "publicacoes") { var x = await db.Publicacoes.FindAsync(r.EntidadeId.Value); if (x != null) x.Ativo = false; return; }
        if (r.Entidade == "arquivos") { var x = await db.Arquivos.FindAsync(r.EntidadeId.Value); if (x != null) x.Visivel = false; return; }
        if (r.Entidade == "paginas_institucionais") { var x = await db.PaginasInstitucionais.FindAsync(r.EntidadeId.Value); if (x != null) x.Ativo = false; return; }
        if (r.Entidade == "usuarios") { var x = await db.Usuarios.FindAsync(r.EntidadeId.Value); if (x != null) x.Ativo = false; return; }
        if (r.Entidade == "submenu_paginas") { var x = await db.SubmenuPaginas.FindAsync(r.EntidadeId.Value); if (x != null) x.Ativo = false; return; }
        var tabelas = new[] { "vereadores", "sessoes_legislativas", "videos", "eventos_agenda", "diarios_oficiais", "atas_reunioes", "portarias", "requerimentos", "convocacoes", "indicacoes", "mocoes", "resolucoes", "projetos_resolucoes", "diplomas", "decretos" };
        if (tabelas.Contains(r.Entidade)) await db.Database.ExecuteSqlRawAsync($"UPDATE {r.Entidade} SET ativo=false, atualizado_em=NOW() WHERE id={{0}}", r.EntidadeId.Value);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static Dictionary<string,string> Json(string? json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string,object?>>(json ?? "{}")?.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") ?? new(); }
        catch { return new(); }
    }
    private static string Get(Dictionary<string,string> d, params string[] keys) => keys.Select(k => d.TryGetValue(k, out var v) ? v : "").FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    private static DateTime? ParseDate(string s) => DateTime.TryParse(s, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;
    private static DateOnly? DateOnlyFrom(DateTime? d) => d.HasValue ? DateOnly.FromDateTime(d.Value) : null;
    private static string Tipo(string tipo)
    {
        var t = (tipo ?? "")
            .Trim()
            .ToUpperInvariant()
            .Replace("Á", "A").Replace("À", "A").Replace("Ã", "A").Replace("Â", "A")
            .Replace("É", "E").Replace("Ê", "E")
            .Replace("Í", "I")
            .Replace("Ó", "O").Replace("Ô", "O").Replace("Õ", "O")
            .Replace("Ú", "U")
            .Replace("Ç", "C")
            .Replace("-", "_").Replace(" ", "_");

        return t switch
        {
            "ATAS_REUNIOES" or "ATA_REUNIAO" or "ATAS_DE_REUNIOES" => "ATAS_DE_REUNIOES",
            "PORTARIAS" or "PORTARIA" => "PORTARIA",
            "REQUERIMENTOS" or "REQUERIMENTO" => "REQUERIMENTO",
            "CONVOCACOES" or "CONVOCACAO" => "CONVOCACAO",
            "INDICACOES" or "INDICACAO" => "INDICACAO",
            "MOCOES" or "MOCAO" => "MOCAO",
            "RESOLUCOES" or "RESOLUCAO" => "RESOLUCAO",
            "PROJETOS_RESOLUCOES" or "PROJETOS_DE_RESOLUCOES" or "PROJETO_RESOLUCAO" or "PROJETO_DE_RESOLUCOES" => "PROJETO_DE_RESOLUCOES",
            "DIPLOMAS" or "DIPLOMA" => "DIPLOMA",
            "DECRETOS" or "DECRETO" => "DECRETO",
            _ => t
        };
    }
    private static string[] TiposEquivalentes(string? tipo)
    {
        var t = Tipo(tipo ?? "");
        return t switch
        {
            "ATAS_DE_REUNIOES" => new[] { "ATAS_DE_REUNIOES", "ATAS_REUNIOES", "ATA_REUNIAO" },
            "PORTARIA" => new[] { "PORTARIA", "PORTARIAS" },
            "REQUERIMENTO" => new[] { "REQUERIMENTO", "REQUERIMENTOS" },
            "CONVOCACAO" => new[] { "CONVOCACAO", "CONVOCACOES" },
            "INDICACAO" => new[] { "INDICACAO", "INDICACOES" },
            "MOCAO" => new[] { "MOCAO", "MOCOES" },
            "RESOLUCAO" => new[] { "RESOLUCAO", "RESOLUCOES" },
            "PROJETO_DE_RESOLUCOES" => new[] { "PROJETO_DE_RESOLUCOES", "PROJETOS_DE_RESOLUCOES", "PROJETO_RESOLUCAO", "PROJETOS_RESOLUCOES" },
            "DIPLOMA" => new[] { "DIPLOMA", "DIPLOMAS" },
            "DECRETO" => new[] { "DECRETO", "DECRETOS" },
            _ => new[] { t }
        };
    }

    private static bool StatusInativo(string? s) => new[] { "Arquivado", "Inativo", "Bloqueado", "Cancelada" }.Contains(s ?? "");
    private static bool TipoAtividadeParlamentar(string? tipo) => new[]
    {
        "ATAS_DE_REUNIOES", "PORTARIA", "REQUERIMENTO", "CONVOCACAO", "INDICACAO",
        "MOCAO", "RESOLUCAO", "PROJETO_DE_RESOLUCOES", "DIPLOMA", "DECRETO"
    }.Contains(Tipo(tipo ?? ""));

    private static string TabelaAtividadeParlamentar(string tipo) => Tipo(tipo) switch
    {
        "ATAS_DE_REUNIOES" => "atas_reunioes",
        "PORTARIA" => "portarias",
        "REQUERIMENTO" => "requerimentos",
        "CONVOCACAO" => "convocacoes",
        "INDICACAO" => "indicacoes",
        "MOCAO" => "mocoes",
        "RESOLUCAO" => "resolucoes",
        "PROJETO_DE_RESOLUCOES" => "projetos_resolucoes",
        "DIPLOMA" => "diplomas",
        "DECRETO" => "decretos",
        _ => throw new InvalidOperationException("Tipo de atividade parlamentar inválido.")
    };

    private static bool TipoPodeSerListadoPublicamente(string? tipo) => new[]
    {
        "NOTICIA", "LICITACAO", "TRANSPARENCIA", "DOCUMENTO", "DIARIO", "PAGINA", "SUBMENU",
        "VEREADOR", "EVENTO",
        "ATAS_DE_REUNIOES", "PORTARIA", "REQUERIMENTO", "CONVOCACAO", "INDICACAO",
        "MOCAO", "RESOLUCAO", "PROJETO_DE_RESOLUCOES", "DIPLOMA", "DECRETO"
    }.Contains(Tipo(tipo ?? ""));

    // Restrito apenas para administração de usuários/configurações sensíveis.
    // Conteúdos/publicações podem ser criados por qualquer usuário autenticado,
    // mas cada usuário só altera ou exclui o que pertence a ele. Admin altera todos.
    private static bool TipoRestritoAdmin(string? tipo) => new[] { "USUARIO", "CONFIG" }.Contains(Tipo(tipo ?? ""));

    private static bool TipoExigeAdminParaPublicar(string? tipo) => TipoRestritoAdmin(tipo);
    private static void Validar(AdminRegistroDto dto) { if (string.IsNullOrWhiteSpace(dto.Tipo)) throw new InvalidOperationException("Tipo do módulo é obrigatório."); if (string.IsNullOrWhiteSpace(dto.Titulo)) throw new InvalidOperationException("Título do registro é obrigatório."); }
    private static string NormalizarStatus(string? status) => string.IsNullOrWhiteSpace(status) ? "Publicado" : status.Trim();
    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin() => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
}
