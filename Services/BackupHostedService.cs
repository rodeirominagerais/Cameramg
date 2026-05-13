using Microsoft.Extensions.Options;

namespace Cameramg.Services;

public class BackupHostedService(IServiceScopeFactory scopeFactory, IOptions<GoogleDriveBackupOptions> options, ILogger<BackupHostedService> logger) : BackgroundService
{
    private readonly GoogleDriveBackupOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = ProximoDelay();
                await Task.Delay(delay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                if (!_options.Enabled) continue;

                using var scope = scopeFactory.CreateScope();
                var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
                var drive = scope.ServiceProvider.GetRequiredService<GoogleDriveBackupService>();
                var resultado = await backup.GerarBackupArquivoAsync(stoppingToken);
                try
                {
                    await drive.UploadAsync(resultado.CaminhoArquivo, resultado.NomeArquivo, stoppingToken);
                    await backup.AplicarRetencaoLocalAsync(_options.RetentionDays, stoppingToken);
                }
                finally
                {
                    if (File.Exists(resultado.CaminhoArquivo)) File.Delete(resultado.CaminhoArquivo);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no backup automático para Google Drive.");
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
    }

    private TimeSpan ProximoDelay()
    {
        var horario = TimeSpan.TryParse(_options.ScheduleUtc, out var h) ? h : new TimeSpan(3, 0, 0);
        var agora = DateTimeOffset.UtcNow;
        var proximo = new DateTimeOffset(agora.Year, agora.Month, agora.Day, horario.Hours, horario.Minutes, horario.Seconds, TimeSpan.Zero);
        if (proximo <= agora) proximo = proximo.AddDays(1);
        return proximo - agora;
    }
}
