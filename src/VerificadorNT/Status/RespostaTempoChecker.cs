using System.Diagnostics;

namespace VerificadorNT.Status;

/// <summary>
/// Mede o tempo de resposta real dos webservices oficiais de NFe, NFCe e CTe da Bahia,
/// no mesmo espírito de monitores como o da TecnoSpeed. Esses webservices de produção
/// exigem certificado digital para operações de negócio, então qualquer chamada nossa sem
/// certificado recebe HTTP 403 — mas o tempo até essa resposta chegar já indica se o
/// servidor está de pé e o quão rápido está respondendo (um servidor fora do ar não
/// responde 403 rápido, ele expira ou recusa a conexão).
/// </summary>
public static class RespostaTempoChecker
{
    // Bahia usa autorizador próprio para NFe/NFCe (mesma infraestrutura para os dois modelos)
    // e a SVRS (Sefaz Virtual do RS) como autorizadora de CTe.
    private const string UrlNfeNfce = "https://nfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx";
    private const string UrlCte = "https://cte.svrs.rs.gov.br/ws/CTeStatusServicoV4/CTeStatusServicoV4.asmx";

    public static readonly DocumentoResposta Nfe = new("NFe", UrlNfeNfce);
    public static readonly DocumentoResposta Nfce = new("NFCe", UrlNfeNfce);
    public static readonly DocumentoResposta Cte = new("CTe", UrlCte);

    public static readonly IReadOnlyList<DocumentoResposta> Documentos = [Nfe, Nfce, Cte];

    public static async Task<RespostaTempo> VerificarAsync(HttpClient http, string url, CancellationToken ct)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(35));

            using var resposta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            cronometro.Stop();

            var segundos = cronometro.Elapsed.TotalSeconds;
            var nivel = segundos switch
            {
                <= 2 => NivelResposta.Normal,
                <= 5 => NivelResposta.Lento,
                < 30 => NivelResposta.MuitoLento,
                _ => NivelResposta.Timeout,
            };

            return new RespostaTempo(nivel, segundos, $"Respondeu em {segundos:0.0}s (HTTP {(int)resposta.StatusCode})");
        }
        catch (OperationCanceledException)
        {
            cronometro.Stop();
            return new RespostaTempo(NivelResposta.Timeout, cronometro.Elapsed.TotalSeconds, "Sem resposta em 35s");
        }
        catch (Exception ex)
        {
            cronometro.Stop();
            return new RespostaTempo(NivelResposta.Erro, null, $"Falha de conexão: {ex.Message}");
        }
    }
}
