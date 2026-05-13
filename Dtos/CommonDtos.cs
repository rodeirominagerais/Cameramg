namespace Cameramg.Dtos;

public record LoginRequest(string Email, string Senha);
public record LoginResponse(string Token, long Id, string Nome, string Email, string Perfil, DateTime ExpiraEm);
public record PrimeiroAcessoRequest(string NomeCompleto, string CpfCnpj, string Email, string Senha, string ConfirmarSenha);
public record PasswordResetRequest(string Email);
public record PasswordResetConfirmRequest(string Email, string Token, string NovaSenha, string ConfirmarSenha);
public record UsuarioCreateDto(string Nome, string Email, string Senha, string Perfil, bool Ativo, string? CpfCnpj = null);
public record UsuarioUpdateDto(string Nome, string Email, string? Senha, string Perfil, bool Ativo, string? CpfCnpj = null);
public record CategoriaDto(string Nome, string Tipo, int Ordem, bool Ativo);
public record PublicacaoDto(string Tipo, bool Destaque, string Titulo, string? Resumo, string? ConteudoHtml, string? ImagemCapa, string? Modalidade, string? Fornecedor, string? Situacao, DateTime? DataPublicacao, DateTime? DataAbertura, DateTime? DataEncerramento, long? CategoriaId, bool Ativo);
public record PaginaDto(string? Chave, string? Titulo, string? ConteudoHtml, string? ImagemCapa, bool Ativo, string? DadosJson = null);
public record TelefoneDto(string Nome, string Telefone, string? Email, string? Observacao, bool Ativo);
public record OuvidoriaCreateDto(long? CategoriaId, string? Nome, string? Email, string? Telefone, string Assunto, string Mensagem, string? Tipo = null, string? Prioridade = null);
public record OuvidoriaUpdateDto(string Status, string? Resposta = null);
public record ConfiguracaoDto(string Chave, string? Valor, string? Descricao);
public record ApiPage<T>(IReadOnlyList<T> Itens, int Page, int PageSize, int Total);

public record AdminRegistroDto(string Tipo, string Titulo, string? Status, string? DadosJson, bool Ativo);


public record LicitacaoArquivoDto(string? Descricao, DateTime? DataArquivo, string? CaminhoRelativo);

public record LicitacaoArquivoResponse(
    long Id,
    long LicitacaoId,
    string Descricao,
    DateTime? DataArquivo,
    string CaminhoRelativo,
    string? NomeArquivo,
    string? Extensao,
    DateTime CriadoEm
);

public record LicitacaoResponse(
    long Id,
    long? UsuarioId,
    string? Orgao,
    string? Telefone,
    string? EmailPrincipal,
    string Modalidade,
    string? ProcessoAdministrativo,
    string? NumeroLicitacao,
    string? Numero,
    string? Exercicio,
    string? Fornecedor,
    string Objeto,
    decimal? Valor,
    DateTime? DataAbertura,
    DateTime? DataEncerramento,
    string? EmailPropostas,
    DateTime? PropostasInicio,
    DateTime? PropostasFim,
    DateTime? Julgamento,
    string? Informacoes,
    string Status,
    DateTime CriadoEm,
    DateTime? AtualizadoEm,
    IReadOnlyList<LicitacaoArquivoResponse> Arquivos
);

public record LicitacaoDto(
    string? Titulo,
    string? Orgao,
    string? Telefone,
    string? EmailPrincipal,
    string? Modalidade,
    string? ProcessoAdministrativo,
    string? NumeroLicitacao,
    string? Numero,
    string? Exercicio,
    string? Fornecedor,
    string? Objeto,
    decimal? Valor,
    DateTime? DataAbertura,
    DateTime? DataEncerramento,
    string? EmailPropostas,
    DateTime? PropostasInicio,
    DateTime? PropostasFim,
    DateTime? Julgamento,
    string? Informacoes,
    string? Status,
    List<LicitacaoArquivoDto>? Arquivos
);


public record AtividadeParlamentarDto(
    string Titulo,
    string? Resumo,
    string? Conteudo,
    string? Arquivo,
    string? Numero,
    DateTime? DataCriacao,
    string? Status,
    bool Ativo
);

public record AtividadeParlamentarResponse(
    long Id,
    long? UsuarioId,
    string Titulo,
    string? Resumo,
    string? Conteudo,
    string? Arquivo,
    string? Numero,
    DateTime? DataCriacao,
    string Status,
    bool Ativo,
    DateTime CriadoEm,
    DateTime? AtualizadoEm
);

public record EditalArquivoDto(string? Descricao, DateTime? DataArquivo, string? CaminhoRelativo);

public record EditalArquivoResponse(
    long Id,
    long RegistroId,
    string Descricao,
    DateTime? DataArquivo,
    string CaminhoRelativo,
    string? NomeArquivo,
    string? Extensao,
    DateTime CriadoEm
);

public record EditalDto(
    string Titulo,
    string? Resumo,
    string? Conteudo,
    string? Numero,
    DateTime? DataPublicacao,
    DateTime? DataInicio,
    DateTime? DataFim,
    string? Status,
    bool Ativo,
    List<EditalArquivoDto>? Arquivos
);

public record EditalResponse(
    long Id,
    long? UsuarioId,
    string Titulo,
    string? Resumo,
    string? Conteudo,
    string? Numero,
    DateTime? DataPublicacao,
    DateTime? DataInicio,
    DateTime? DataFim,
    string Status,
    bool Ativo,
    DateTime CriadoEm,
    DateTime? AtualizadoEm,
    IReadOnlyList<EditalArquivoResponse> Arquivos
);
