using Cameramg.Data;
using Cameramg.Models;
using Microsoft.EntityFrameworkCore;
namespace Cameramg.Services;
public class SeedService(AppDbContext db)
{
    public async Task CriarAdminPadraoAsync()
    {
        if (!await db.Usuarios.AnyAsync())
        {
            db.Usuarios.Add(new Usuario { Nome = "Administrador", Email = "admin@rodeiro.mg.leg.br", SenhaHash = BCrypt.Net.BCrypt.HashPassword("admin123"), Perfil = "admin", Ativo = true });
        }

        // Garante o usuário administrativo padrão usado no painel.
        // Login aceito: root / root.
        if (!await db.Usuarios.AnyAsync(x => x.Email.ToLower() == "root" || x.Nome.ToLower() == "root"))
        {
            db.Usuarios.Add(new Usuario
            {
                Nome = "root",
                Email = "root",
                SenhaHash = BCrypt.Net.BCrypt.HashPassword("root"),
                Perfil = "admin",
                Ativo = true
            });
        }
        string[] categorias = ["Notícias", "Licitações", "Atas", "Leis", "Resoluções", "Transparência", "Sessões"];
        foreach (var c in categorias)
            if (!await db.Categorias.AnyAsync(x=>x.Nome==c)) db.Categorias.Add(new Categoria { Nome=c, Slug=c.ToLower().Replace("ç","c").Replace("í","i").Replace(" ","-"), Tipo="PUBLICACAO", Ativo=true });
        if (!await db.ConfiguracoesSite.AnyAsync())
        {
            db.ConfiguracoesSite.AddRange(
                new ConfiguracaoSite{Chave="site_nome", Valor="Câmara Municipal de Rodeiro/MG", Descricao="Nome do portal"},
                new ConfiguracaoSite{Chave="email_contato", Valor="contato@rodeiro.mg.leg.br", Descricao="E-mail principal"},
                new ConfiguracaoSite{Chave="storage_base_url", Valor="/uploads", Descricao="Base de arquivos"});
        }
        await db.SaveChangesAsync();
    }
}
