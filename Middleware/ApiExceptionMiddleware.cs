using System.Net;
namespace Cameramg.Middleware;
public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (InvalidOperationException ex) { await Write(ctx, HttpStatusCode.BadRequest, ex.Message); }
        catch (UnauthorizedAccessException ex) { await Write(ctx, HttpStatusCode.Unauthorized, ex.Message); }
        catch (Exception ex) { logger.LogError(ex, "Erro interno"); await Write(ctx, HttpStatusCode.InternalServerError, "Erro interno no servidor."); }
    }
    static async Task Write(HttpContext ctx, HttpStatusCode code, string msg)
    {
        ctx.Response.StatusCode = (int)code; ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { erro = msg });
    }
}
