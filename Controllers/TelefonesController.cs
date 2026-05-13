using System.Security.Claims; using Cameramg.Data; using Cameramg.Dtos; using Cameramg.Models; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc; using Microsoft.EntityFrameworkCore;
namespace Cameramg.Controllers;
[ApiController,Route("api/telefones")]
public class TelefonesController(AppDbContext db):ControllerBase
{
 [HttpGet, AllowAnonymous] public async Task<IActionResult> Listar(){var q=db.TelefonesUteis.AsNoTracking().Where(x=>x.Ativo); return Ok(await q.OrderBy(x=>x.Nome).ToListAsync());}
 [HttpPost,Authorize(Policy="Editor")] public async Task<IActionResult> Criar(TelefoneDto dto){var t=new TelefoneUtil{UsuarioId=Uid(),Nome=dto.Nome,Telefone=dto.Telefone,Email=dto.Email,Observacao=dto.Observacao,Ativo=dto.Ativo}; db.TelefonesUteis.Add(t); await db.SaveChangesAsync(); return Ok(t);}
 [HttpPut("{id:long}"),Authorize(Policy="Editor")] public async Task<IActionResult> Atualizar(long id,TelefoneDto dto){var t=await db.TelefonesUteis.FindAsync(id)??throw new InvalidOperationException("Telefone não encontrado."); if(Uid().HasValue && t.UsuarioId!=Uid()) return Forbid(); t.Nome=dto.Nome;t.Telefone=dto.Telefone;t.Email=dto.Email;t.Observacao=dto.Observacao;t.Ativo=dto.Ativo; await db.SaveChangesAsync(); return NoContent();}
 [HttpDelete("{id:long}"),AllowAnonymous] public async Task<IActionResult> Remover(long id){var t=await db.TelefonesUteis.FindAsync(id); if(t is null) return NoContent(); t.Ativo=false; await db.SaveChangesAsync(); return NoContent();}
 private long? Uid()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)?id:null;
}
