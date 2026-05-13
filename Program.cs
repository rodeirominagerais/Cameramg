using System.Text;
using System.Text.Json.Serialization;
using Cameramg.Data;
using Cameramg.Middleware;
using Cameramg.Security;
using Cameramg.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connection));

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<SlugService>();
builder.Services.AddScoped<PasswordEmailService>();
builder.Services.AddScoped<OuvidoriaEmailService>();
builder.Services.AddScoped<SeedService>();
builder.Services.Configure<GoogleDriveBackupOptions>(builder.Configuration.GetSection("GoogleDriveBackup"));
builder.Services.AddScoped<BackupService>();
builder.Services.AddHttpClient<GoogleDriveBackupService>();
builder.Services.AddHostedService<BackupHostedService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.Secret))
    throw new InvalidOperationException("Jwt:Secret não configurado.");

var key = Encoding.UTF8.GetBytes(jwt.Secret);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(key),

            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("Editor", policy => policy.RequireAuthenticatedUser());
});

var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

var allowedOrigins = configuredOrigins
    .Concat(new[]
    {
        "http://localhost:5173",
        "http://localhost:3000", 
        "https://cameramg-y1bd.onrender.com",
        "https://rodeiromg.com.br",
        "https://www.rodeiromg.com.br",
        "http://rodeiro.mg.leg.br",
        "https://rodeiro.mg.leg.br"
    })
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("CameraCors", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.FullName);

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cameramg API",
        Version = "v1",
        Description = "API do Portal Institucional da Câmara Municipal de Rodeiro/MG"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Informe o token JWT no formato: Bearer {seu_token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    if (context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/uploads") ||
        context.Request.Path.StartsWithSegments("/debug"))
    {
        headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        headers["Pragma"] = "no-cache";
        headers["Expires"] = "0";
    }

    if (context.Request.IsHttps)
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var webRootPath = string.IsNullOrWhiteSpace(app.Environment.WebRootPath)
    ? Path.Combine(app.Environment.ContentRootPath, "wwwroot")
    : app.Environment.WebRootPath;

Directory.CreateDirectory(webRootPath);

var configuredStoragePath =
    builder.Configuration["Storage:BasePath"] ??
    builder.Configuration["Storage__BasePath"] ??
    "uploads";

var configuredStorageUrl =
    builder.Configuration["Storage:BaseUrl"] ??
    builder.Configuration["Storage__BaseUrl"] ??
    "/uploads";

configuredStorageUrl = "/" + configuredStorageUrl.Trim('/');

static string ResolveStoragePath(string storagePath, string webRootPath)
{
    if (string.IsNullOrWhiteSpace(storagePath))
        storagePath = "uploads";

    storagePath = storagePath.Trim();

    if (Path.IsPathRooted(storagePath))
        return Path.GetFullPath(storagePath);

    return Path.GetFullPath(
        Path.Combine(webRootPath, storagePath.Trim('/', '\\'))
    );
}

var publicUploadsPath =
    ResolveStoragePath(configuredStoragePath, webRootPath);

Directory.CreateDirectory(publicUploadsPath);

var legacyUploadsPath =
    Path.Combine(app.Environment.ContentRootPath, "uploads");

Directory.CreateDirectory(legacyUploadsPath);

var legacyWwwrootUploadsPath =
    Path.Combine(webRootPath, "uploads");

Directory.CreateDirectory(legacyWwwrootUploadsPath);

var renderUploadsPath = "/var/data/uploads";

if (Directory.Exists("/var/data"))
{
    Directory.CreateDirectory(renderUploadsPath);
}

app.UseStaticFiles();

var uploadRoots = new[]
{
    publicUploadsPath,
    legacyUploadsPath,
    legacyWwwrootUploadsPath,
    renderUploadsPath,
    Path.Combine(app.Environment.ContentRootPath, "src", "uploads")
}
.Where(x => !string.IsNullOrWhiteSpace(x))
.Select(Path.GetFullPath)
.Distinct(StringComparer.OrdinalIgnoreCase)
.Where(Directory.Exists)
.ToArray();

var uploadsProviders = uploadRoots
    .Select(x => (IFileProvider)new PhysicalFileProvider(x))
    .ToList();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new CompositeFileProvider(uploadsProviders),
    RequestPath = configuredStorageUrl
});

