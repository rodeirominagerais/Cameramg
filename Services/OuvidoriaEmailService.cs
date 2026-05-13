using System.Net;
using Cameramg.Data;
using Cameramg.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Cameramg.Services;

public record OuvidoriaEmailAnexo(string NomeOriginal, string CaminhoFisico, string? MimeType);

public class OuvidoriaEmailService(AppDbContext db, IConfiguration config, ILogger<OuvidoriaEmailService> logger)
{
    public Task EnviarNovoChamadoAsync(OuvidoriaChamado chamado, CancellationToken ct = default)
        => EnviarNovoChamadoAsync(chamado, null, ct);

    public async Task EnviarNovoChamadoAsync(OuvidoriaChamado chamado, IEnumerable<OuvidoriaEmailAnexo>? anexos, CancellationToken ct = default)
    {
        var cfg = await CarregarConfiguracoesAsync(ct);
        var smtpHost = Pick(cfg, "email_smtp_host", config["Email:SmtpHost"] ?? "");
        var smtpPort = int.TryParse(Pick(cfg, "email_smtp_port", config["Email:SmtpPort"] ?? "465"), out var porta) ? porta : 465;
        var smtpUser = Pick(cfg, "email_user", config["Email:User"] ?? "");
        var smtpPass = Pick(cfg, "email_password", config["Email:Password"] ?? "");
        var display = Pick(cfg, "email_display_name", config["Email:DisplayName"] ?? "Câmara Municipal de Rodeiro");
        var emailOuvidoria = Pick(cfg, "email_ouvidoria", Pick(cfg, "email_contato", Pick(cfg, "email", config["Email:Ouvidoria"] ?? "ouvidoria@rodeiro.mg.leg.br")));

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
        {
            logger.LogWarning("SMTP da Ouvidoria não configurado. Chamado {Protocolo} salvo sem envio de e-mail.", chamado.Protocolo);
            return;
        }

        using var smtp = new SmtpClient();
        try
        {
            var opt = smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await smtp.ConnectAsync(smtpHost, smtpPort, opt, ct);
            await smtp.AuthenticateAsync(smtpUser, smtpPass, ct);

            if (!string.IsNullOrWhiteSpace(emailOuvidoria))
                await smtp.SendAsync(MontarEmailOuvidoria(display, smtpUser, emailOuvidoria, chamado, anexos), ct);

            if (!string.IsNullOrWhiteSpace(chamado.Email))
                await smtp.SendAsync(MontarEmailCidadao(display, smtpUser, chamado), ct);

            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar e-mails da Ouvidoria para o chamado {Protocolo}.", chamado.Protocolo);
            try { if (smtp.IsConnected) await smtp.DisconnectAsync(true, ct); } catch { }
        }
    }


