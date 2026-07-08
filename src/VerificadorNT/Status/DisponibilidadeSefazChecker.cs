using HtmlAgilityPack;

namespace VerificadorNT.Status;

/// <summary>
/// Consulta as páginas oficiais de disponibilidade dos webservices de documentos fiscais
/// eletrônicos (nfe.fazenda.gov.br e cte.fazenda.gov.br), atualizadas a cada poucos
/// minutos, que mostram uma bolinha verde/amarela/vermelha por serviço (emissão,
/// eventos - que cobrem cancelamento e carta de correção -, inutilização, consultas...).
/// É a fonte oficial mais confiável de indisponibilidade real (ao contrário de relatos de
/// usuários em sites como o Downdetector).
/// </summary>
public static class DisponibilidadeSefazChecker
{
    public static readonly DocumentoMonitorado NfeNfce = new(
        "NFe / NFCe", "https://www.nfe.fazenda.gov.br/portal/disponibilidade.aspx", "BA");

    public static readonly DocumentoMonitorado Cte = new(
        "CTe", "https://www.cte.fazenda.gov.br/portal/disponibilidade.aspx", "SVRS");

    public static readonly IReadOnlyList<DocumentoMonitorado> Documentos = [NfeNfce, Cte];

    public static async Task<DisponibilidadeResultado> VerificarAsync(
        HttpClient http, DocumentoMonitorado documento, CancellationToken ct)
    {
        var html = await http.GetStringAsync(documento.Url, ct);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var tabela = doc.DocumentNode.SelectSingleNode("//table[contains(@id,'gdvDisponibilidade')]");
        if (tabela is null)
            throw new InvalidOperationException(
                $"Página de disponibilidade de {documento.Nome} não tem a tabela esperada (id contendo 'gdvDisponibilidade') — o site pode ter mudado o layout ou estar fora do ar.");

        var linhaCabecalho = tabela.SelectSingleNode(".//tr[1]");
        var cabecalhos = linhaCabecalho?.SelectNodes(".//th")
            ?.Select(th => HtmlEntity.DeEntitize(th.InnerText).Trim())
            .ToList() ?? new List<string>();

        var linhas = tabela.SelectNodes(".//tr[position()>1]");
        if (linhas is null)
            throw new InvalidOperationException(
                $"Tabela de disponibilidade de {documento.Nome} não tem nenhuma linha de dado além do cabeçalho.");

        foreach (var linha in linhas)
        {
            var celulas = linha.SelectNodes(".//td");
            if (celulas is null || celulas.Count == 0) continue;

            var chaveLinha = HtmlEntity.DeEntitize(celulas[0].InnerText).Trim();
            if (!string.Equals(chaveLinha, documento.LinhaBahia, StringComparison.OrdinalIgnoreCase)) continue;

            var servicos = new List<ServicoStatus>();
            for (var i = 1; i < celulas.Count && i < cabecalhos.Count; i++)
            {
                var nomeServico = cabecalhos[i].TrimEnd('4', ' ');
                if (nomeServico.Equals("Tempo Médio", StringComparison.OrdinalIgnoreCase)) continue;

                var img = celulas[i].SelectSingleNode(".//img");
                var src = img?.GetAttributeValue("src", "") ?? "";
                var cor = ExtrairCor(src);
                if (cor is null) continue; // célula vazia (serviço não aplicável a essa linha)

                servicos.Add(new ServicoStatus(nomeServico, cor));
            }

            return new DisponibilidadeResultado
            {
                Documento = documento.Nome,
                LinhaConsultada = documento.LinhaBahia,
                Servicos = servicos,
                VerificadoEmUtc = DateTime.UtcNow,
            };
        }

        throw new InvalidOperationException(
            $"Tabela de disponibilidade de {documento.Nome} não tem uma linha para '{documento.LinhaBahia}' — o site pode ter mudado o layout.");
    }

    private static string? ExtrairCor(string srcImagem)
    {
        var s = srcImagem.ToLowerInvariant();
        if (s.Contains("verde")) return "verde";
        if (s.Contains("amarel")) return "amarela";
        if (s.Contains("vermelh")) return "vermelho";
        return null;
    }
}
