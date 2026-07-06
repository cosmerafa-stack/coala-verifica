using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace VerificadorNT.Sources;

/// <summary>
/// Checker para os portais nfe.fazenda.gov.br e cte.fazenda.gov.br, que compartilham o
/// mesmo mecanismo "listaConteudo.aspx": cada nota técnica aparece como
/// &lt;p&gt;&lt;a href="exibirArquivo.aspx?..."&gt;&lt;span class="tituloConteudo"&gt;título&lt;/span&gt;&lt;/a&gt;...&lt;/p&gt;
/// </summary>
public sealed partial class FazendaListaConteudoChecker(string nome, string baseUrl, string listaUrl)
    : ISourceChecker
{
    public string Nome => nome;

    public async Task<IReadOnlyList<NotaTecnica>> BuscarAsync(HttpClient http, CancellationToken ct)
    {
        var html = await http.GetStringAsync(listaUrl, ct);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var resultado = new List<NotaTecnica>();
        var spans = doc.DocumentNode.SelectNodes("//span[@class='tituloConteudo']");
        if (spans is null) return resultado;

        foreach (var span in spans)
        {
            var link = span.ParentNode;
            if (link?.Name != "a") continue;

            var href = link.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href)) continue;

            var titulo = HtmlEntity.DeEntitize(span.InnerText).Trim();
            var data = ExtrairData(titulo);
            var url = new Uri(new Uri(baseUrl), href).ToString();
            var descricao = ExtrairDescricao(link.ParentNode, titulo);

            resultado.Add(new NotaTecnica
            {
                Fonte = nome,
                Titulo = titulo,
                Descricao = descricao,
                DataPublicacao = data,
                Url = url,
            });
        }

        return resultado;
    }

    private static string? ExtrairData(string titulo)
    {
        var m = RegexData().Match(titulo);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>O parágrafo contém título + descrição juntos (ex.: "TítuloDescrição...");
    /// removemos o título do texto completo do parágrafo para isolar a descrição.</summary>
    private static string? ExtrairDescricao(HtmlNode paragrafo, string titulo)
    {
        var textoCompleto = HtmlEntity.DeEntitize(paragrafo.InnerText);
        var descricao = textoCompleto.Replace(titulo, "", StringComparison.Ordinal);
        descricao = RegexEspacos().Replace(descricao, " ").Trim();
        if (descricao.Length > 220) descricao = descricao[..220].TrimEnd() + "...";
        return string.IsNullOrWhiteSpace(descricao) ? null : descricao;
    }

    [GeneratedRegex(@"(\d{2}/\d{2}/\d{4})")]
    private static partial Regex RegexData();

    [GeneratedRegex(@"\s+")]
    private static partial Regex RegexEspacos();
}