    public async Task EnviarRespostaAsync(OuvidoriaChamado chamado, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chamado.Email)) return;

        var cfg = await CarregarConfiguracoesAsync(ct);
        var smtpHost = Pick(cfg, "email_smtp_host", config["Email:SmtpHost"] ?? "");
        var smtpPort = int.TryParse(Pick(cfg, "email_smtp_port", config["Email:SmtpPort"] ?? "465"), out var porta) ? porta : 465;
        var smtpUser = Pick(cfg, "email_user", config["Email:User"] ?? "");
        var smtpPass = Pick(cfg, "email_password", config["Email:Password"] ?? "");
        var display = Pick(cfg, "email_display_name", config["Email:DisplayName"] ?? "Câmara Municipal de Rodeiro");

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
        {
            logger.LogWarning("SMTP da Ouvidoria não configurado. Resposta do chamado {Protocolo} salva sem envio de e-mail.", chamado.Protocolo);
            return;
        }

        using var smtp = new SmtpClient();
        try
        {
            var opt = smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await smtp.ConnectAsync(smtpHost, smtpPort, opt, ct);
            await smtp.AuthenticateAsync(smtpUser, smtpPass, ct);
            await smtp.SendAsync(MontarEmailRespostaCidadao(display, smtpUser, chamado), ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar resposta da Ouvidoria para o chamado {Protocolo}.", chamado.Protocolo);
            try { if (smtp.IsConnected) await smtp.DisconnectAsync(true, ct); } catch { }
        }
    }

    private async Task<Dictionary<string, string>> CarregarConfiguracoesAsync(CancellationToken ct)
    {
        var lista = await db.ConfiguracoesSite.AsNoTracking()
            .OrderBy(x => x.UsuarioId == null ? 0 : 1)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        return lista
            .Where(x => !string.IsNullOrWhiteSpace(x.Chave))
            .GroupBy(x => x.Chave.Trim())
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Valor))?.Valor ?? "");
    }

    private static string Pick(Dictionary<string, string> cfg, string key, string fallback = "")
        => cfg.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static MimeMessage MontarEmailOuvidoria(string display, string remetente, string destino, OuvidoriaChamado chamado, IEnumerable<OuvidoriaEmailAnexo>? anexos)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(display, remetente));
        msg.To.Add(MailboxAddress.Parse(destino));
        if (!string.IsNullOrWhiteSpace(chamado.Email)) msg.ReplyTo.Add(MailboxAddress.Parse(chamado.Email));
        msg.Subject = $"Novo chamado de Ouvidoria - {chamado.Protocolo}";
        msg.Body = new BodyBuilder
        {
            HtmlBody = $@"<h2>Novo chamado recebido pela Ouvidoria</h2>
<p><strong>Protocolo:</strong> {H(chamado.Protocolo)}</p>
<p><strong>Status:</strong> {H(chamado.Status)}</p>
<p><strong>Nome:</strong> {H(chamado.Nome)}</p>
<p><strong>E-mail:</strong> {H(chamado.Email)}</p>
<p><strong>Telefone:</strong> {H(chamado.Telefone)}</p>
<p><strong>Assunto:</strong> {H(chamado.Assunto)}</p>
<p><strong>Mensagem:</strong></p>
<div style=""white-space:pre-wrap;border:1px solid #ddd;padding:12px;border-radius:8px"">{H(chamado.Mensagem)}</div>
<p><small>Mensagem gerada automaticamente pelo Portal da Câmara Municipal de Rodeiro.</small></p>",
            TextBody = $"Novo chamado recebido pela Ouvidoria\n\nProtocolo: {chamado.Protocolo}\nStatus: {chamado.Status}\nNome: {chamado.Nome}\nE-mail: {chamado.Email}\nTelefone: {chamado.Telefone}\nAssunto: {chamado.Assunto}\n\nMensagem:\n{chamado.Mensagem}"
        }.ToMessageBody();
        return msg;
    }

    private static MimeMessage MontarEmailCidadao(string display, string remetente, OuvidoriaChamado chamado)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(display, remetente));
        msg.To.Add(MailboxAddress.Parse(chamado.Email!));
        msg.Subject = $"Protocolo da Ouvidoria - {chamado.Protocolo}";
        msg.Body = new BodyBuilder
        {
            HtmlBody = $@"<p>Olá, {H(chamado.Nome)}.</p>
<p>Sua solicitação foi recebida pela Ouvidoria da Câmara Municipal de Rodeiro.</p>
<p><strong>Protocolo:</strong> {H(chamado.Protocolo)}</p>
<p><strong>Status inicial:</strong> {H(chamado.Status)}</p>
<p>Guarde este número. Para consultar a situação, acesse a página de Ouvidoria e informe o protocolo junto com este e-mail.</p>
<p><strong>Assunto:</strong> {H(chamado.Assunto)}</p>",
            TextBody = $"Olá, {chamado.Nome}.\n\nSua solicitação foi recebida pela Ouvidoria da Câmara Municipal de Rodeiro.\n\nProtocolo: {chamado.Protocolo}\nStatus inicial: {chamado.Status}\n\nGuarde este número. Para consultar a situação, acesse a página de Ouvidoria e informe o protocolo junto com este e-mail.\n\nAssunto: {chamado.Assunto}"
        }.ToMessageBody();
        return msg;
    }


    private static MimeMessage MontarEmailRespostaCidadao(string display, string remetente, OuvidoriaChamado chamado)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(display, remetente));
        msg.To.Add(MailboxAddress.Parse(chamado.Email!));
        msg.Subject = $"Resposta da Ouvidoria - {chamado.Protocolo}";
        msg.Body = new BodyBuilder
        {
            HtmlBody = $@"<p>Olá, {H(chamado.Nome)}.</p>
<p>Sua solicitação foi atualizada pela Ouvidoria da Câmara Municipal de Rodeiro.</p>
<p><strong>Protocolo:</strong> {H(chamado.Protocolo)}</p>
<p><strong>Status:</strong> {H(chamado.Status)}</p>
<p><strong>Assunto:</strong> {H(chamado.Assunto)}</p>
<p><strong>Resposta:</strong></p>
<div style=""white-space:pre-wrap;border:1px solid #ddd;padding:12px;border-radius:8px"">{H(chamado.Resposta)}</div>",
            TextBody = $"Olá, {chamado.Nome}.\n\nSua solicitação foi atualizada pela Ouvidoria da Câmara Municipal de Rodeiro.\n\nProtocolo: {chamado.Protocolo}\nStatus: {chamado.Status}\nAssunto: {chamado.Assunto}\n\nResposta:\n{chamado.Resposta}"
        }.ToMessageBody();
        return msg;
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
}