var contentTypeProvider = new FileExtensionContentTypeProvider();

if (app.Environment.IsDevelopment())
{
app.MapGet("/debug/uploads", () =>
{
    object MapRoot(string root)
    {
        var files = Directory.Exists(root)
            ? Directory
                .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Take(30)
                .ToArray()
            : Array.Empty<string>();

        return new
        {
            path = root,
            exists = Directory.Exists(root),
            fileCountPreview = files.Length,
            files = files
                .Select(x => x.Replace(root, "").Replace("\\", "/"))
                .ToArray()
        };
    }

    return Results.Ok(new
    {
        storageBasePath = configuredStoragePath,
        storageBaseUrl = configuredStorageUrl,
        contentRoot = app.Environment.ContentRootPath,
        webRoot = webRootPath,
        roots = uploadRoots.Select(MapRoot).ToArray()
    });
}).AllowAnonymous();
}

app.MapGet($"{configuredStorageUrl}/{{**filePath}}", (string filePath) =>
{
    if (string.IsNullOrWhiteSpace(filePath) ||
        filePath.Contains("..") ||
        filePath.Contains('\\'))
    {
        return (IResult)Results.NotFound();
    }

    filePath = filePath.TrimStart('/');

    foreach (var root in uploadRoots)
    {
        var full = Path.GetFullPath(
            Path.Combine(root, filePath)
        );

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            continue;

        if (System.IO.File.Exists(full))
        {
            if (!contentTypeProvider.TryGetContentType(full, out var contentType))
                contentType = "application/octet-stream";

            return (IResult)Results.File(full, contentType);
        }
    }

    var fileName = Path.GetFileName(filePath);

    if (!string.IsNullOrWhiteSpace(fileName))
    {
        foreach (var root in uploadRoots)
        {
            var found = Directory
                .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (found is not null)
            {
                if (!contentTypeProvider.TryGetContentType(found, out var contentType))
                    contentType = "application/octet-stream";

                return (IResult)Results.File(found, contentType);
            }
        }
    }

    return (IResult)Results.NotFound();
}).AllowAnonymous();

app.UseRouting();

app.UseCors("CameraCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/lgpd", () => Results.Ok(new
{
    portal = "Câmara Municipal de Rodeiro",
    politica = "Dados pessoais são tratados somente para atendimento ao cidadão, autenticação administrativa, segurança, auditoria e cumprimento de obrigações legais.",
    cookies = "Não usamos cookies de publicidade. O frontend utiliza armazenamento local para sessão administrativa, consentimento LGPD e preferências de usabilidade.",
    direitos = new[] { "confirmação de tratamento", "acesso aos dados", "correção ou atualização", "informações sobre finalidade", "solicitação pela Ouvidoria/e-SIC" }
})).AllowAnonymous();

