using Cameramg.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Cameramg.Services;

public class PasswordEmailService(AppDbContext db, IConfiguration config)
{
    public async Task EnviarLinkRedefinicaoAsync(string destino, string nome, string link, CancellationToken ct = default)
    {
        var vals = await db.ConfiguracoesSite.AsNoTracking().ToDictionaryAsync(x => x.Chave, x => x.Valor ?? "", ct);
        string Pick(string key, string fallback = "") => vals.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        var smtpHost = Pick("email_smtp_host", config["Email:SmtpHost"] ?? "");
        var smtpPort = int.TryParse(Pick("email_smtp_port", config["Email:SmtpPort"] ?? "465"), out var p) ? p : 465;
        var smtpUser = Pick("email_user", config["Email:User"] ?? "");
        var smtpPass = Pick("email_password", config["Email:Password"] ?? "");
        var display = Pick("email_display_name", config["Email:DisplayName"] ?? "Câmara Municipal");

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
            return;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(display, smtpUser));
        msg.To.Add(MailboxAddress.Parse(destino));
        msg.Subject = "Redefinição de senha";
        msg.Body = new BodyBuilder
        {
            HtmlBody = $"<p>Olá, {System.Net.WebUtility.HtmlEncode(nome)}.</p><p>Clique no link abaixo para redefinir sua senha:</p><p><a href=\"{link}\">Redefinir senha</a></p><p>Este link expira em 1 hora.</p>",
            TextBody = $"Olá, {nome}. Acesse este link para redefinir sua senha: {link}. Este link expira em 1 hora."
        }.ToMessageBody();

        using var smtp = new SmtpClient();
        var opt = smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await smtp.ConnectAsync(smtpHost, smtpPort, opt, ct);
        await smtp.AuthenticateAsync(smtpUser, smtpPass, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
