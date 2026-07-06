using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace VerificadorNT.Sources;

/// <summary>
/// O portal do SPED (sped.rfb.gov.br) é uma coleção de páginas wiki (uma por módulo:
/// ECD, ECF, EFD-ICMS/IPI, EFD-Contribuições, EFD-Reinf, etc.) sem data padronizada de
/// atualização. Em vez de tentar um parser específico por módulo, monitoramos o
/// conteúdo textual de cada página e alertamos quando ele muda. O título do "item"
/// carrega o hash do conteúdo, então uma mudança de hash naturalmente vira uma nota
/// nova no histórico (ver HistoryStore).
/// </summary>
public sealed partial class SpedPageWatcher(string nomeModulo, string url) : ISourceChecker
{
    public string Nome => $"SPED - {nomeModulo}";

    public async Task<IReadOnlyList<NotaTecnica>> BuscarAsync(HttpClient http, CancellationToken ct)
    {
        var html = await http.GetStringAsync(url, ct);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var conteudoNode = doc.DocumentNode.SelectSingleNode("//*[@id='content'] | //body");
        var texto = conteudoNode?.InnerText ?? html;
        texto = NormalizarEspacos().Replace(texto, " ").Trim();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..12];

        return new List<NotaTecnica>
        {
            new()
            {
                Fonte = Nome,
                Titulo = $"Conteúdo da página atualizado (hash {hash})",
                Descricao = $"Alguma mudança foi detectada na página do módulo {nomeModulo} do SPED " +
                             "(sem detalhamento automático — confira o link para ver o que mudou).",
                DataPublicacao = DateTime.Now.ToString("dd/MM/yyyy"),
                Url = url,
            },
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex NormalizarEspacos();
}