app.MapGet("/", () => Results.Ok(new
{
    sistema = "Cameramg",
    status = "online"
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.EnsureCreatedAsync();

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS admin_registros (
            id BIGSERIAL PRIMARY KEY,
            tipo VARCHAR(80) NOT NULL,
            titulo VARCHAR(500) NOT NULL,
            status VARCHAR(120) NULL,
            dados_json TEXT NULL,
            ativo BOOLEAN NOT NULL DEFAULT TRUE,
            entidade VARCHAR(120) NULL,
            entidade_id BIGINT NULL,
            criado_em TIMESTAMP NOT NULL DEFAULT NOW(),
            atualizado_em TIMESTAMP NULL
        );
        ALTER TABLE admin_registros ADD COLUMN IF NOT EXISTS entidade VARCHAR(120) NULL;
        ALTER TABLE admin_registros ADD COLUMN IF NOT EXISTS entidade_id BIGINT NULL;
        CREATE INDEX IF NOT EXISTS idx_admin_registros_tipo ON admin_registros(tipo);
        CREATE INDEX IF NOT EXISTS idx_admin_registros_ativo ON admin_registros(ativo);
        CREATE INDEX IF NOT EXISTS idx_admin_registros_entidade ON admin_registros(entidade, entidade_id);
        CREATE TABLE IF NOT EXISTS configuracoes_site (
            id BIGSERIAL PRIMARY KEY,
            chave VARCHAR(180) NOT NULL,
            valor TEXT NULL,
            descricao TEXT NULL,
            usuario_id BIGINT NULL
        );
        ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS id BIGSERIAL;
        ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS chave VARCHAR(180) NOT NULL DEFAULT '';
        ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS valor TEXT NULL;
        ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS descricao TEXT NULL;
        ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;

        ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS cpf_cnpj VARCHAR(40) NULL;
        ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS reset_token_hash TEXT NULL;
        ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS reset_token_expira_em TIMESTAMP NULL;
        ALTER TABLE categorias ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE publicacoes ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE arquivos ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE imagens ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE paginas_institucionais ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE paginas_institucionais ADD COLUMN IF NOT EXISTS dados_json TEXT NULL;
        ALTER TABLE telefones_uteis ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE ouvidoria_chamados ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE ouvidoria_chamados ADD COLUMN IF NOT EXISTS resposta TEXT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_ouvidoria_chamados_protocolo ON ouvidoria_chamados(protocolo) WHERE protocolo IS NOT NULL;
        ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        DROP INDEX IF EXISTS ix_configuracoes_site_chave;
        DROP INDEX IF EXISTS ix_configuracoes_site_chave_usuario_id;
        WITH duplicadas AS (
            SELECT id, ROW_NUMBER() OVER (PARTITION BY chave, usuario_id ORDER BY id) AS rn
            FROM configuracoes_site
        )
        DELETE FROM configuracoes_site c USING duplicadas d
        WHERE c.id = d.id AND d.rn > 1;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_configuracoes_site_chave_global
            ON configuracoes_site(chave) WHERE usuario_id IS NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_configuracoes_site_chave_usuario
            ON configuracoes_site(chave, usuario_id) WHERE usuario_id IS NOT NULL;
        ALTER TABLE admin_registros ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        CREATE INDEX IF NOT EXISTS idx_publicacoes_usuario ON publicacoes(usuario_id);
        CREATE INDEX IF NOT EXISTS idx_arquivos_usuario ON arquivos(usuario_id);
        CREATE INDEX IF NOT EXISTS idx_imagens_usuario ON imagens(usuario_id);
        CREATE INDEX IF NOT EXISTS idx_admin_registros_usuario ON admin_registros(usuario_id);

        CREATE TABLE IF NOT EXISTS vereadores (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS sessoes_legislativas (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS diarios_oficiais (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS videos (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS eventos_agenda (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);

        CREATE TABLE IF NOT EXISTS banners (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, imagem VARCHAR(700), link VARCHAR(700), ordem INT NOT NULL DEFAULT 0, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS menus_portal (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(180) NOT NULL, url VARCHAR(700), pai_id BIGINT NULL, ordem INT NOT NULL DEFAULT 0, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS submenu_paginas (id BIGSERIAL PRIMARY KEY, menu VARCHAR(160) NOT NULL, pagina VARCHAR(220) NOT NULL, slug VARCHAR(220) NOT NULL, rota VARCHAR(420) NOT NULL, titulo VARCHAR(500) NOT NULL, conteudo_html TEXT NULL, imagem VARCHAR(700) NULL, arquivo VARCHAR(700) NULL, status VARCHAR(120) NULL DEFAULT 'Publicado', ativo BOOLEAN NOT NULL DEFAULT TRUE, usuario_id BIGINT NULL, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_submenu_paginas_rota ON submenu_paginas(rota);
        CREATE INDEX IF NOT EXISTS idx_submenu_paginas_slug ON submenu_paginas(slug);
        CREATE INDEX IF NOT EXISTS idx_submenu_paginas_ativo ON submenu_paginas(ativo);
        CREATE TABLE IF NOT EXISTS notificacoes (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(300) NOT NULL, mensagem TEXT, usuario_id BIGINT NULL, lida BOOLEAN NOT NULL DEFAULT FALSE, criado_em TIMESTAMP NOT NULL DEFAULT NOW());
        CREATE TABLE IF NOT EXISTS auditoria_logs (id BIGSERIAL PRIMARY KEY, usuario_id BIGINT NULL, acao VARCHAR(120) NOT NULL, entidade VARCHAR(120), entidade_id BIGINT NULL, detalhes_json TEXT, ip VARCHAR(80), criado_em TIMESTAMP NOT NULL DEFAULT NOW());
        CREATE TABLE IF NOT EXISTS permissoes (id BIGSERIAL PRIMARY KEY, perfil VARCHAR(80) NOT NULL, modulo VARCHAR(120) NOT NULL, pode_ler BOOLEAN NOT NULL DEFAULT TRUE, pode_criar BOOLEAN NOT NULL DEFAULT FALSE, pode_editar BOOLEAN NOT NULL DEFAULT FALSE, pode_excluir BOOLEAN NOT NULL DEFAULT FALSE);
        CREATE TABLE IF NOT EXISTS usuarios_sessoes (id BIGSERIAL PRIMARY KEY, usuario_id BIGINT NOT NULL, token_hash TEXT, ip VARCHAR(80), expira_em TIMESTAMP NULL, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), revogado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS anexos (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, arquivo_id BIGINT NULL, titulo VARCHAR(500), caminho VARCHAR(700), criado_em TIMESTAMP NOT NULL DEFAULT NOW());
        CREATE TABLE IF NOT EXISTS comentarios (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, nome VARCHAR(180), email VARCHAR(180), comentario TEXT, aprovado BOOLEAN NOT NULL DEFAULT FALSE, criado_em TIMESTAMP NOT NULL DEFAULT NOW());
        CREATE TABLE IF NOT EXISTS favoritos (id BIGSERIAL PRIMARY KEY, usuario_id BIGINT NOT NULL, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, criado_em TIMESTAMP NOT NULL DEFAULT NOW());
        CREATE TABLE IF NOT EXISTS seo_meta (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, titulo VARCHAR(300), descricao TEXT, palavras_chave TEXT, atualizado_em TIMESTAMP NULL);
        CREATE TABLE IF NOT EXISTS workflow_status (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, status_anterior VARCHAR(120), status_novo VARCHAR(120), usuario_id BIGINT NULL, observacao TEXT, criado_em TIMESTAMP NOT NULL DEFAULT NOW());

        ALTER TABLE vereadores ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE sessoes_legislativas ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE diarios_oficiais ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE videos ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE eventos_agenda ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;

        CREATE INDEX IF NOT EXISTS idx_vereadores_ativo ON vereadores(ativo);
        CREATE INDEX IF NOT EXISTS idx_sessoes_ativo ON sessoes_legislativas(ativo);
        CREATE INDEX IF NOT EXISTS idx_videos_ativo ON videos(ativo);
        CREATE INDEX IF NOT EXISTS idx_eventos_ativo ON eventos_agenda(ativo);
        CREATE INDEX IF NOT EXISTS idx_auditoria_entidade ON auditoria_logs(entidade, entidade_id);
        CREATE INDEX IF NOT EXISTS idx_anexos_entidade ON anexos(entidade, entidade_id);

        CREATE TABLE IF NOT EXISTS processos_seletivos (
            id BIGSERIAL PRIMARY KEY,
            usuario_id BIGINT NULL,
            titulo TEXT NOT NULL DEFAULT '',
            resumo TEXT NULL,
            conteudo TEXT NULL,
            numero TEXT NULL,
            data_publicacao TIMESTAMPTZ NULL,
            data_inicio TIMESTAMPTZ NULL,
            data_fim TIMESTAMPTZ NULL,
            status TEXT NOT NULL DEFAULT 'Publicado',
            ativo BOOLEAN NOT NULL DEFAULT TRUE,
            criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            atualizado_em TIMESTAMPTZ NULL
        );
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS titulo TEXT NOT NULL DEFAULT '';
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS resumo TEXT NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS conteudo TEXT NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS numero TEXT NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS data_publicacao TIMESTAMPTZ NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS data_inicio TIMESTAMPTZ NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS data_fim TIMESTAMPTZ NULL;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'Publicado';
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS ativo BOOLEAN NOT NULL DEFAULT TRUE;
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW();
        ALTER TABLE processos_seletivos ADD COLUMN IF NOT EXISTS atualizado_em TIMESTAMPTZ NULL;
        CREATE INDEX IF NOT EXISTS ix_processos_seletivos_ativo ON processos_seletivos(ativo);
        CREATE INDEX IF NOT EXISTS ix_processos_seletivos_status ON processos_seletivos(status);
        CREATE INDEX IF NOT EXISTS ix_processos_seletivos_data_publicacao ON processos_seletivos(data_publicacao);

        CREATE TABLE IF NOT EXISTS processos_seletivos_arquivos (
            id BIGSERIAL PRIMARY KEY,
            processo_seletivo_id BIGINT NOT NULL,
            descricao TEXT NOT NULL DEFAULT '',
            data_arquivo TIMESTAMPTZ NULL,
            caminho_relativo TEXT NOT NULL DEFAULT '',
            nome_arquivo TEXT NULL,
            extensao TEXT NULL,
            criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT fk_processos_seletivos_arquivos_processo
                FOREIGN KEY (processo_seletivo_id) REFERENCES processos_seletivos(id) ON DELETE CASCADE
        );
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS processo_seletivo_id BIGINT NOT NULL DEFAULT 0;
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS descricao TEXT NOT NULL DEFAULT '';
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS data_arquivo TIMESTAMPTZ NULL;
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS caminho_relativo TEXT NOT NULL DEFAULT '';
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS nome_arquivo TEXT NULL;
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS extensao TEXT NULL;
        ALTER TABLE processos_seletivos_arquivos ADD COLUMN IF NOT EXISTS criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW();
        CREATE INDEX IF NOT EXISTS ix_processos_seletivos_arquivos_processo_seletivo_id ON processos_seletivos_arquivos(processo_seletivo_id);

        CREATE TABLE IF NOT EXISTS concursos (
            id BIGSERIAL PRIMARY KEY,
            usuario_id BIGINT NULL,
            titulo TEXT NOT NULL DEFAULT '',
            resumo TEXT NULL,
            conteudo TEXT NULL,
            numero TEXT NULL,
            data_publicacao TIMESTAMPTZ NULL,
            data_inicio TIMESTAMPTZ NULL,
            data_fim TIMESTAMPTZ NULL,
            status TEXT NOT NULL DEFAULT 'Publicado',
            ativo BOOLEAN NOT NULL DEFAULT TRUE,
            criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            atualizado_em TIMESTAMPTZ NULL
        );
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS titulo TEXT NOT NULL DEFAULT '';
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS resumo TEXT NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS conteudo TEXT NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS numero TEXT NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS data_publicacao TIMESTAMPTZ NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS data_inicio TIMESTAMPTZ NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS data_fim TIMESTAMPTZ NULL;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'Publicado';
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS ativo BOOLEAN NOT NULL DEFAULT TRUE;
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW();
        ALTER TABLE concursos ADD COLUMN IF NOT EXISTS atualizado_em TIMESTAMPTZ NULL;
        CREATE INDEX IF NOT EXISTS ix_concursos_ativo ON concursos(ativo);
        CREATE INDEX IF NOT EXISTS ix_concursos_status ON concursos(status);
        CREATE INDEX IF NOT EXISTS ix_concursos_data_publicacao ON concursos(data_publicacao);

        CREATE TABLE IF NOT EXISTS concursos_arquivos (
            id BIGSERIAL PRIMARY KEY,
            concurso_id BIGINT NOT NULL,
            descricao TEXT NOT NULL DEFAULT '',
            data_arquivo TIMESTAMPTZ NULL,
            caminho_relativo TEXT NOT NULL DEFAULT '',
            nome_arquivo TEXT NULL,
            extensao TEXT NULL,
            criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT fk_concursos_arquivos_concurso
                FOREIGN KEY (concurso_id) REFERENCES concursos(id) ON DELETE CASCADE
        );
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS concurso_id BIGINT NOT NULL DEFAULT 0;
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS descricao TEXT NOT NULL DEFAULT '';
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS data_arquivo TIMESTAMPTZ NULL;
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS caminho_relativo TEXT NOT NULL DEFAULT '';
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS nome_arquivo TEXT NULL;
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS extensao TEXT NULL;
        ALTER TABLE concursos_arquivos ADD COLUMN IF NOT EXISTS criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW();
        CREATE INDEX IF NOT EXISTS ix_concursos_arquivos_concurso_id ON concursos_arquivos(concurso_id);
    ");

    var seed = scope.ServiceProvider.GetRequiredService<SeedService>();
    await seed.CriarAdminPadraoAsync();
}

app.Run();
