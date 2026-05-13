using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Concurrent;
using System.Security.Claims;
using Cameramg.Data;
using Cameramg.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using MimeKit.Utils;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/email")]
[Authorize(Policy = "Editor")]
public class EmailController(IConfiguration config, AppDbContext db) : ControllerBase
{
    private static readonly ConcurrentDictionary<long, DateTimeOffset> UltimaSincronizacaoPorUsuario = new();

    private async Task<EmailSettings> SettingsAsync(CancellationToken ct = default)
    {
        var uid = Uid();

        // Primeiro procura a configuração isolada do usuário logado.
        var vals = await db.ConfiguracoesSite.AsNoTracking()
            .Where(x => x.Chave.StartsWith("email_") && x.UsuarioId == uid)
            .ToDictionaryAsync(x => x.Chave, x => x.Valor ?? "", ct);

        // E-MAIL 100% ISOLADO POR USUÁRIO:
        // não existe fallback para configuração global, appsettings ou variáveis de ambiente.
        // Se o usuário ainda não configurou a própria conta, o painel retorna "não configurado".
        return new EmailSettings(config, vals);
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        return Ok(new
        {
            configurado = s.Configurado,
            conta = EmailValido(s.User) ? s.User : "Conta inválida",
            displayName = s.DisplayName,
            imapHost = s.ImapHost,
            imapPort = s.ImapPort,
            smtpHost = s.SmtpHost,
            smtpPort = s.SmtpPort,
            seguro = true,
            mensagem = s.Configurado
                ? "Conta de e-mail exclusiva do usuário logado."
                : "Este usuário ainda não configurou sua própria conta de e-mail."
        });
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        return Ok(new
        {
            emailUser = s.User,
            emailPassword = string.IsNullOrWhiteSpace(s.Password) ? "" : "********",
            emailDisplayName = s.DisplayName,
            emailImapHost = s.ImapHost,
            emailImapPort = s.ImapPort,
            emailSmtpHost = s.SmtpHost,
            emailSmtpPort = s.SmtpPort
        });
    }

    [HttpPost("config")]
    public async Task<IActionResult> SaveConfig([FromBody] EmailConfigRequest req, CancellationToken ct = default)
    {
        var email = NormalizarEmail(req.EmailUser);
        if (!EmailValido(email))
            return BadRequest(new { erro = "Informe o e-mail completo da conta. Exemplo: rodeiro@rodeiromg.com.br" });

        await Salvar("email_user", email, "Conta de e-mail institucional", ct);
        if (!string.IsNullOrWhiteSpace(req.EmailPassword) && req.EmailPassword != "********")
            await Salvar("email_password", req.EmailPassword, "Senha da conta de e-mail institucional", ct);
        await Salvar("email_display_name", req.EmailDisplayName, "Nome exibido no remetente", ct);

        var senhaParaTeste = !string.IsNullOrWhiteSpace(req.EmailPassword) && req.EmailPassword != "********"
            ? req.EmailPassword
            : (await SettingsAsync(ct)).Password;

        var imapHost = (req.EmailImapHost ?? "").Trim();
        var smtpHost = (req.EmailSmtpHost ?? "").Trim();
        var imapPort = req.EmailImapPort <= 0 ? 993 : req.EmailImapPort;
        var smtpPort = req.EmailSmtpPort <= 0 ? 465 : req.EmailSmtpPort;

        if (string.IsNullOrWhiteSpace(imapHost) || string.IsNullOrWhiteSpace(smtpHost))
        {
            var detectado = await DetectarServidoresEmailAsync(email, senhaParaTeste, ct);
            if (string.IsNullOrWhiteSpace(imapHost)) { imapHost = detectado.ImapHost; imapPort = detectado.ImapPort; }
            if (string.IsNullOrWhiteSpace(smtpHost)) { smtpHost = detectado.SmtpHost; smtpPort = detectado.SmtpPort; }
        }

        await Salvar("email_imap_host", imapHost, "Servidor IMAP", ct);
        await Salvar("email_imap_port", imapPort.ToString(), "Porta IMAP", ct);
        await Salvar("email_smtp_host", smtpHost, "Servidor SMTP", ct);
        await Salvar("email_smtp_port", smtpPort.ToString(), "Porta SMTP", ct);
        await db.SaveChangesAsync(ct);
        return Ok(new { salvo = true, mensagem = "Configurações de e-mail salvas com sucesso.", imapHost, imapPort, smtpHost, smtpPort });
    }


    private async Task<(string ImapHost, int ImapPort, string SmtpHost, int SmtpPort)> DetectarServidoresEmailAsync(string email, string senha, CancellationToken ct)
    {
        var dominio = email.Split('@').LastOrDefault()?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(dominio))
            throw new InvalidOperationException("Não foi possível identificar o domínio do e-mail.");

        var hostsImap = new[] { $"imap.{dominio}", $"mail.{dominio}", dominio };
        var hostsSmtp = new[] { $"smtp.{dominio}", $"mail.{dominio}", dominio };
        string? imapOk = null;
        string? smtpOk = null;

        foreach (var host in hostsImap.Distinct())
        {
            try
            {
                using var imap = new ImapClient();
                imap.Timeout = 8000;
                await imap.ConnectAsync(host, 993, SecureSocketOptions.SslOnConnect, ct);
                if (!string.IsNullOrWhiteSpace(senha)) await imap.AuthenticateAsync(email, senha, ct);
                await imap.DisconnectAsync(true, ct);
                imapOk = host;
                break;
            }
            catch { }
        }

        foreach (var host in hostsSmtp.Distinct())
        {
            try
            {
                using var smtp = new SmtpClient();
                smtp.Timeout = 8000;
                await smtp.ConnectAsync(host, 465, SecureSocketOptions.SslOnConnect, ct);
                if (!string.IsNullOrWhiteSpace(senha)) await smtp.AuthenticateAsync(email, senha, ct);
                await smtp.DisconnectAsync(true, ct);
                smtpOk = host;
                break;
            }
            catch { }
        }

