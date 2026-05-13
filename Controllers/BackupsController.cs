using Cameramg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cameramg.Controllers;

[ApiController]
[Route("api/backups")]
[Authorize(Policy = "Admin")]
public class BackupsController(BackupService backupService, GoogleDriveBackupService googleDrive) : ControllerBase
{
    [HttpGet("diagnostico")]
    public IActionResult Diagnostico()
        => Ok(backupService.Diagnostico(googleDrive.IsConfigured));

    [HttpGet("gerar")]
    public async Task<IActionResult> Gerar(CancellationToken ct)
    {
        var backup = await backupService.GerarBackupCompletoAsync(ct);
        return File(backup.Conteudo, "application/zip", backup.NomeArquivo);
    }

    [HttpPost("google-drive")]
    public async Task<IActionResult> GerarEEnviarGoogleDrive(CancellationToken ct)
    {
        if (!googleDrive.IsConfigured)
        {
            return BadRequest(new
            {
                erro = "Google Drive não configurado.",
                detalhe = "Configure GoogleDriveBackup:Enabled=true, Mode=OAuth, ClientId, ClientSecret, RedirectUri, FolderId e conecte a conta em /api/google-drive/login."
            });
        }

        var resultado = await backupService.GerarEEnviarGoogleDriveAsync(googleDrive, ct);
        return Ok(resultado);
    }
}
