-- Portal Câmara de Rodeiro/MG - PostgreSQL
-- Estrutura base para criar o projeto novo em cima do dump antigo

CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE TABLE IF NOT EXISTS usuarios (
    id BIGSERIAL PRIMARY KEY,
    nome VARCHAR(180) NOT NULL,
    email VARCHAR(180) NOT NULL UNIQUE,
    senha_hash TEXT NOT NULL,
    perfil VARCHAR(40) NOT NULL DEFAULT 'editor',
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW(),
    atualizado_em TIMESTAMP NULL
);

CREATE TABLE IF NOT EXISTS categorias (
    id BIGSERIAL PRIMARY KEY,
    nome VARCHAR(160) NOT NULL,
    slug VARCHAR(180) NOT NULL UNIQUE,
    tipo VARCHAR(60) NOT NULL DEFAULT 'PUBLICACAO',
    ordem INT NOT NULL DEFAULT 0,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS publicacoes (
    id BIGSERIAL PRIMARY KEY,
    id_antigo BIGINT UNIQUE,
    tipo VARCHAR(80) NOT NULL,
    destaque BOOLEAN NOT NULL DEFAULT FALSE,
    titulo VARCHAR(500) NOT NULL,
    resumo TEXT NULL,
    conteudo_html TEXT NULL,
    imagem_capa VARCHAR(500) NULL,
    modalidade VARCHAR(120) NULL,
    fornecedor VARCHAR(500) NULL,
    situacao VARCHAR(120) NULL,
    data_publicacao TIMESTAMP NULL,
    data_abertura TIMESTAMP NULL,
    data_encerramento TIMESTAMP NULL,
    categoria_id BIGINT NULL REFERENCES categorias(id),
    slug VARCHAR(600) NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW(),
    atualizado_em TIMESTAMP NULL
);

CREATE TABLE IF NOT EXISTS arquivos (
    id BIGSERIAL PRIMARY KEY,
    id_antigo BIGINT UNIQUE,
    publicacao_id BIGINT NULL REFERENCES publicacoes(id) ON DELETE SET NULL,
    tipo VARCHAR(100) NOT NULL DEFAULT 'DOCUMENTO',
    titulo VARCHAR(500) NOT NULL,
    nome_arquivo VARCHAR(500) NOT NULL,
    caminho_relativo TEXT NOT NULL,
    extensao VARCHAR(20) NULL,
    mime_type VARCHAR(120) NULL,
    tamanho_bytes BIGINT NULL,
    data_arquivo DATE NULL,
    origem VARCHAR(80) NOT NULL DEFAULT 'backup_antigo',
    visivel BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS imagens (
    id BIGSERIAL PRIMARY KEY,
    id_antigo BIGINT UNIQUE,
    publicacao_id BIGINT NULL REFERENCES publicacoes(id) ON DELETE SET NULL,
    titulo VARCHAR(500) NULL,
    nome_arquivo VARCHAR(500) NOT NULL,
    caminho_relativo TEXT NOT NULL,
    data_imagem DATE NULL,
    origem VARCHAR(80) NOT NULL DEFAULT 'backup_antigo',
    visivel BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS paginas_institucionais (
    id BIGSERIAL PRIMARY KEY,
    chave VARCHAR(100) NOT NULL UNIQUE,
    titulo VARCHAR(300) NOT NULL,
    conteudo_html TEXT NULL,
    imagem_capa VARCHAR(500) NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    atualizado_em TIMESTAMP NULL,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS telefones_uteis (
    id BIGSERIAL PRIMARY KEY,
    id_antigo BIGINT UNIQUE,
    nome VARCHAR(250) NOT NULL,
    telefone VARCHAR(120) NOT NULL,
    email VARCHAR(180) NULL,
    observacao TEXT NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS ouvidoria_categorias (
    id BIGSERIAL PRIMARY KEY,
    id_antigo BIGINT UNIQUE,
    nome VARCHAR(180) NOT NULL,
    ordem INT NOT NULL DEFAULT 0,
    ativo BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS ouvidoria_chamados (
    id BIGSERIAL PRIMARY KEY,
    id_antigo BIGINT UNIQUE,
    protocolo VARCHAR(80) NULL,
    categoria_id BIGINT NULL REFERENCES ouvidoria_categorias(id),
    nome VARCHAR(180) NULL,
    email VARCHAR(180) NULL,
    telefone VARCHAR(80) NULL,
    assunto VARCHAR(300) NULL,
    mensagem TEXT NULL,
    resposta TEXT NULL,
    status VARCHAR(80) NOT NULL DEFAULT 'ABERTO',
    criado_em TIMESTAMP NOT NULL DEFAULT NOW(),
    atualizado_em TIMESTAMP NULL
);

CREATE TABLE IF NOT EXISTS configuracoes_site (
    id BIGSERIAL PRIMARY KEY,
    usuario_id BIGINT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    chave VARCHAR(120) NOT NULL,
    valor TEXT NULL,
    descricao TEXT NULL
);

ALTER TABLE configuracoes_site ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'configuracoes_site_chave_key') THEN
        ALTER TABLE configuracoes_site DROP CONSTRAINT configuracoes_site_chave_key;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_configuracoes_site_chave_usuario
ON configuracoes_site(chave, COALESCE(usuario_id, 0));

CREATE INDEX IF NOT EXISTS idx_publicacoes_tipo ON publicacoes(tipo);
CREATE INDEX IF NOT EXISTS idx_publicacoes_data ON publicacoes(data_publicacao DESC);
CREATE INDEX IF NOT EXISTS idx_publicacoes_titulo_trgm ON publicacoes USING gin (titulo gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_arquivos_publicacao ON arquivos(publicacao_id);
CREATE INDEX IF NOT EXISTS idx_arquivos_nome_trgm ON arquivos USING gin (nome_arquivo gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_imagens_publicacao ON imagens(publicacao_id);

INSERT INTO usuarios (nome, email, senha_hash, perfil)
VALUES ('Administrador', 'admin@rodeiro.mg.leg.br', 'ALTERAR_SENHA_HASH_NO_BACKEND', 'admin')
ON CONFLICT (email) DO NOTHING;

INSERT INTO configuracoes_site (chave, valor, descricao, usuario_id)
SELECT v.chave, v.valor, v.descricao, NULL
FROM (VALUES
('site_nome', 'Câmara Municipal de Rodeiro/MG', 'Nome exibido no portal'),
('email_contato', 'contato@rodeiro.mg.leg.br', 'E-mail institucional principal'),
('storage_base_url', '/uploads', 'URL base dos arquivos restaurados')
) AS v(chave, valor, descricao)
WHERE NOT EXISTS (
    SELECT 1 FROM configuracoes_site c
    WHERE c.chave = v.chave AND c.usuario_id IS NULL
);


CREATE TABLE IF NOT EXISTS admin_registros (
    id BIGSERIAL PRIMARY KEY,
    tipo VARCHAR(80) NOT NULL,
    titulo VARCHAR(500) NOT NULL,
    status VARCHAR(120) NULL,
    dados_json TEXT NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMP NOT NULL DEFAULT NOW(),
    atualizado_em TIMESTAMP NULL
);

CREATE INDEX IF NOT EXISTS idx_admin_registros_tipo ON admin_registros(tipo);
CREATE INDEX IF NOT EXISTS idx_admin_registros_ativo ON admin_registros(ativo);

-- Complemento completo do painel administrativo
ALTER TABLE admin_registros ADD COLUMN IF NOT EXISTS entidade VARCHAR(120) NULL;
ALTER TABLE admin_registros ADD COLUMN IF NOT EXISTS entidade_id BIGINT NULL;
CREATE INDEX IF NOT EXISTS idx_admin_registros_entidade ON admin_registros(entidade, entidade_id);

CREATE TABLE IF NOT EXISTS vereadores (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS sessoes_legislativas (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS diarios_oficiais (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS videos (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS eventos_agenda (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, status VARCHAR(120), dados_json TEXT, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS banners (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(500) NOT NULL, imagem VARCHAR(700), link VARCHAR(700), ordem INT NOT NULL DEFAULT 0, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS menus_portal (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(180) NOT NULL, url VARCHAR(700), pai_id BIGINT NULL, ordem INT NOT NULL DEFAULT 0, ativo BOOLEAN NOT NULL DEFAULT TRUE, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS notificacoes (id BIGSERIAL PRIMARY KEY, titulo VARCHAR(300) NOT NULL, mensagem TEXT, usuario_id BIGINT NULL, lida BOOLEAN NOT NULL DEFAULT FALSE, criado_em TIMESTAMP NOT NULL DEFAULT NOW());
CREATE TABLE IF NOT EXISTS auditoria_logs (id BIGSERIAL PRIMARY KEY, usuario_id BIGINT NULL, acao VARCHAR(120) NOT NULL, entidade VARCHAR(120), entidade_id BIGINT NULL, detalhes_json TEXT, ip VARCHAR(80), criado_em TIMESTAMP NOT NULL DEFAULT NOW());
CREATE TABLE IF NOT EXISTS permissoes (id BIGSERIAL PRIMARY KEY, perfil VARCHAR(80) NOT NULL, modulo VARCHAR(120) NOT NULL, pode_ler BOOLEAN NOT NULL DEFAULT TRUE, pode_criar BOOLEAN NOT NULL DEFAULT FALSE, pode_editar BOOLEAN NOT NULL DEFAULT FALSE, pode_excluir BOOLEAN NOT NULL DEFAULT FALSE);
CREATE TABLE IF NOT EXISTS usuarios_sessoes (id BIGSERIAL PRIMARY KEY, usuario_id BIGINT NOT NULL, token_hash TEXT, ip VARCHAR(80), expira_em TIMESTAMP NULL, criado_em TIMESTAMP NOT NULL DEFAULT NOW(), revogado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS anexos (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, arquivo_id BIGINT NULL, titulo VARCHAR(500), caminho VARCHAR(700), criado_em TIMESTAMP NOT NULL DEFAULT NOW());
CREATE TABLE IF NOT EXISTS comentarios (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, nome VARCHAR(180), email VARCHAR(180), comentario TEXT, aprovado BOOLEAN NOT NULL DEFAULT FALSE, criado_em TIMESTAMP NOT NULL DEFAULT NOW());
CREATE TABLE IF NOT EXISTS favoritos (id BIGSERIAL PRIMARY KEY, usuario_id BIGINT NOT NULL, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, criado_em TIMESTAMP NOT NULL DEFAULT NOW());
CREATE TABLE IF NOT EXISTS seo_meta (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, titulo VARCHAR(300), descricao TEXT, palavras_chave TEXT, atualizado_em TIMESTAMP NULL);
CREATE TABLE IF NOT EXISTS workflow_status (id BIGSERIAL PRIMARY KEY, entidade VARCHAR(120) NOT NULL, entidade_id BIGINT NOT NULL, status_anterior VARCHAR(120), status_novo VARCHAR(120), usuario_id BIGINT NULL, observacao TEXT, criado_em TIMESTAMP NOT NULL DEFAULT NOW());

CREATE UNIQUE INDEX IF NOT EXISTS ux_ouvidoria_chamados_protocolo ON ouvidoria_chamados(protocolo) WHERE protocolo IS NOT NULL;
