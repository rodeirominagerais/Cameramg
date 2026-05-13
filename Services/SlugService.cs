using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace Cameramg.Services;
public class SlugService
{
    public string Gerar(string texto)
    {
        var normalized = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var slug = Regex.Replace(sb.ToString(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N") : slug;
    }
}
