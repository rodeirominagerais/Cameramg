using Cameramg.Models;
using Microsoft.EntityFrameworkCore;

namespace Cameramg.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Publicacao> Publicacoes => Set<Publicacao>();
    public DbSet<Licitacao> Licitacoes => Set<Licitacao>();
    public DbSet<LicitacaoArquivo> LicitacaoArquivos => Set<LicitacaoArquivo>();
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();
    public DbSet<Imagem> Imagens => Set<Imagem>();
    public DbSet<PaginaInstitucional> PaginasInstitucionais => Set<PaginaInstitucional>();
    public DbSet<TelefoneUtil> TelefonesUteis => Set<TelefoneUtil>();
    public DbSet<OuvidoriaCategoria> OuvidoriaCategorias => Set<OuvidoriaCategoria>();
    public DbSet<OuvidoriaChamado> OuvidoriaChamados => Set<OuvidoriaChamado>();
    public DbSet<ConfiguracaoSite> ConfiguracoesSite => Set<ConfiguracaoSite>();
    public DbSet<AdminRegistro> AdminRegistros => Set<AdminRegistro>();
    public DbSet<SubmenuPagina> SubmenuPaginas => Set<SubmenuPagina>();
    public DbSet<AtaReuniao> AtasReunioes => Set<AtaReuniao>();
    public DbSet<Portaria> Portarias => Set<Portaria>();
    public DbSet<Requerimento> Requerimentos => Set<Requerimento>();
    public DbSet<Convocacao> Convocacoes => Set<Convocacao>();
    public DbSet<Indicacao> Indicacoes => Set<Indicacao>();
    public DbSet<Mocao> Mocoes => Set<Mocao>();
    public DbSet<Resolucao> Resolucoes => Set<Resolucao>();
    public DbSet<ProjetoResolucao> ProjetosResolucao => Set<ProjetoResolucao>();
    public DbSet<Diploma> Diplomas => Set<Diploma>();
    public DbSet<Decreto> Decretos => Set<Decreto>();

    public DbSet<ProcessoSeletivo> ProcessosSeletivos => Set<ProcessoSeletivo>();
    public DbSet<ProcessoSeletivoArquivo> ProcessosSeletivosArquivos => Set<ProcessoSeletivoArquivo>();
    public DbSet<Concurso> Concursos => Set<Concurso>();
    public DbSet<ConcursoArquivo> ConcursosArquivos => Set<ConcursoArquivo>();
    public DbSet<EstruturaAdministrativa> EstruturasAdministrativas => Set<EstruturaAdministrativa>();
    public DbSet<CalendarioReuniao> CalendarioReunioes => Set<CalendarioReuniao>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("unaccent");
        b.HasPostgresExtension("pg_trgm");
        b.Entity<Usuario>(e => { e.ToTable("usuarios"); e.HasIndex(x => x.Email).IsUnique(); e.Property(x=>x.Id).HasColumnName("id"); MapUsuario(e); });
        b.Entity<Categoria>(e => { e.ToTable("categorias"); e.HasIndex(x => x.Slug).IsUnique(); MapCategoria(e); });
        b.Entity<Publicacao>(e => { e.ToTable("publicacoes"); e.HasIndex(x => x.Tipo); e.HasIndex(x=>x.DataPublicacao); MapPublicacao(e); });
        b.Entity<Licitacao>(e => { e.ToTable("licitacoes"); e.HasIndex(x => x.Status); e.HasIndex(x => x.DataAbertura); MapLicitacao(e); });
        b.Entity<LicitacaoArquivo>(e => { e.ToTable("licitacao_arquivos"); e.HasIndex(x => x.LicitacaoId); MapLicitacaoArquivo(e); });
        b.Entity<Arquivo>(e => { e.ToTable("arquivos"); e.HasIndex(x=>x.PublicacaoId); MapArquivo(e); });
        b.Entity<Imagem>(e => { e.ToTable("imagens"); e.HasIndex(x=>x.PublicacaoId); MapImagem(e); });
        b.Entity<PaginaInstitucional>(e => { e.ToTable("paginas_institucionais"); e.HasIndex(x=>x.Chave).IsUnique(); MapPagina(e); });
        b.Entity<TelefoneUtil>(e => { e.ToTable("telefones_uteis"); MapTelefone(e); });
        b.Entity<OuvidoriaCategoria>(e => { e.ToTable("ouvidoria_categorias"); MapOuvidoriaCategoria(e); });
        b.Entity<OuvidoriaChamado>(e => { e.ToTable("ouvidoria_chamados"); MapOuvidoriaChamado(e); });
        b.Entity<ConfiguracaoSite>(e => { e.ToTable("configuracoes_site"); e.HasIndex(x=>new { x.Chave, x.UsuarioId }).IsUnique(); MapConfiguracao(e); });
        b.Entity<AdminRegistro>(e => { e.ToTable("admin_registros"); e.HasIndex(x=>x.Tipo); e.HasIndex(x=>x.Ativo); MapAdminRegistro(e); });
        b.Entity<SubmenuPagina>(e => { e.ToTable("submenu_paginas"); e.HasIndex(x=>x.Rota).IsUnique(); e.HasIndex(x=>x.Slug); e.HasIndex(x=>x.Ativo); MapSubmenuPagina(e); });
        b.Entity<EstruturaAdministrativa>(e => { e.ToTable("camara_estruturas_administrativas"); e.HasIndex(x => x.Ativo); e.HasIndex(x => x.Status); MapEstruturaAdministrativa(e); });
        b.Entity<CalendarioReuniao>(e => { e.ToTable("camara_calendario_reunioes"); e.HasIndex(x => x.Ativo); e.HasIndex(x => x.Status); e.HasIndex(x => x.DataReuniao); MapCalendarioReuniao(e); });
        b.Entity<ProcessoSeletivo>(e => { e.ToTable("processos_seletivos"); e.HasIndex(x => x.Ativo); e.HasIndex(x => x.Status); e.HasIndex(x => x.DataPublicacao); MapEdital(e); e.HasMany(x => x.Arquivos).WithOne(x => x.ProcessoSeletivo).HasForeignKey(x => x.ProcessoSeletivoId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<ProcessoSeletivoArquivo>(e => { e.ToTable("processos_seletivos_arquivos"); e.HasIndex(x => x.ProcessoSeletivoId); MapEditalArquivo(e); e.Property(x => x.ProcessoSeletivoId).HasColumnName("processo_seletivo_id"); });
        b.Entity<Concurso>(e => { e.ToTable("concursos"); e.HasIndex(x => x.Ativo); e.HasIndex(x => x.Status); e.HasIndex(x => x.DataPublicacao); MapEdital(e); e.HasMany(x => x.Arquivos).WithOne(x => x.Concurso).HasForeignKey(x => x.ConcursoId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<ConcursoArquivo>(e => { e.ToTable("concursos_arquivos"); e.HasIndex(x => x.ConcursoId); MapEditalArquivo(e); e.Property(x => x.ConcursoId).HasColumnName("concurso_id"); });
        b.Entity<AtaReuniao>(e => { e.ToTable("atas_reunioes"); MapAtividadeParlamentar(e); });
        b.Entity<Portaria>(e => { e.ToTable("portarias"); MapAtividadeParlamentar(e); });
        b.Entity<Requerimento>(e => { e.ToTable("requerimentos"); MapAtividadeParlamentar(e); });
        b.Entity<Convocacao>(e => { e.ToTable("convocacoes"); MapAtividadeParlamentar(e); });
        b.Entity<Indicacao>(e => { e.ToTable("indicacoes"); MapAtividadeParlamentar(e); });
        b.Entity<Mocao>(e => { e.ToTable("mocoes"); MapAtividadeParlamentar(e); });
        b.Entity<Resolucao>(e => { e.ToTable("resolucoes"); MapAtividadeParlamentar(e); });
        b.Entity<ProjetoResolucao>(e => { e.ToTable("projetos_resolucoes"); MapAtividadeParlamentar(e); });
        b.Entity<Diploma>(e => { e.ToTable("diplomas"); MapAtividadeParlamentar(e); });
        b.Entity<Decreto>(e => { e.ToTable("decretos"); MapAtividadeParlamentar(e); });
    }
    static void Base<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e) where TEntity: class { e.Property<long>("Id").HasColumnName("id"); }
    static void MapUsuario(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Usuario> e){ e.Property(x=>x.Nome).HasColumnName("nome"); e.Property(x=>x.Email).HasColumnName("email"); e.Property(x=>x.SenhaHash).HasColumnName("senha_hash"); e.Property(x=>x.Perfil).HasColumnName("perfil"); e.Property(x=>x.CpfCnpj).HasColumnName("cpf_cnpj"); e.Property(x=>x.ResetTokenHash).HasColumnName("reset_token_hash"); e.Property(x=>x.ResetTokenExpiraEm).HasColumnName("reset_token_expira_em"); e.Property(x=>x.Ativo).HasColumnName("ativo"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); }
    static void MapCategoria(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Categoria> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.Nome).HasColumnName("nome"); e.Property(x=>x.Slug).HasColumnName("slug"); e.Property(x=>x.Tipo).HasColumnName("tipo"); e.Property(x=>x.Ordem).HasColumnName("ordem"); e.Property(x=>x.Ativo).HasColumnName("ativo"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); }
    static void MapPublicacao(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Publicacao> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.IdAntigo).HasColumnName("id_antigo"); e.Property(x=>x.Tipo).HasColumnName("tipo"); e.Property(x=>x.Destaque).HasColumnName("destaque"); e.Property(x=>x.Titulo).HasColumnName("titulo"); e.Property(x=>x.Resumo).HasColumnName("resumo"); e.Property(x=>x.ConteudoHtml).HasColumnName("conteudo_html"); e.Property(x=>x.ImagemCapa).HasColumnName("imagem_capa"); e.Property(x=>x.Modalidade).HasColumnName("modalidade"); e.Property(x=>x.Fornecedor).HasColumnName("fornecedor"); e.Property(x=>x.Situacao).HasColumnName("situacao"); e.Property(x=>x.DataPublicacao).HasColumnName("data_publicacao"); e.Property(x=>x.DataAbertura).HasColumnName("data_abertura"); e.Property(x=>x.DataEncerramento).HasColumnName("data_encerramento"); e.Property(x=>x.CategoriaId).HasColumnName("categoria_id"); e.Property(x=>x.Slug).HasColumnName("slug"); e.Property(x=>x.Ativo).HasColumnName("ativo"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); }

    static void MapLicitacao(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Licitacao> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.Orgao).HasColumnName("orgao"); e.Property(x=>x.Telefone).HasColumnName("telefone"); e.Property(x=>x.EmailPrincipal).HasColumnName("email_principal"); e.Property(x=>x.Modalidade).HasColumnName("modalidade"); e.Property(x=>x.ProcessoAdministrativo).HasColumnName("processo_administrativo"); e.Property(x=>x.NumeroLicitacao).HasColumnName("numero_licitacao"); e.Property(x=>x.Numero).HasColumnName("numero"); e.Property(x=>x.Exercicio).HasColumnName("exercicio"); e.Property(x=>x.Fornecedor).HasColumnName("fornecedor"); e.Property(x=>x.Objeto).HasColumnName("objeto"); e.Property(x=>x.Valor).HasColumnName("valor"); e.Property(x=>x.DataAbertura).HasColumnName("data_abertura"); e.Property(x=>x.DataEncerramento).HasColumnName("data_encerramento"); e.Property(x=>x.EmailPropostas).HasColumnName("email_propostas"); e.Property(x=>x.PropostasInicio).HasColumnName("propostas_inicio"); e.Property(x=>x.PropostasFim).HasColumnName("propostas_fim"); e.Property(x=>x.Julgamento).HasColumnName("julgamento"); e.Property(x=>x.Informacoes).HasColumnName("informacoes"); e.Property(x=>x.Status).HasColumnName("status"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); e.HasMany(x=>x.Arquivos).WithOne(x=>x.Licitacao).HasForeignKey(x=>x.LicitacaoId).OnDelete(DeleteBehavior.Cascade); }
    static void MapLicitacaoArquivo(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<LicitacaoArquivo> e){ Base(e); e.Property(x=>x.LicitacaoId).HasColumnName("licitacao_id"); e.Property(x=>x.Descricao).HasColumnName("descricao"); e.Property(x=>x.DataArquivo).HasColumnName("data_arquivo"); e.Property(x=>x.CaminhoRelativo).HasColumnName("caminho_relativo"); e.Property(x=>x.NomeArquivo).HasColumnName("nome_arquivo"); e.Property(x=>x.Extensao).HasColumnName("extensao"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); }
    static void MapArquivo(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Arquivo> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.IdAntigo).HasColumnName("id_antigo"); e.Property(x=>x.PublicacaoId).HasColumnName("publicacao_id"); e.Property(x=>x.Tipo).HasColumnName("tipo"); e.Property(x=>x.Titulo).HasColumnName("titulo"); e.Property(x=>x.NomeArquivo).HasColumnName("nome_arquivo"); e.Property(x=>x.CaminhoRelativo).HasColumnName("caminho_relativo"); e.Property(x=>x.Extensao).HasColumnName("extensao"); e.Property(x=>x.MimeType).HasColumnName("mime_type"); e.Property(x=>x.TamanhoBytes).HasColumnName("tamanho_bytes"); e.Property(x=>x.DataArquivo).HasColumnName("data_arquivo"); e.Property(x=>x.Origem).HasColumnName("origem"); e.Property(x=>x.Visivel).HasColumnName("visivel"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); }
    static void MapImagem(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Imagem> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.IdAntigo).HasColumnName("id_antigo"); e.Property(x=>x.PublicacaoId).HasColumnName("publicacao_id"); e.Property(x=>x.Titulo).HasColumnName("titulo"); e.Property(x=>x.NomeArquivo).HasColumnName("nome_arquivo"); e.Property(x=>x.CaminhoRelativo).HasColumnName("caminho_relativo"); e.Property(x=>x.DataImagem).HasColumnName("data_imagem"); e.Property(x=>x.Origem).HasColumnName("origem"); e.Property(x=>x.Visivel).HasColumnName("visivel"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); }
    static void MapPagina(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PaginaInstitucional> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.Chave).HasColumnName("chave"); e.Property(x=>x.Titulo).HasColumnName("titulo"); e.Property(x=>x.ConteudoHtml).HasColumnName("conteudo_html"); e.Property(x=>x.ImagemCapa).HasColumnName("imagem_capa"); e.Property(x=>x.DadosJson).HasColumnName("dados_json"); e.Property(x=>x.Ativo).HasColumnName("ativo"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); }
    static void MapTelefone(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TelefoneUtil> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.IdAntigo).HasColumnName("id_antigo"); e.Property(x=>x.Nome).HasColumnName("nome"); e.Property(x=>x.Telefone).HasColumnName("telefone"); e.Property(x=>x.Email).HasColumnName("email"); e.Property(x=>x.Observacao).HasColumnName("observacao"); e.Property(x=>x.Ativo).HasColumnName("ativo"); }
    static void MapOuvidoriaCategoria(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<OuvidoriaCategoria> e){ Base(e); e.Property(x=>x.IdAntigo).HasColumnName("id_antigo"); e.Property(x=>x.Nome).HasColumnName("nome"); e.Property(x=>x.Ordem).HasColumnName("ordem"); e.Property(x=>x.Ativo).HasColumnName("ativo"); }
    static void MapOuvidoriaChamado(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<OuvidoriaChamado> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.IdAntigo).HasColumnName("id_antigo"); e.Property(x=>x.Protocolo).HasColumnName("protocolo"); e.Property(x=>x.CategoriaId).HasColumnName("categoria_id"); e.Property(x=>x.Nome).HasColumnName("nome"); e.Property(x=>x.Email).HasColumnName("email"); e.Property(x=>x.Telefone).HasColumnName("telefone"); e.Property(x=>x.Assunto).HasColumnName("assunto"); e.Property(x=>x.Mensagem).HasColumnName("mensagem"); e.Property(x=>x.Resposta).HasColumnName("resposta"); e.Property(x=>x.Status).HasColumnName("status"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); }
    static void MapAdminRegistro(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AdminRegistro> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.Tipo).HasColumnName("tipo"); e.Property(x=>x.Titulo).HasColumnName("titulo"); e.Property(x=>x.Status).HasColumnName("status"); e.Property(x=>x.DadosJson).HasColumnName("dados_json"); e.Property(x=>x.Ativo).HasColumnName("ativo"); e.Property(x=>x.Entidade).HasColumnName("entidade"); e.Property(x=>x.EntidadeId).HasColumnName("entidade_id"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); }
    static void MapSubmenuPagina(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<SubmenuPagina> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.Menu).HasColumnName("menu"); e.Property(x=>x.Pagina).HasColumnName("pagina"); e.Property(x=>x.Slug).HasColumnName("slug"); e.Property(x=>x.Rota).HasColumnName("rota"); e.Property(x=>x.Titulo).HasColumnName("titulo"); e.Property(x=>x.ConteudoHtml).HasColumnName("conteudo_html"); e.Property(x=>x.Imagem).HasColumnName("imagem"); e.Property(x=>x.Arquivo).HasColumnName("arquivo"); e.Property(x=>x.Status).HasColumnName("status"); e.Property(x=>x.Ativo).HasColumnName("ativo"); e.Property(x=>x.CriadoEm).HasColumnName("criado_em"); e.Property(x=>x.AtualizadoEm).HasColumnName("atualizado_em"); }
    static void MapConfiguracao(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ConfiguracaoSite> e){ Base(e); e.Property(x=>x.UsuarioId).HasColumnName("usuario_id"); e.Property(x=>x.Chave).HasColumnName("chave"); e.Property(x=>x.Valor).HasColumnName("valor"); e.Property(x=>x.Descricao).HasColumnName("descricao"); }
    static void MapEstruturaAdministrativa(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<EstruturaAdministrativa> e)
    {
        Base(e);
        e.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        e.Property(x => x.Titulo).HasColumnName("titulo");
        e.Property(x => x.ConteudoHtml).HasColumnName("conteudo_html");
        e.Property(x => x.Imagem).HasColumnName("imagem");
        e.Property(x => x.Arquivo).HasColumnName("arquivo");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Ativo).HasColumnName("ativo");
        e.Property(x => x.CriadoEm).HasColumnName("criado_em");
        e.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
    }

    static void MapCalendarioReuniao(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CalendarioReuniao> e)
    {
        Base(e);
        e.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        e.Property(x => x.Titulo).HasColumnName("titulo");
        e.Property(x => x.Resumo).HasColumnName("resumo");
        e.Property(x => x.ConteudoHtml).HasColumnName("conteudo_html");
        e.Property(x => x.DataReuniao).HasColumnName("data_reuniao");
        e.Property(x => x.Local).HasColumnName("local");
        e.Property(x => x.Arquivo).HasColumnName("arquivo");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Ativo).HasColumnName("ativo");
        e.Property(x => x.CriadoEm).HasColumnName("criado_em");
        e.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
    }

    static void MapEdital<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e) where TEntity : EditalBase
    {
        Base(e);
        e.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        e.Property(x => x.Titulo).HasColumnName("titulo");
        e.Property(x => x.Resumo).HasColumnName("resumo");
        e.Property(x => x.Conteudo).HasColumnName("conteudo");
        e.Property(x => x.Numero).HasColumnName("numero");
        e.Property(x => x.DataPublicacao).HasColumnName("data_publicacao");
        e.Property(x => x.DataInicio).HasColumnName("data_inicio");
        e.Property(x => x.DataFim).HasColumnName("data_fim");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Ativo).HasColumnName("ativo");
        e.Property(x => x.CriadoEm).HasColumnName("criado_em");
        e.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
    }

    static void MapEditalArquivo<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e) where TEntity : EditalArquivoBase
    {
        Base(e);
        e.Property(x => x.Descricao).HasColumnName("descricao");
        e.Property(x => x.DataArquivo).HasColumnName("data_arquivo");
        e.Property(x => x.CaminhoRelativo).HasColumnName("caminho_relativo");
        e.Property(x => x.NomeArquivo).HasColumnName("nome_arquivo");
        e.Property(x => x.Extensao).HasColumnName("extensao");
        e.Property(x => x.CriadoEm).HasColumnName("criado_em");
    }

    static void MapAtividadeParlamentar<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e) where TEntity : AtividadeParlamentarBase
    {
        Base(e);
        e.HasIndex(x => x.Ativo);
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.DataCriacao);
        e.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        e.Property(x => x.Titulo).HasColumnName("titulo");
        e.Property(x => x.Resumo).HasColumnName("resumo");
        e.Property(x => x.Conteudo).HasColumnName("conteudo");
        e.Property(x => x.Arquivo).HasColumnName("arquivo");
        e.Property(x => x.Numero).HasColumnName("numero");
        e.Property(x => x.DataCriacao).HasColumnName("data_criacao");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Ativo).HasColumnName("ativo");
        e.Property(x => x.CriadoEm).HasColumnName("criado_em");
        e.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
    }
}
