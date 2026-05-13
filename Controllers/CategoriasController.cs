using System.Security.Claims; using Cameramg.Data; using Cameramg.Dtos; using Cameramg.Models; using Cameramg.Services; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc; using Microsoft.EntityFrameworkCore;
namespace Cameramg.Controllers;
[ApiController,Route("api/categorias")]
public class CategoriasController(AppDbContext db,SlugService slug):ControllerBase
{
 [HttpGet, AllowAnonymous] public async Task<IActionResult> Listar([FromQuery]string? tipo,[FromQuery]bool? ativo){var q=db.Categorias.AsNoTracking().AsQueryable(); if(!string.IsNullOrWhiteSpace(tipo)) q=q.Where(x=>x.Tipo==tipo); if(ativo.HasValue) q=q.Where(x=>x.Ativo==ativo); return Ok(await q.OrderBy(x=>x.Ordem).ThenBy(x=>x.Nome).ToListAsync());}
 [HttpPost,Authorize(Policy="Editor")] public async Task<IActionResult> Criar(CategoriaDto dto){var c=new Categoria{UsuarioId=Uid(),Nome=dto.Nome,Slug=slug.Gerar(dto.Nome),Tipo=dto.Tipo,Ordem=dto.Ordem,Ativo=dto.Ativo}; db.Categorias.Add(c); await db.SaveChangesAsync(); return Ok(c);}
 [HttpPut("{id:long}"),Authorize(Policy="Editor")] public async Task<IActionResult> Atualizar(long id,CategoriaDto dto){var c=await db.Categorias.FindAsync(id)??throw new InvalidOperationException("Categoria não encontrada."); if(Uid().HasValue && c.UsuarioId!=Uid()) return Forbid(); c.Nome=dto.Nome;c.Tipo=dto.Tipo;c.Ordem=dto.Ordem;c.Ativo=dto.Ativo; await db.SaveChangesAsync(); return NoContent();}
 [HttpDelete("{id:long}"),Authorize(Policy="Admin")] public async Task<IActionResult> Remover(long id){var c=await db.Categorias.FindAsync(id)??throw new InvalidOperationException("Categoria não encontrada."); if(Uid().HasValue && c.UsuarioId!=Uid()) return Forbid(); c.Ativo=false; await db.SaveChangesAsync(); return NoContent();}
 private long? Uid()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)?id:null;
}
