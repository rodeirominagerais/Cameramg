using System.Text.Json.Serialization;

namespace Cameramg.Models;

public class Usuario
{
    public long Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string Perfil { get; set; } = "editor";
    public string? CpfCnpj { get; set; }
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiraEm { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
}

public class Categoria
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Tipo { get; set; } = "PUBLICACAO";
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public class Publicacao
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public long? IdAntigo { get; set; }
    public string Tipo { get; set; } = "NOTICIA";
    public bool Destaque { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Resumo { get; set; }
    public string? ConteudoHtml { get; set; }
    public string? ImagemCapa { get; set; }
    public string? Modalidade { get; set; }
    public string? Fornecedor { get; set; }
    public string? Situacao { get; set; }
    public DateTime? DataPublicacao { get; set; }
    public DateTime? DataAbertura { get; set; }
    public DateTime? DataEncerramento { get; set; }
    public long? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public string? Slug { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
    public List<Arquivo> Arquivos { get; set; } = [];
    public List<Imagem> Imagens { get; set; } = [];
}


public class Licitacao
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string? Orgao { get; set; }
    public string? Telefone { get; set; }
    public string? EmailPrincipal { get; set; }
    public string Modalidade { get; set; } = string.Empty;
    public string? ProcessoAdministrativo { get; set; }
    public string? NumeroLicitacao { get; set; }
    public string? Numero { get; set; }
    public string? Exercicio { get; set; }
    public string? Fornecedor { get; set; }
    public string Objeto { get; set; } = string.Empty;
    public decimal? Valor { get; set; }
    public DateTime? DataAbertura { get; set; }
    public DateTime? DataEncerramento { get; set; }
    public string? EmailPropostas { get; set; }
    public DateTime? PropostasInicio { get; set; }
    public DateTime? PropostasFim { get; set; }
    public DateTime? Julgamento { get; set; }
    public string? Informacoes { get; set; }
    public string Status { get; set; } = "EM ANDAMENTO";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
    public List<LicitacaoArquivo> Arquivos { get; set; } = [];
}

public class LicitacaoArquivo
{
    public long Id { get; set; }
    public long LicitacaoId { get; set; }
    [JsonIgnore]
    public Licitacao? Licitacao { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime? DataArquivo { get; set; }
    public string CaminhoRelativo { get; set; } = string.Empty;
    public string? NomeArquivo { get; set; }
    public string? Extensao { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public class Arquivo
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public long? IdAntigo { get; set; }
    public long? PublicacaoId { get; set; }
    public Publicacao? Publicacao { get; set; }
    public string Tipo { get; set; } = "DOCUMENTO";
    public string Titulo { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoRelativo { get; set; } = string.Empty;
    public string? Extensao { get; set; }
    public string? MimeType { get; set; }
    public long? TamanhoBytes { get; set; }
    public DateOnly? DataArquivo { get; set; }
    public string Origem { get; set; } = "portal";
    public bool Visivel { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public class Imagem
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public long? IdAntigo { get; set; }
    public long? PublicacaoId { get; set; }
    public Publicacao? Publicacao { get; set; }
    public string? Titulo { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoRelativo { get; set; } = string.Empty;
    public DateOnly? DataImagem { get; set; }
    public string Origem { get; set; } = "portal";
    public bool Visivel { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public class PaginaInstitucional
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Chave { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? ConteudoHtml { get; set; }
    public string? ImagemCapa { get; set; }
    public string? DadosJson { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime? AtualizadoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public class TelefoneUtil
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public long? IdAntigo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Observacao { get; set; }
    public bool Ativo { get; set; } = true;
}

public class OuvidoriaCategoria
{
    public long Id { get; set; }
    public long? IdAntigo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
}

public class OuvidoriaChamado
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public long? IdAntigo { get; set; }
    public string? Protocolo { get; set; }
    public long? CategoriaId { get; set; }
    public OuvidoriaCategoria? Categoria { get; set; }
    public string? Nome { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Assunto { get; set; }
    public string? Mensagem { get; set; }
    public string? Resposta { get; set; }
    public string Status { get; set; } = "ABERTO";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
}

public class ConfiguracaoSite
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Chave { get; set; } = string.Empty;
    public string? Valor { get; set; }
    public string? Descricao { get; set; }
}

public class AdminRegistro
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? DadosJson { get; set; }
    public bool Ativo { get; set; } = true;
    public string? Entidade { get; set; }
    public long? EntidadeId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
}

public class SubmenuPagina
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Menu { get; set; } = string.Empty;
    public string Pagina { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Rota { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? ConteudoHtml { get; set; }
    public string? Imagem { get; set; }
    public string? Arquivo { get; set; }
    public string? Status { get; set; } = "Publicado";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
}


public abstract class AtividadeParlamentarBase
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Resumo { get; set; }
    public string? Conteudo { get; set; }
    public string? Arquivo { get; set; }
    public string? Numero { get; set; }
    public DateTime? DataCriacao { get; set; }
    public string Status { get; set; } = "Publicado";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
}

public class AtaReuniao : AtividadeParlamentarBase { }
public class Portaria : AtividadeParlamentarBase { }
public class Requerimento : AtividadeParlamentarBase { }
public class Convocacao : AtividadeParlamentarBase { }
public class Indicacao : AtividadeParlamentarBase { }
public class Mocao : AtividadeParlamentarBase { }
public class Resolucao : AtividadeParlamentarBase { }
public class ProjetoResolucao : AtividadeParlamentarBase { }
public class Diploma : AtividadeParlamentarBase { }
public class Decreto : AtividadeParlamentarBase { }

public abstract class EditalBase
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Resumo { get; set; }
    public string? Conteudo { get; set; }
    public string? Numero { get; set; }
    public DateTime? DataPublicacao { get; set; }
    public DateTime? DataInicio { get; set; }
    public DateTime? DataFim { get; set; }
    public string Status { get; set; } = "Publicado";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
}

public abstract class EditalArquivoBase
{
    public long Id { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime? DataArquivo { get; set; }
    public string CaminhoRelativo { get; set; } = string.Empty;
    public string? NomeArquivo { get; set; }
    public string? Extensao { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public class ProcessoSeletivo : EditalBase
{
    public List<ProcessoSeletivoArquivo> Arquivos { get; set; } = [];
}

public class ProcessoSeletivoArquivo : EditalArquivoBase
{
    public long ProcessoSeletivoId { get; set; }
    [JsonIgnore]
    public ProcessoSeletivo? ProcessoSeletivo { get; set; }
}

public class Concurso : EditalBase
{
    public List<ConcursoArquivo> Arquivos { get; set; } = [];
}

public class ConcursoArquivo : EditalArquivoBase
{
    public long ConcursoId { get; set; }
    [JsonIgnore]
    public Concurso? Concurso { get; set; }
}
