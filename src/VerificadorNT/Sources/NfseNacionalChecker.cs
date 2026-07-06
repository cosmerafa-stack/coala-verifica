using HtmlAgilityPack;

namespace VerificadorNT.Sources;

/// <summary>
/// Checker para a página de notícias do Portal da NFS-e Nacional (gov.br/nfse), onde a
/// SE/CGNFS-e publica as notas técnicas e demais atualizações. Estrutura:
/// &lt;li&gt;&lt;div class="conteudo"&gt;&lt;h2 class="titulo"&gt;&lt;a href="..."&gt;título&lt;/a&gt;&lt;/h2&gt;
/// ...&lt;span class="descricao"&gt;&lt;span class="data"&gt;DD/MM/AAAA&lt;/span&gt;.
/// </summary>
public sealed class NfseNacionalChecker(string noticiasUrl) : ISourceChecker
{
    public string Nome => "NFSe Nacional";

    public async Task<IReadOnlyList<NotaTecnica>> BuscarAsync(HttpClient http, CancellationToken ct)
    {
        var html = await http.GetStringAsync(noticiasUrl, ct);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var resultado = new List<NotaTecnica>();
        var titulos = doc.DocumentNode.SelectNodes("//h2[contains(@class,'titulo')]/a[@href]");
        if (titulos is null) return resultado;

        foreach (var link in titulos)
        {
            var titulo = HtmlEntity.DeEntitize(link.InnerText).Trim();
            var href = link.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(href)) continue;
            if (!FiltroNotaTecnica.EhNotaTecnica(titulo)) continue;

            var container = link.SelectSingleNode("ancestor::li[1]");
            var dataNode = container?.SelectSingleNode(".//span[@class='data']");
            var data = dataNode?.InnerText.Trim();
            var categoriaNode = container?.SelectSingleNode(".//div[@class='categoria-noticia']");
            var descricao = categoriaNode is null ? null : HtmlEntity.DeEntitize(categoriaNode.InnerText).Trim();

            resultado.Add(new NotaTecnica
            {
                Fonte = Nome,
                Titulo = titulo,
                Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao,
                DataPublicacao = data,
                Url = new Uri(new Uri(noticiasUrl), href).ToString(),
            });
        }

        return resultado;
    }
}
