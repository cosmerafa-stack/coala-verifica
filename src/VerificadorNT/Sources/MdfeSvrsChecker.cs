using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace VerificadorNT.Sources;

/// <summary>
/// Checker para o Portal do MDF-e (dfe-portal.svrs.rs.gov.br), cujos itens usam
/// &lt;article class="conteudo-lista__item"&gt; com &lt;time datetime="..."&gt; e
/// &lt;h2 class="conteudo-lista__item__titulo"&gt;&lt;a&gt;título&lt;/a&gt;&lt;/h2&gt;.
/// Os links de download são disparados via JavaScript (sem href real), então
/// apontamos sempre para a página de documentos.
/// </summary>
public sealed partial class MdfeSvrsChecker(string paginaUrl) : ISourceChecker
{
    public string Nome => "MDFe";

    public async Task<IReadOnlyList<NotaTecnica>> BuscarAsync(HttpClient http, CancellationToken ct)
    {
        var html = await http.GetStringAsync(paginaUrl, ct);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var resultado = new List<NotaTecnica>();
        var artigos = doc.DocumentNode.SelectNodes("//article[contains(@class,'conteudo-lista__item')]");
        if (artigos is null) return resultado;

        foreach (var artigo in artigos)
        {
            var linkTitulo = artigo.SelectSingleNode(".//h2[contains(@class,'conteudo-lista__item__titulo')]//a");
            if (linkTitulo is null) continue;

            var titulo = HtmlEntity.DeEntitize(linkTitulo.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(titulo)) continue;
            if (!FiltroNotaTecnica.EhNotaTecnica(titulo)) continue;

            var tempo = artigo.SelectSingleNode(".//time[contains(@class,'conteudo-lista__item__datahora')]");
            var dataIso = tempo?.GetAttributeValue("datetime", null)?.Trim();
            var data = dataIso is not null && DateTime.TryParse(dataIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataParseada)
                ? dataParseada.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : tempo?.InnerText.Trim();

            var paragrafos = artigo.SelectNodes(".//p");
            var descricao = paragrafos is null
                ? null
                : string.Join(" ", paragrafos.Select(p => HtmlEntity.DeEntitize(p.InnerText).Trim()));
            if (descricao is not null) descricao = RegexEspacos().Replace(descricao, " ").Trim();
            if (string.IsNullOrWhiteSpace(descricao)) descricao = null;
            if (descricao is { Length: > 220 }) descricao = descricao[..220].TrimEnd() + "...";

            resultado.Add(new NotaTecnica
            {
                Fonte = Nome,
                Titulo = titulo,
                Descricao = descricao,
                DataPublicacao = data,
                Url = paginaUrl,
            });
        }

        return resultado;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex RegexEspacos();
}