        imapOk ??= $"mail.{dominio}";
        smtpOk ??= $"mail.{dominio}";
        return (imapOk, 993, smtpOk, 465);
    }

    [HttpGet("folder/{folder}")]
    public Task<IActionResult> Folder(string folder, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? busca = null, CancellationToken ct = default)
        => ListarPasta(folder, page, pageSize, busca, ct);

    [HttpGet("inbox")]
    public Task<IActionResult> Inbox([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? busca = null, CancellationToken ct = default)
        => ListarPasta("inbox", page, pageSize, busca, ct);

    private async Task<IActionResult> ListarPasta(string folder, int page, int pageSize, string? busca, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "E-mail ainda não configurado corretamente. Confira e-mail completo e senha em Configurar conta." });

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        try
        {
            using var client = new ImapClient();
            client.Timeout = 30000;
            await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await client.AuthenticateAsync(s.User, s.Password, ct);

            if ((folder ?? "").Equals("favorites", StringComparison.OrdinalIgnoreCase))
            {
                var itensFav = await ListarFavoritosAsync(client, busca, page, pageSize, ct);
                await client.DisconnectAsync(true, ct);
                return Ok(itensFav);
            }

            var mailFolder = await AbrirPasta(client, folder, FolderAccess.ReadOnly, ct);

            IList<UniqueId> ids;
            if (!string.IsNullOrWhiteSpace(busca))
                ids = await mailFolder.SearchAsync(SearchQuery.SubjectContains(busca).Or(SearchQuery.BodyContains(busca)).Or(SearchQuery.FromContains(busca)), ct);
            else
                ids = await mailFolder.SearchAsync(SearchQuery.All, ct);

            var total = ids.Count;
            var selecionados = ids.Reverse().Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var itens = new List<object>();
            var summaries = selecionados.Count > 0
                ? await mailFolder.FetchAsync(selecionados, MessageSummaryItems.Flags, ct)
                : new List<IMessageSummary>();
            var flagsPorId = summaries.ToDictionary(x => x.UniqueId.Id, x => x.Flags ?? MessageFlags.None);

            foreach (var id in selecionados)
            {
                var msg = await mailFolder.GetMessageAsync(id, ct);
                var flags = flagsPorId.TryGetValue(id.Id, out var f) ? f : MessageFlags.None;
                itens.Add(new
                {
                    id = id.Id.ToString(),
                    pasta = folder,
                    assunto = msg.Subject ?? "(sem assunto)",
                    remetente = msg.From.ToString(),
                    destinatarios = msg.To.ToString(),
                    data = msg.Date.LocalDateTime,
                    resumo = Limpar(msg.TextBody ?? msg.HtmlBody ?? ""),
                    favorito = flags.HasFlag(MessageFlags.Flagged),
                    lido = flags.HasFlag(MessageFlags.Seen),
                    anexos = msg.Attachments.Select(a => a.ContentDisposition?.FileName ?? a.ContentType.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                });
            }

            await client.DisconnectAsync(true, ct);
            return Ok(new { total, itens });
        }
        catch (AuthenticationException)
        {
            return BadRequest(new { erro = "O servidor recusou o login. Use o e-mail completo e a senha correta da caixa de e-mail." });
        }
        catch (OperationCanceledException ex)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                erro = "A consulta ao servidor de e-mail foi cancelada ou excedeu o tempo limite. Tente recarregar a pasta novamente.",
                detalhe = ex.Message
            });
        }
        catch (Exception ex) when (ex is ImapCommandException || ex is ServiceNotConnectedException || ex is System.Net.Sockets.SocketException || ex is IOException)
        {
            return BadRequest(new { erro = "Não foi possível acessar o servidor de e-mail. Confira IMAP, porta 993 e SSL.", detalhe = ex.Message });
        }
    }

    [HttpGet("message/{id}")]
    public Task<IActionResult> Message(string id, [FromQuery] string folder = "inbox", CancellationToken ct = default)
        => MessageFromFolder(folder, id, ct);

    [HttpGet("folder/{folder}/message/{id}")]
    public Task<IActionResult> MessageFromFolderRoute(string folder, string id, CancellationToken ct = default)
        => MessageFromFolder(folder, id, ct);

    private async Task<IActionResult> MessageFromFolder(string folder, string id, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "E-mail ainda não configurado no painel administrativo." });
        if (!uint.TryParse(id, out var uid)) return BadRequest(new { erro = "ID de e-mail inválido." });

        try
        {
            using var client = new ImapClient();
            client.Timeout = 30000;
            await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await client.AuthenticateAsync(s.User, s.Password, ct);
            var mailFolder = await AbrirPasta(client, folder, FolderAccess.ReadOnly, ct);
            var msg = await mailFolder.GetMessageAsync(new UniqueId(uid), ct);
            await client.DisconnectAsync(true, ct);

            return Ok(new
            {
                id,
                pasta = folder,
                assunto = msg.Subject ?? "(sem assunto)",
                remetente = msg.From.ToString(),
                destinatarios = msg.To.ToString(),
                data = msg.Date.LocalDateTime,
                texto = msg.TextBody,
                html = msg.HtmlBody,
                anexos = msg.Attachments.Select(a => a.ContentDisposition?.FileName ?? a.ContentType.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
            });
        }
        catch (AuthenticationException)
        {
            return BadRequest(new { erro = "O servidor recusou o login. Use o e-mail completo e a senha correta da caixa de e-mail." });
        }
        catch (OperationCanceledException ex)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                erro = "A consulta ao servidor de e-mail foi cancelada ou excedeu o tempo limite. Tente abrir a mensagem novamente.",
                detalhe = ex.Message
            });
        }
        catch (Exception ex) when (ex is ImapCommandException || ex is ServiceNotConnectedException || ex is System.Net.Sockets.SocketException || ex is IOException)
        {
            return BadRequest(new { erro = "Não foi possível acessar o servidor de e-mail. Confira IMAP, porta 993 e SSL.", detalhe = ex.Message });
        }
    }



    [HttpPost("folder/{folder}/message/{id}/archive")]
    public Task<IActionResult> ArchiveMessage(string folder, string id, CancellationToken ct = default)
        => MoverMensagem(folder, id, "archive", "E-mail arquivado com sucesso.", ct);

    [HttpPost("folder/{folder}/message/{id}/trash")]
    public Task<IActionResult> TrashMessage(string folder, string id, CancellationToken ct = default)
        => MoverMensagem(folder, id, "trash", "E-mail movido para a lixeira.", ct);

    [HttpPost("folder/{folder}/message/{id}/spam")]
    public Task<IActionResult> SpamMessage(string folder, string id, CancellationToken ct = default)
        => MoverMensagem(folder, id, "spam", "E-mail movido para spam.", ct);

    [HttpPost("folder/{folder}/message/{id}/restore")]
    public Task<IActionResult> RestoreMessage(string folder, string id, CancellationToken ct = default)
        => MoverMensagem(folder, id, "inbox", "E-mail restaurado para a caixa de entrada.", ct);

    [HttpDelete("folder/{folder}/message/{id}")]
    public async Task<IActionResult> DeleteMessage(string folder, string id, CancellationToken ct = default)
    {
        if (!uint.TryParse(id, out var uid)) return BadRequest(new { erro = "ID de e-mail inválido." });
        var s = await SettingsAsync(ct);
        using var client = new ImapClient();
        client.Timeout = 30000;
        await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(s.User, s.Password, ct);
        var origem = await AbrirPasta(client, folder, FolderAccess.ReadWrite, ct);
        await origem.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, ct);
        await origem.ExpungeAsync(ct);
        await client.DisconnectAsync(true, ct);
        return Ok(new { removido = true, mensagem = "E-mail excluído definitivamente." });
    }

    [HttpPost("folder/{folder}/message/{id}/favorite")]
    public Task<IActionResult> FavoriteMessage(string folder, string id, CancellationToken ct = default)
        => AlterarFlag(folder, id, MessageFlags.Flagged, true, "E-mail marcado como favorito.", ct);

    [HttpPost("folder/{folder}/message/{id}/unfavorite")]
    public Task<IActionResult> UnfavoriteMessage(string folder, string id, CancellationToken ct = default)
        => AlterarFlag(folder, id, MessageFlags.Flagged, false, "E-mail removido dos favoritos.", ct);

    [HttpPost("folder/{folder}/message/{id}/read")]
    public Task<IActionResult> ReadMessage(string folder, string id, CancellationToken ct = default)
        => AlterarFlag(folder, id, MessageFlags.Seen, true, "E-mail marcado como lido.", ct);

    [HttpPost("folder/{folder}/message/{id}/unread")]
    public Task<IActionResult> UnreadMessage(string folder, string id, CancellationToken ct = default)
        => AlterarFlag(folder, id, MessageFlags.Seen, false, "E-mail marcado como não lido.", ct);


    [HttpPost("folder/{folder}/bulk/{acao}")]
    public async Task<IActionResult> BulkAction(string folder, string acao, [FromBody] BulkEmailRequest req, CancellationToken ct = default)
    {
        var ids = (req?.Ids ?? new List<string>())
            .Select(x => uint.TryParse(x, out var uid) ? (uint?)uid : null)
            .Where(x => x.HasValue)
            .Select(x => new UniqueId(x!.Value))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return BadRequest(new { erro = "Selecione pelo menos um e-mail." });

        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "Este usuário ainda não configurou sua própria conta de e-mail." });

        using var client = new ImapClient();
        await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(s.User, s.Password, ct);

        var origem = await AbrirPasta(client, folder, FolderAccess.ReadWrite, ct);
        var destino = (acao ?? "").Trim().ToLowerInvariant();
        var processados = 0;

        async Task MoverTodosAsync(string destinoPasta)
        {
            var pastaDestino = ObterPastaSemAbrir(client, destinoPasta);
            try
            {
                await origem.MoveToAsync(ids, pastaDestino, ct);
            }
            catch (Exception ex) when (ex is ImapCommandException || ex is FolderNotOpenException || ex is NotSupportedException)
            {
                if (!origem.IsOpen || origem.Access != FolderAccess.ReadWrite)
                    origem = await AbrirPasta(client, folder, FolderAccess.ReadWrite, ct);

                await origem.CopyToAsync(ids, pastaDestino, ct);
                await origem.AddFlagsAsync(ids, MessageFlags.Deleted, true, ct);
                await origem.ExpungeAsync(ct);
            }
        }

        switch (destino)
        {
            case "archive":
                await MoverTodosAsync("archive");
                break;
            case "trash":
                await MoverTodosAsync("trash");
                break;
            case "spam":
                await MoverTodosAsync("spam");
                break;
            case "restore":
                await MoverTodosAsync("inbox");
                break;
            case "delete":
                await origem.AddFlagsAsync(ids, MessageFlags.Deleted, true, ct);
                await origem.ExpungeAsync(ct);
                break;
            case "favorite":
                await origem.AddFlagsAsync(ids, MessageFlags.Flagged, true, ct);
                break;
            case "unfavorite":
                await origem.RemoveFlagsAsync(ids, MessageFlags.Flagged, true, ct);
                break;
            case "read":
                await origem.AddFlagsAsync(ids, MessageFlags.Seen, true, ct);
                break;
            case "unread":
                await origem.RemoveFlagsAsync(ids, MessageFlags.Seen, true, ct);
                break;
            default:
                return BadRequest(new { erro = "Ação em lote inválida." });
        }

        processados = ids.Count;
        await client.DisconnectAsync(true, ct);
        return Ok(new { atualizado = true, processados, mensagem = $"Ação aplicada em {processados} e-mail(s)." });
    }

    [HttpPost("draft")]
    public async Task<IActionResult> SaveDraft([FromBody] EnviarEmailRequest req, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "E-mail ainda não configurado no painel administrativo." });
        var message = CriarMensagem(s, req);
        using var imap = new ImapClient();
        await imap.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await imap.AuthenticateAsync(s.User, s.Password, ct);
        var drafts = await AbrirPasta(imap, "drafts", FolderAccess.ReadWrite, ct);
        await drafts.AppendAsync(message, MessageFlags.Draft, ct);
        await imap.DisconnectAsync(true, ct);
        return Ok(new { salvo = true, mensagem = "Rascunho salvo com sucesso." });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "Este usuário ainda não configurou sua própria conta de e-mail." });

        var uidLogado = Uid() ?? 0;
        var agora = DateTimeOffset.UtcNow;
        if (uidLogado > 0 && UltimaSincronizacaoPorUsuario.TryGetValue(uidLogado, out var ultima) && (agora - ultima).TotalSeconds < 4)
        {
            return Ok(new { sincronizado = true, ignorado = true, mensagem = "Sincronização recente reaproveitada." });
        }
        if (uidLogado > 0) UltimaSincronizacaoPorUsuario[uidLogado] = agora;

        using var client = new ImapClient();
        client.Timeout = 30000;
        await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(s.User, s.Password, ct);

        var pastas = new[]
        {
            new { chave = "inbox", nome = "Caixa de entrada" },
            new { chave = "sent", nome = "Enviado" },
            new { chave = "drafts", nome = "Rascunhos" },
            new { chave = "spam", nome = "Spam" },
            new { chave = "archive", nome = "Arquivar" },
            new { chave = "trash", nome = "Lixeira" },
            new { chave = "favorites", nome = "Favoritos" }
        };

        var resultado = new List<object>();
        foreach (var pasta in pastas)
        {
            try
            {
                var folder = await AbrirPasta(client, pasta.chave, FolderAccess.ReadOnly, ct);
                await folder.CheckAsync(ct);
                resultado.Add(new
                {
                    pasta = pasta.chave,
                    nome = pasta.nome,
                    total = folder.Count,
                    naoLidos = folder.Unread,
                    sincronizado = true
                });
                await folder.CloseAsync(false, ct);
            }
            catch
            {
                resultado.Add(new { pasta = pasta.chave, nome = pasta.nome, total = 0, naoLidos = 0, sincronizado = false });
            }
        }

        await client.DisconnectAsync(true, ct);
        return Ok(new
        {
            sincronizado = true,
            mensagem = "Sincronização concluída. Os e-mails restaurados já podem aparecer no painel.",
            pastas = resultado
        });
    }


    [HttpPost("restore-backup")]
    [DisableRequestSizeLimit]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
    public async Task<IActionResult> RestoreBackup([FromForm] IFormFile backup, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado)
            return BadRequest(new { erro = "Configure primeiro a conta de e-mail institucional antes de restaurar backup." });

        if (backup is null || backup.Length == 0)
            return BadRequest(new { erro = "Selecione um arquivo de backup .tar, .tar.gz ou .tgz exportado do servidor de e-mail/cPanel." });

        var nomeArquivo = backup.FileName ?? "backup";
        var totalLidos = 0;
        var totalRestaurados = 0;
        var totalIgnorados = 0;
        var totalErros = 0;
        var errosAmostra = new List<object>();
        var porPasta = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pastasCache = new Dictionary<string, IMailFolder>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(s.User, s.Password, ct);

            await using var origem = backup.OpenReadStream();
            await using var arquivo = PrepararStreamTar(origem, nomeArquivo);
            using var reader = new TarReader(arquivo, leaveOpen: false);

            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.EntryType != TarEntryType.RegularFile || entry.DataStream is null)
                    continue;

                var caminho = (entry.Name ?? string.Empty).Replace('\\', '/');
                if (!EhArquivoEmailMaildir(caminho))
                    continue;

                totalLidos++;

                try
                {
                    var pastaDestino = DetectarPastaBackup(caminho);
                    var flags = DetectarFlagsMaildir(caminho);
                    var destino = await ObterOuCriarPastaRestauracaoAsync(imap, pastaDestino, pastasCache, ct);

                    // O Maildir guarda cada e-mail como um arquivo MIME completo.
                    // O MimeMessage.LoadAsync lê o arquivo atual do TAR sem extrair tudo para disco.
                    var msg = await MimeMessage.LoadAsync(entry.DataStream, ct);

                    if (msg.Date == default)
                        msg.Date = DateTimeOffset.Now;

                    await destino.AppendAsync(msg, flags, ct);

                    totalRestaurados++;
                    porPasta[pastaDestino] = porPasta.TryGetValue(pastaDestino, out var qtd) ? qtd + 1 : 1;
                }
                catch (Exception ex)
                {
                    totalErros++;
                    if (errosAmostra.Count < 10)
                    {
                        errosAmostra.Add(new
                        {
                            arquivo = caminho,
                            erro = ex.Message
                        });
                    }
                }
            }

            await imap.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException)
        {
            return BadRequest(new { erro = "IMAP recusou o login. Confira e-mail completo e senha da conta de e-mail." });
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is FormatException)
        {
            return BadRequest(new
            {
                erro = "Não foi possível ler o backup. Este endpoint agora aceita TAR puro mesmo com extensão .tar.gz, além de .tar.gz real.",
                arquivo = nomeArquivo,
                detalhe = ex.Message
            });
        }

        if (totalLidos == 0)
        {
            return BadRequest(new
            {
                erro = "Nenhum e-mail foi encontrado no backup.",
                detalhe = "O backup precisa conter estrutura Maildir com arquivos dentro de cur/ ou new/, exemplo: administrativo@dominio/cur/... ou administrativo@dominio/.Sent/cur/...",
                arquivo = nomeArquivo
            });
        }

        totalIgnorados = Math.Max(0, totalLidos - totalRestaurados - totalErros);
        return Ok(new
        {
            restaurado = totalRestaurados > 0,
            mensagem = $"Backup processado. {totalRestaurados} e-mail(s) restaurado(s).",
            arquivo = nomeArquivo,
            totalLidos,
            totalRestaurados,
            totalIgnorados,
            totalErros,
            pastas = porPasta.Select(x => new { pasta = x.Key, total = x.Value }).ToArray(),
            erros = errosAmostra
        });
    }

    [HttpPost("send")]
    [Consumes("application/json")]
    public async Task<IActionResult> Send([FromBody] EnviarEmailRequest req, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "E-mail ainda não configurado no painel administrativo." });
        if (string.IsNullOrWhiteSpace(req.Para)) return BadRequest(new { erro = "Informe o destinatário." });
        if (string.IsNullOrWhiteSpace(req.Assunto)) return BadRequest(new { erro = "Informe o assunto." });

        try
        {
            var message = CriarMensagem(s, req);

            using var client = new SmtpClient();
            var socket = s.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(s.SmtpHost, s.SmtpPort, socket, ct);
            await client.AuthenticateAsync(s.User, s.Password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            var salvoEmEnviados = await SalvarEmEnviadosAsync(s, message, ct);

            return Ok(new
            {
                enviado = true,
                salvoEmEnviados,
                mensagem = salvoEmEnviados
                    ? "E-mail enviado com sucesso e salvo na pasta Enviados."
                    : "E-mail enviado com sucesso, mas não foi possível salvar automaticamente na pasta Enviados. Confira a pasta Sent/Enviados no servidor de e-mail."
            });
        }
        catch (FormatException)
        {
            return BadRequest(new { erro = "Endereço de destinatário inválido. Informe somente e-mails completos, separados por vírgula." });
        }
        catch (SmtpCommandException ex)
        {
            return BadRequest(new { erro = "O servidor SMTP recusou o envio.", detalhe = ex.Message });
        }
        catch (AuthenticationException)
        {
            return BadRequest(new { erro = "SMTP recusou o login. Confira e-mail completo, senha e servidores da conta." });
        }
    }





    [HttpPost("send")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SendForm([FromForm] EnviarEmailForm req, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (!s.Configurado) return BadRequest(new { erro = "E-mail ainda não configurado no painel administrativo." });
        if (string.IsNullOrWhiteSpace(req.Para)) return BadRequest(new { erro = "Informe o destinatário." });
        if (string.IsNullOrWhiteSpace(req.Assunto)) return BadRequest(new { erro = "Informe o assunto." });

        try
        {
            var baseReq = new EnviarEmailRequest(req.Para, req.Assunto, req.Mensagem, req.Html, req.Cc, req.Bcc);
            var message = await CriarMensagemComAnexosAsync(s, baseReq, req.Anexos, ct);

            using var client = new SmtpClient();
            var socket = s.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(s.SmtpHost, s.SmtpPort, socket, ct);
            await client.AuthenticateAsync(s.User, s.Password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            var salvoEmEnviados = await SalvarEmEnviadosAsync(s, message, ct);
            return Ok(new { enviado = true, salvoEmEnviados, mensagem = salvoEmEnviados ? "E-mail enviado com sucesso e salvo na pasta Enviados." : "E-mail enviado com sucesso." });
        }
        catch (FormatException)
        {
            return BadRequest(new { erro = "Endereço de destinatário inválido. Informe somente e-mails completos, separados por vírgula." });
        }
        catch (AuthenticationException)
        {
            return BadRequest(new { erro = "SMTP recusou o login. Confira e-mail completo, senha e servidores da conta." });
        }
    }

    private static MimeMessage CriarMensagem(EmailSettings s, EnviarEmailRequest req)
    {
        var remetente = new MailboxAddress(s.DisplayName, s.User);
        var message = new MimeMessage();
        message.From.Add(remetente);
        message.Sender = remetente;
        message.ReplyTo.Add(remetente);
        if (!string.IsNullOrWhiteSpace(req.Para)) message.To.AddRange(ParseEnderecos(req.Para));
        if (!string.IsNullOrWhiteSpace(req.Cc)) message.Cc.AddRange(ParseEnderecos(req.Cc));
        if (!string.IsNullOrWhiteSpace(req.Bcc)) message.Bcc.AddRange(ParseEnderecos(req.Bcc));
        message.Subject = string.IsNullOrWhiteSpace(req.Assunto) ? "(sem assunto)" : req.Assunto.Trim();
        message.Date = DateTimeOffset.Now;
        message.MessageId = MimeUtils.GenerateMessageId(s.User.Split('@').Last());
        message.Headers.Add("X-Mailer", "Câmara Municipal de Rodeiro - Portal Administrativo");

        var texto = LimparHtmlParaTexto(req.Mensagem ?? req.Html ?? string.Empty);
        var mensagemHtml = System.Net.WebUtility.HtmlEncode(req.Mensagem ?? string.Empty).Replace("\n", "<br>");
        var html = !string.IsNullOrWhiteSpace(req.Html)
            ? req.Html
            : $"<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.55;color:#0b2540\">{mensagemHtml}</div>";

        message.Body = new BodyBuilder { TextBody = texto, HtmlBody = html }.ToMessageBody();
        return message;
    }


    private static async Task<MimeMessage> CriarMensagemComAnexosAsync(EmailSettings s, EnviarEmailRequest req, IEnumerable<IFormFile>? anexos, CancellationToken ct)
    {
        var remetente = new MailboxAddress(s.DisplayName, s.User);
        var message = new MimeMessage();
        message.From.Add(remetente);
        message.Sender = remetente;
        message.ReplyTo.Add(remetente);
        if (!string.IsNullOrWhiteSpace(req.Para)) message.To.AddRange(ParseEnderecos(req.Para));
        if (!string.IsNullOrWhiteSpace(req.Cc)) message.Cc.AddRange(ParseEnderecos(req.Cc));
        if (!string.IsNullOrWhiteSpace(req.Bcc)) message.Bcc.AddRange(ParseEnderecos(req.Bcc));
        message.Subject = string.IsNullOrWhiteSpace(req.Assunto) ? "(sem assunto)" : req.Assunto.Trim();
        message.Date = DateTimeOffset.Now;
        message.MessageId = MimeUtils.GenerateMessageId(s.User.Split('@').Last());
        message.Headers.Add("X-Mailer", "Câmara Municipal de Rodeiro - Portal Administrativo");

        var mensagemHtml = System.Net.WebUtility.HtmlEncode(req.Mensagem ?? string.Empty).Replace("\n", "<br>");
        var html = !string.IsNullOrWhiteSpace(req.Html) ? req.Html : $"<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.55;color:#0b2540\">{mensagemHtml}</div>";
        var builder = new BodyBuilder { TextBody = LimparHtmlParaTexto(req.Mensagem ?? req.Html ?? string.Empty), HtmlBody = html };
        foreach (var file in anexos ?? Enumerable.Empty<IFormFile>())
        {
            if (file.Length <= 0) continue;
            await using var input = file.OpenReadStream();
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms, ct);
            builder.Attachments.Add(file.FileName, ms.ToArray(), ContentType.Parse(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType));
        }
        message.Body = builder.ToMessageBody();
        return message;
    }

    private async Task<IActionResult> MoverMensagem(string folder, string id, string destino, string mensagem, CancellationToken ct)
    {
        if (!uint.TryParse(id, out var uid)) return BadRequest(new { erro = "ID de e-mail inválido." });
        var s = await SettingsAsync(ct);
        using var client = new ImapClient();
        await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(s.User, s.Password, ct);
        var origem = await AbrirPasta(client, folder, FolderAccess.ReadWrite, ct);

        // IMPORTANTE: no IMAP apenas uma pasta fica selecionada por conexão.
        // Se abrir a pasta destino em ReadWrite antes do MoveToAsync, o servidor fecha a pasta origem
        // e o MailKit lança: "The folder is not currently open in read-write mode".
        // Portanto, a origem deve ficar aberta em ReadWrite e o destino deve ser apenas resolvido, sem OpenAsync.
        var pastaDestino = ObterPastaSemAbrir(client, destino);

        try
        {
            await origem.MoveToAsync(new UniqueId(uid), pastaDestino, ct);
        }
        catch (Exception ex) when (ex is ImapCommandException || ex is FolderNotOpenException || ex is NotSupportedException)
        {
            // Fallback para servidores cPanel/HostGator que não aceitam MOVE.
            // Garante novamente que a origem está aberta em ReadWrite antes de copiar/deletar.
            if (!origem.IsOpen || origem.Access != FolderAccess.ReadWrite)
                origem = await AbrirPasta(client, folder, FolderAccess.ReadWrite, ct);

            await origem.CopyToAsync(new UniqueId(uid), pastaDestino, ct);
            await origem.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, ct);
            await origem.ExpungeAsync(ct);
        }

        await client.DisconnectAsync(true, ct);
        return Ok(new { movido = true, pasta = destino, mensagem });
    }

    private async Task<IActionResult> AlterarFlag(string folder, string id, MessageFlags flag, bool adicionar, string mensagem, CancellationToken ct)
    {
        if (!uint.TryParse(id, out var uid)) return BadRequest(new { erro = "ID de e-mail inválido." });
        var s = await SettingsAsync(ct);
        using var client = new ImapClient();
        await client.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(s.User, s.Password, ct);
        var pasta = await AbrirPasta(client, folder, FolderAccess.ReadWrite, ct);
        if (adicionar) await pasta.AddFlagsAsync(new UniqueId(uid), flag, true, ct);
        else await pasta.RemoveFlagsAsync(new UniqueId(uid), flag, true, ct);
        await client.DisconnectAsync(true, ct);
        return Ok(new { atualizado = true, mensagem });
    }

    private static async Task<object> ListarFavoritosAsync(ImapClient client, string? busca, int page, int pageSize, CancellationToken ct)
    {
        var nomes = new[] { "inbox", "sent", "drafts", "archive", "trash", "spam" };
        var todos = new List<object>();
        foreach (var nome in nomes)
        {
            try
            {
                var pasta = await AbrirPasta(client, nome, FolderAccess.ReadOnly, ct);
                var query = SearchQuery.Flagged;
                if (!string.IsNullOrWhiteSpace(busca))
                    query = query.And(SearchQuery.SubjectContains(busca).Or(SearchQuery.BodyContains(busca)).Or(SearchQuery.FromContains(busca)));
                var ids = await pasta.SearchAsync(query, ct);
                foreach (var id in ids.Reverse())
                {
                    var msg = await pasta.GetMessageAsync(id, ct);
                    todos.Add(new
                    {
                        id = id.Id.ToString(),
                        pasta = nome,
                        assunto = msg.Subject ?? "(sem assunto)",
                        remetente = msg.From.ToString(),
                        destinatarios = msg.To.ToString(),
                        data = msg.Date.LocalDateTime,
                        resumo = Limpar(msg.TextBody ?? msg.HtmlBody ?? ""),
                        favorito = true,
                        anexos = msg.Attachments.Select(a => a.ContentDisposition?.FileName ?? a.ContentType.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    });
                }
                await pasta.CloseAsync(false, ct);
            }
            catch { }
        }
        return new { total = todos.Count, itens = todos.Skip((page - 1) * pageSize).Take(pageSize).ToArray() };
    }

    private static Stream PrepararStreamTar(Stream origem, string nomeArquivo)
    {
        // O arquivo enviado como administrativo.tar.gz está, na prática, em TAR puro.
        // Por isso a validação é pelo cabeçalho real do arquivo, não pela extensão.
        var buffer = new BufferedStream(origem, 1024 * 64);

        var magic = new byte[2];
        var lidos = buffer.Read(magic, 0, magic.Length);

        if (buffer.CanSeek)
            buffer.Position = 0;
        else
            throw new InvalidDataException("Não foi possível reposicionar o stream do backup para leitura.");

        var gzip = lidos == 2 && magic[0] == 0x1F && magic[1] == 0x8B;
        return gzip ? new GZipStream(buffer, CompressionMode.Decompress) : buffer;
    }

    private static bool EhArquivoEmailMaildir(string caminho)
    {
        var c = caminho.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(c)) return false;

        var nome = c.Split('/').LastOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nome)) return false;
        if (nome.Equals("maildirfolder", StringComparison.OrdinalIgnoreCase)) return false;
        if (nome.Equals("maildirsize", StringComparison.OrdinalIgnoreCase)) return false;
        if (nome.Equals("subscriptions", StringComparison.OrdinalIgnoreCase)) return false;
        if (c.Contains("/tmp/", StringComparison.OrdinalIgnoreCase)) return false;

        // Maildir válido: conta/cur/arquivo, conta/new/arquivo,
        // conta/.Sent/cur/arquivo, conta/.Drafts/new/arquivo etc.
        return c.Contains("/cur/", StringComparison.OrdinalIgnoreCase)
            || c.Contains("/new/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectarPastaBackup(string caminho)
    {
        var c = caminho.Replace('\\', '/');
        var partes = c.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Maildir salva as pastas especiais como diretórios iniciados por ponto:
        // .Sent, .Drafts, .Junk, .Spam, .Trash etc.
        // A raiz da conta, quando vem direto em cur/new, é Caixa de Entrada.
        foreach (var parteOriginal in partes)
        {
            var parte = parteOriginal.Trim().TrimStart('.').ToLowerInvariant();
            parte = parte.Replace(" ", "").Replace("-", "").Replace("_", "");

            if (parte is "sent" or "sentmail" or "sentmessages" or "sentitems" or "enviados" or "itensenviados")
                return "sent";

            if (parte is "drafts" or "draft" or "rascunhos" or "rascunho")
                return "drafts";

            if (parte is "trash" or "deleted" or "deletedmessages" or "deleteditems" or "lixeira" or "excluidos" or "itensexcluidos")
                return "trash";

            if (parte is "junk" or "spam" or "bulk" or "lixoeletronico" or "lixoeletrônico")
                return "spam";

            if (parte is "archive" or "archives" or "arquivados" or "arquivo")
                return "archive";
        }

        return "inbox";
    }

    private static MessageFlags DetectarFlagsMaildir(string caminho)
    {
        var flags = MessageFlags.None;
        var idx = caminho.LastIndexOf(":2,", StringComparison.Ordinal);
        if (idx < 0) return flags;

        var letras = caminho[(idx + 3)..];
        if (letras.Contains('S')) flags |= MessageFlags.Seen;
        if (letras.Contains('R')) flags |= MessageFlags.Answered;
        if (letras.Contains('F')) flags |= MessageFlags.Flagged;
        if (letras.Contains('D')) flags |= MessageFlags.Deleted;
        if (letras.Contains('T')) flags |= MessageFlags.Deleted;
        return flags;
    }

    private static async Task<IMailFolder> ObterOuCriarPastaRestauracaoAsync(
        ImapClient client,
        string folder,
        Dictionary<string, IMailFolder> cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue(folder, out var existente))
            return existente;

        IMailFolder? f = null;

        // 1) Primeiro tenta pelas pastas especiais anunciadas pelo servidor IMAP.
        try
        {
            f = folder switch
            {
                "sent" => client.GetFolder(SpecialFolder.Sent),
                "drafts" => client.GetFolder(SpecialFolder.Drafts),
                "spam" => client.GetFolder(SpecialFolder.Junk),
                "trash" => client.GetFolder(SpecialFolder.Trash),
                "archive" => client.GetFolder(SpecialFolder.Archive),
                _ => client.Inbox
            };
        }
        catch
        {
            // Alguns servidores não anunciam SpecialFolder corretamente.
        }

        // 2) Se o servidor não informou a pasta especial, procura por nomes comuns,
        // incluindo padrões cPanel/HostGator/Titan: Sent, INBOX.Sent, Enviados etc.
        if (f is null)
        {
            var nomesPossiveis = folder switch
            {
                "sent" => new[] { "Sent", "Sent Mail", "Sent Messages", "Sent Items", "Enviados", "Itens Enviados", "INBOX.Sent", "INBOX.Enviados", "INBOX/Sent", "INBOX/Enviados" },
                "drafts" => new[] { "Drafts", "Draft", "Rascunhos", "Rascunho", "INBOX.Drafts", "INBOX.Rascunhos", "INBOX/Drafts", "INBOX/Rascunhos" },
                "spam" => new[] { "Spam", "Junk", "Bulk", "Lixo eletrônico", "Lixo Eletronico", "INBOX.Spam", "INBOX.Junk", "INBOX/Spam", "INBOX/Junk" },
                "trash" => new[] { "Trash", "Deleted", "Deleted Items", "Lixeira", "Excluidos", "Excluídos", "INBOX.Trash", "INBOX.Lixeira", "INBOX/Trash", "INBOX/Lixeira" },
                "archive" => new[] { "Archive", "Archives", "Arquivados", "Arquivo", "INBOX.Archive", "INBOX.Arquivos", "INBOX/Archive", "INBOX/Arquivados" },
                _ => new[] { "INBOX" }
            };

            f = await EncontrarPastaImapAsync(client, nomesPossiveis, ct);
        }

        // 3) Se a pasta ainda não existir, cria com nome padrão.
        if (f is null)
        {
            var nomeCriar = folder switch
            {
                "sent" => "Sent",
                "drafts" => "Drafts",
                "spam" => "Spam",
                "trash" => "Trash",
                "archive" => "Archive",
                _ => "INBOX"
            };

            f = nomeCriar.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await CriarPastaImapAsync(client, nomeCriar, ct);
        }

        cache[folder] = f;
        return f;
    }

    private static async Task<IMailFolder?> EncontrarPastaImapAsync(ImapClient client, IEnumerable<string> nomesPossiveis, CancellationToken ct)
    {
        var alvos = nomesPossiveis
            .Select(NormalizarNomePastaImap)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (alvos.Contains("inbox"))
            return client.Inbox;

        var pastas = new List<IMailFolder> { client.Inbox };

        try
        {
            var raiz = client.GetFolder(client.PersonalNamespaces[0]);
            await ColetarPastasImapAsync(raiz, pastas, ct);
        }
        catch
        {
            // Se não conseguir listar a árvore, usa apenas INBOX.
        }

        return pastas.FirstOrDefault(p =>
            alvos.Contains(NormalizarNomePastaImap(p.Name)) ||
            alvos.Contains(NormalizarNomePastaImap(p.FullName)));
    }

    private static async Task ColetarPastasImapAsync(IMailFolder pasta, List<IMailFolder> destino, CancellationToken ct)
    {
        IList<IMailFolder> subpastas;
        try
        {
            subpastas = await pasta.GetSubfoldersAsync(false, ct);
        }
        catch
        {
            return;
        }

        foreach (var subpasta in subpastas)
        {
            if (!destino.Any(x => string.Equals(x.FullName, subpasta.FullName, StringComparison.OrdinalIgnoreCase)))
                destino.Add(subpasta);

            await ColetarPastasImapAsync(subpasta, destino, ct);
        }
    }

    private static async Task<IMailFolder> CriarPastaImapAsync(ImapClient client, string nome, CancellationToken ct)
    {
        var raiz = client.GetFolder(client.PersonalNamespaces[0]);

        try
        {
            return await raiz.CreateAsync(nome, true, ct);
        }
        catch
        {
            // Alguns servidores cPanel criam subpastas abaixo da INBOX.
            return await client.Inbox.CreateAsync(nome, true, ct);
        }
    }

    private static string NormalizarNomePastaImap(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return string.Empty;

        var n = nome.Trim().ToLowerInvariant();
        n = n.Replace("\\", "/").Replace(".", "/");
        n = n.Replace("inbox/", "");
        n = n.Replace(" ", "").Replace("-", "").Replace("_", "");
        n = n.Replace("é", "e").Replace("ê", "e").Replace("í", "i").Replace("ó", "o").Replace("õ", "o").Replace("ú", "u").Replace("ç", "c");
        return n.Trim('/');
    }

    private static async Task<bool> SalvarEmEnviadosAsync(EmailSettings s, MimeMessage message, CancellationToken ct)
    {
        try
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(s.ImapHost, s.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(s.User, s.Password, ct);

            var sentFolder = imap.GetFolder(SpecialFolder.Sent);
            await sentFolder.OpenAsync(FolderAccess.ReadWrite, ct);
            await sentFolder.AppendAsync(message, MessageFlags.Seen, ct);
            await sentFolder.CloseAsync(true, ct);

            await imap.DisconnectAsync(true, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }


    private static InternetAddressList ParseEnderecos(string enderecos)
    {
        var lista = new InternetAddressList();
        var partes = (enderecos ?? "")
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var parte in partes)
        {
            var email = parte.Trim().Trim('"', '\'');
            if (!MailboxAddress.TryParse(email, out var mailbox) || string.IsNullOrWhiteSpace(mailbox.Address) || !mailbox.Address.Contains('@'))
                throw new FormatException($"E-mail inválido: {parte}");

            // Evita sair como: "email@dominio.com" <email@dominio.com>
            lista.Add(new MailboxAddress(string.Empty, mailbox.Address.Trim().ToLowerInvariant()));
        }
        return lista;
    }

    private static string LimparHtmlParaTexto(string txt)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(txt ?? "", "<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        clean = System.Text.RegularExpressions.Regex.Replace(clean, "<[^>]+>", " ");
        clean = System.Net.WebUtility.HtmlDecode(clean);
        clean = System.Text.RegularExpressions.Regex.Replace(clean, "[ \t]+", " ").Trim();
        return clean;
    }

    private static string NormalizarEmail(string? email)
    {
        return (email ?? "").Trim().Trim('\"', '\'').ToLowerInvariant();
    }

    private static bool EmailValido(string? email)
    {
        var v = NormalizarEmail(email);
        return v.Contains('@') && MailboxAddress.TryParse(v, out var mb) && !string.IsNullOrWhiteSpace(mb.Address) && mb.Address.Contains('@');
    }

    private async Task Salvar(string chave, string? valor, string descricao, CancellationToken ct)
    {
        var uid = Uid();
        var c = await db.ConfiguracoesSite.FirstOrDefaultAsync(x => x.Chave == chave && x.UsuarioId == uid, ct);
        if (c is null)
        {
            c = new ConfiguracaoSite { Chave = chave, UsuarioId = uid };
            db.ConfiguracoesSite.Add(c);
        }
        c.Valor = valor ?? "";
        c.Descricao = descricao;
    }

    private static IMailFolder ObterPastaSemAbrir(ImapClient client, string folder)
    {
        var nome = (folder ?? "inbox").Trim().ToLowerInvariant();

        try
        {
            return nome switch
            {
                "sent" or "enviados" => client.GetFolder(SpecialFolder.Sent),
                "drafts" or "rascunhos" => client.GetFolder(SpecialFolder.Drafts),
                "spam" or "junk" => client.GetFolder(SpecialFolder.Junk),
                "trash" or "lixeira" => client.GetFolder(SpecialFolder.Trash),
                "archive" or "arquivar" or "arquivados" => client.GetFolder(SpecialFolder.Archive),
                _ => client.Inbox
            };
        }
        catch
        {
            var raiz = client.GetFolder(client.PersonalNamespaces[0]);
            var fallback = nome switch
            {
                "sent" or "enviados" => "Sent",
                "drafts" or "rascunhos" => "Drafts",
                "spam" or "junk" => "Spam",
                "trash" or "lixeira" => "Trash",
                "archive" or "arquivar" or "arquivados" => "Archive",
                _ => "INBOX"
            };

            return fallback.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : raiz.GetSubfolder(fallback);
        }
    }

    private static async Task<IMailFolder> AbrirPasta(ImapClient client, string folder, FolderAccess access, CancellationToken ct)
    {
        var nome = (folder ?? "inbox").Trim().ToLowerInvariant();
        IMailFolder f = nome switch
        {
            "sent" or "enviados" => client.GetFolder(SpecialFolder.Sent),
            "drafts" or "rascunhos" => client.GetFolder(SpecialFolder.Drafts),
            "spam" or "junk" => client.GetFolder(SpecialFolder.Junk),
            "trash" or "lixeira" => client.GetFolder(SpecialFolder.Trash),
            "archive" or "arquivar" or "arquivados" => client.GetFolder(SpecialFolder.Archive),
            _ => client.Inbox
        };
        if (f.IsOpen)
        {
            if (access == FolderAccess.ReadWrite && f.Access != FolderAccess.ReadWrite)
            {
                await f.CloseAsync(false, ct);
                await f.OpenAsync(FolderAccess.ReadWrite, ct);
            }
        }
        else
        {
            await f.OpenAsync(access, ct);
        }

        return f;
    }

    private static string Limpar(string txt)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(txt ?? "", "<[^>]+>", " ");
        clean = System.Net.WebUtility.HtmlDecode(clean);
        clean = System.Text.RegularExpressions.Regex.Replace(clean, "\\s+", " ").Trim();
        return clean.Length > 240 ? clean[..240] + "..." : clean;
    }

    private long? Uid() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public record EnviarEmailRequest(string Para, string Assunto, string? Mensagem, string? Html, string? Cc, string? Bcc);
