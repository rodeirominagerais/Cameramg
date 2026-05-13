using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/google-drive")]
public class GoogleDriveController(
    GoogleDriveBackupService googleDrive,
    BackupService backupService) : ControllerBase
{
    [HttpGet("status")]
    [AllowAnonymous]
    public IActionResult Status()
        => Ok(googleDrive.Status());

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login()
        => Redirect(googleDrive.BuildAuthorizationUrl());

    [HttpGet("oauth-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> OAuthCallback([FromQuery] string? code, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new
            {
                sucesso = false,
                erro = error,
                mensagem = "O Google retornou erro na autorização."
            });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new
            {
                sucesso = false,
                erro = "code ausente",
                mensagem = "O callback não recebeu o código OAuth do Google."
            });
        }

        var resultado = await googleDrive.ExchangeCodeAndSaveTokenAsync(code, ct);
        return Ok(resultado);
    }

    [HttpPost("backup-completo")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> BackupCompletoPost(CancellationToken ct)
        => Ok(await backupService.GerarEEnviarGoogleDriveAsync(googleDrive, ct));

    [HttpGet("backup-completo")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> BackupCompletoGet(CancellationToken ct)
        => Ok(await backupService.GerarEEnviarGoogleDriveAsync(googleDrive, ct));

    [HttpPost("upload-backup")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> UploadBackup(CancellationToken ct)
        => Ok(await backupService.GerarEEnviarGoogleDriveAsync(googleDrive, ct));
}
