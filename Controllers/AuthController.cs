using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cameramg.Data;
using Cameramg.Dtos;
using Cameramg.Models;
using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens, PasswordEmailService passwordEmail, IConfiguration config) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var login = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Usuarios.FirstOrDefaultAsync(x =>
            x.Email.ToLower() == login || x.Nome.ToLower() == login);

        if (user is null || !SenhaConfere(req.Senha, user.SenhaHash))
            return Unauthorized(new { erro = "Login ou senha inválidos." });

        if (!user.Ativo)
            return Unauthorized(new { erro = "Usuário aguardando liberação do administrador." });

        // Se a senha estava salva em formato legado/texto puro, atualiza para BCrypt no primeiro login válido.
        if (!string.IsNullOrWhiteSpace(req.Senha) && user.SenhaHash == req.Senha)
        {
            user.SenhaHash = BCrypt.Net.BCrypt.HashPassword(req.Senha);
            user.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var tk = tokens.Gerar(user);
        return Ok(new LoginResponse(tk.token, user.Id, user.Nome, user.Email, user.Perfil, tk.expires));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        nome = User.Identity?.Name,
        email = User.FindFirstValue(ClaimTypes.Email),
        perfil = User.FindFirstValue(ClaimTypes.Role)
    });

    [HttpPost("primeiro-acesso")]
    [AllowAnonymous]
    public async Task<IActionResult> PrimeiroAcesso(PrimeiroAcessoRequest req)
    {
        var nome = (req.NomeCompleto ?? "").Trim();
        var cpfCnpj = SomenteDigitos(req.CpfCnpj);
        var email = (req.Email ?? "").Trim().ToLowerInvariant();

        if (nome.Length < 3) return BadRequest(new { erro = "Informe o nome completo." });
        if (cpfCnpj.Length is not (11 or 14)) return BadRequest(new { erro = "CPF/CNPJ inválido." });
        if (!email.Contains('@')) return BadRequest(new { erro = "E-mail inválido." });
        if (string.IsNullOrWhiteSpace(req.Senha) || req.Senha.Length < 6) return BadRequest(new { erro = "A senha deve ter pelo menos 6 caracteres." });
        if (req.Senha != req.ConfirmarSenha) return BadRequest(new { erro = "As senhas não conferem." });
        if (await db.Usuarios.AnyAsync(x => x.Email.ToLower() == email)) return Conflict(new { erro = "E-mail já cadastrado." });
        if (await db.Usuarios.AnyAsync(x => x.CpfCnpj == cpfCnpj)) return Conflict(new { erro = "CPF/CNPJ já cadastrado." });

        var u = new Usuario
        {
            Nome = nome,
            CpfCnpj = cpfCnpj,
            Email = email,
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(req.Senha),
            Perfil = "editor",
            // Primeiro acesso fica bloqueado até liberação por administrador.
            Ativo = false
        };
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Cadastro realizado. Aguarde a liberação do administrador para acessar o painel." });
    }

    [HttpPost("solicitar-redefinicao")]
    [AllowAnonymous]
    public async Task<IActionResult> SolicitarRedefinicao(PasswordResetRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Usuarios.FirstOrDefaultAsync(x => x.Email.ToLower() == email, ct);
        if (user is not null && user.Ativo)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace("+", "-").Replace("/", "_").TrimEnd('=');
            user.ResetTokenHash = Hash(token);
            user.ResetTokenExpiraEm = DateTime.UtcNow.AddHours(1);
            user.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var front = (config["Frontend:BaseUrl"] ?? Request.Headers.Origin.FirstOrDefault() ?? "http://localhost:5173").TrimEnd('/');
            var link = $"{front}/admin/redefinir-senha?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(user.Email)}";
            await passwordEmail.EnviarLinkRedefinicaoAsync(user.Email, user.Nome, link, ct);
        }
        return Ok(new { mensagem = "Se o e-mail estiver cadastrado, enviaremos um link de redefinição." });
    }

    [HttpPost("redefinir-senha")]
    [AllowAnonymous]
    public async Task<IActionResult> RedefinirSenha(PasswordResetConfirmRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(req.NovaSenha) || req.NovaSenha.Length < 6) return BadRequest(new { erro = "A nova senha deve ter pelo menos 6 caracteres." });
        if (req.NovaSenha != req.ConfirmarSenha) return BadRequest(new { erro = "As senhas não conferem." });

        var tokenHash = Hash(req.Token ?? "");
        var user = await db.Usuarios.FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.ResetTokenHash == tokenHash);
        if (user is null || user.ResetTokenExpiraEm is null || user.ResetTokenExpiraEm < DateTime.UtcNow)
            return BadRequest(new { erro = "Link de redefinição inválido ou expirado." });

        user.SenhaHash = BCrypt.Net.BCrypt.HashPassword(req.NovaSenha);
        user.ResetTokenHash = null;
        user.ResetTokenExpiraEm = null;
        user.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Senha redefinida com sucesso." });
    }

    static bool SenhaConfere(string? senhaInformada, string? senhaSalva)
    {
        if (string.IsNullOrWhiteSpace(senhaInformada) || string.IsNullOrWhiteSpace(senhaSalva)) return false;
        if (senhaSalva == senhaInformada) return true;
        try { return BCrypt.Net.BCrypt.Verify(senhaInformada, senhaSalva); }
        catch { return false; }
    }

    static string SomenteDigitos(string? v) => new string((v ?? "").Where(char.IsDigit).ToArray());
    static string Hash(string v) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();
}