public sealed class BulkEmailRequest { public List<string> Ids { get; set; } = new(); }
public sealed class EnviarEmailForm
{
    public string Para { get; set; } = "";
    public string Assunto { get; set; } = "";
    public string? Mensagem { get; set; }
    public string? Html { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public List<IFormFile> Anexos { get; set; } = new();
}
public record EmailConfigRequest(string? EmailUser, string? EmailPassword, string? EmailDisplayName, string? EmailImapHost, int EmailImapPort, string? EmailSmtpHost, int EmailSmtpPort);

public sealed class EmailSettings
{
    public string User { get; }
    public string Password { get; }
    public string DisplayName { get; }
    public string ImapHost { get; }
    public int ImapPort { get; }
    public string SmtpHost { get; }
    public int SmtpPort { get; }
    public bool Configurado => IsValidEmail(User) && !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(ImapHost) && !string.IsNullOrWhiteSpace(SmtpHost);

    public EmailSettings(IConfiguration config, IReadOnlyDictionary<string, string>? dbVals = null)
    {
        var isolado = dbVals is not null;
        var dbUser = dbVals != null && dbVals.TryGetValue("email_user", out var du) ? du : "";
        var envUser = Environment.GetEnvironmentVariable("EMAIL_USER") ?? config["Email:User"] ?? "";

        // Quando vier do banco do usuário logado, não cai para conta global.
        User = isolado ? CleanEmail(dbUser) : CleanEmail(envUser);
        Password = Get(config, dbVals, "email_password", "EMAIL_PASSWORD", "Email:Password");
        DisplayName = Get(config, dbVals, "email_display_name", "EMAIL_DISPLAY_NAME", "Email:DisplayName", "Câmara Municipal de Rodeiro");
        var domain = User.Contains('@') ? User.Split('@').LastOrDefault() ?? "" : "";
        ImapHost = Get(config, dbVals, "email_imap_host", "EMAIL_IMAP_HOST", "Email:ImapHost", string.IsNullOrWhiteSpace(domain) ? "" : $"mail.{domain}");
        ImapPort = int.TryParse(Get(config, dbVals, "email_imap_port", "EMAIL_IMAP_PORT", "Email:ImapPort", "993"), out var ip) ? ip : 993;
        SmtpHost = Get(config, dbVals, "email_smtp_host", "EMAIL_SMTP_HOST", "Email:SmtpHost", string.IsNullOrWhiteSpace(domain) ? "" : $"mail.{domain}");
        SmtpPort = int.TryParse(Get(config, dbVals, "email_smtp_port", "EMAIL_SMTP_PORT", "Email:SmtpPort", "465"), out var sp) ? sp : 465;
    }

    private static string CleanEmail(string? value) => (value ?? "").Trim().Trim('\"', '\'').ToLowerInvariant();

    private static bool IsValidEmail(string? value)
    {
        var v = CleanEmail(value);
        return v.Contains('@') && MailboxAddress.TryParse(v, out var mb) && !string.IsNullOrWhiteSpace(mb.Address) && mb.Address.Contains('@');
    }

    private static string Get(IConfiguration c, IReadOnlyDictionary<string, string>? vals, string dbKey, string env, string key, string fallback = "")
    {
        if (vals != null)
            return vals.TryGetValue(dbKey, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        return Environment.GetEnvironmentVariable(env) ?? c[key] ?? fallback;
    }
}
