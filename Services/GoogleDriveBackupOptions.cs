namespace Cameramg.Services;

public class GoogleDriveBackupOptions
{
    public bool Enabled { get; set; } = false;

    // OAuth = conta Google normal conectada pelo navegador.
    // ServiceAccount = conta de serviço antiga.
    public string Mode { get; set; } = "OAuth";

    public string? FolderId { get; set; }

    // OAuth
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string? TokenStoragePath { get; set; } = "App_Data/google-drive-token.json";

    // Service Account, mantido para compatibilidade.
    public string? ServiceAccountJson { get; set; }
    public string? ServiceAccountJsonPath { get; set; }
    public string? ServiceAccountEmail { get; set; }
    public string? PrivateKey { get; set; }

    public string? BackupRootPath { get; set; } = "App_Data/backups";
    public string? UploadRootPath { get; set; } = "uploads";
    public string? DatabaseProvider { get; set; } = "PostgreSQL";
    public string ScheduleUtc { get; set; } = "03:00";
    public int RetentionDays { get; set; } = 30;
    public bool CreateZipByModule { get; set; } = true;
    public bool IncludeDatabase { get; set; } = true;
    public bool IncludeUploads { get; set; } = true;
    public bool IncludeImages { get; set; } = true;
    public bool IncludeVideos { get; set; } = true;
    public bool IncludeDocuments { get; set; } = true;
    public bool IncludePdf { get; set; } = true;
    public string[] Modules { get; set; } = [];
}
