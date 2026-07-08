using VerificadorNT.Http;

namespace VerificadorNT.Status;

public sealed record TempoRespostaResultado(string Documento, RespostaTempo Resposta);

public sealed class StatusMonitorResultado
{
    public required IReadOnlyList<DisponibilidadeResultado> Disponibilidades { get; init; }
    public required IReadOnlyList<TempoRespostaResultado> TemposResposta { get; init; }
    public required DowndetectorResultado Downdetector { get; init; }
    public required DateTime VerificadoEmUtc { get; init; }

    public bool AlertaBahia =>
        Disponibilidades.Any(d => d.TemProblema) || Downdetector.ComentariosSobreBahia.Count > 0;

    /// <summary>Identifica o "formato" do problema atual (quais serviços, qual origem),
    /// para saber se é um problema novo/diferente do último alertado ou o mesmo em curso.</summary>
    public string ChaveDoProblema()
    {
        if (!AlertaBahia) return "";
        var partes = new List<string>();
        foreach (var disp in Disponibilidades)
            partes.AddRange(disp.ServicosComProblema.Select(s => $"{disp.Documento}:{s.Nome}:{s.Cor}"));
        if (Downdetector.ComentariosSobreBahia.Count > 0)
            partes.Add("downdetector-bahia");
        return string.Join("|", partes);
    }
}

public sealed class StatusMonitor
{
    private readonly HttpClient _http;
    public event Action<string>? Log;

    public StatusMonitor(HttpClient http)
    {
        _http = http;
    }

    public async Task<StatusMonitorResultado> VerificarAsync(CancellationToken ct = default)
    {
        var disponibilidades = new List<DisponibilidadeResultado>();
        foreach (var documento in DisponibilidadeSefazChecker.Documentos)
        {
            try
            {
                var resultado = await DisponibilidadeSefazChecker.VerificarAsync(_http, documento, ct);
                disponibilidades.Add(resultado);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Disponibilidade {documento.Nome}: falha ao verificar ({ex.Message}).");
            }
        }

        var tarefasResposta = RespostaTempoChecker.Documentos.Select(async documento =>
        {
            var resposta = await RespostaTempoChecker.VerificarAsync(_http, documento.Url, ct);
            return new TempoRespostaResultado(documento.Nome, resposta);
        });
        var temposResposta = await Task.WhenAll(tarefasResposta);

        DowndetectorResultado downdetector;
        try
        {
            downdetector = await DowndetectorChecker.VerificarAsync(ct);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Downdetector: falha ao verificar ({ex.Message}).");
            downdetector = new DowndetectorResultado
            {
                ProblemaDetectado = false,
                Resumo = "Não foi possível verificar o Downdetector.",
                VerificadoEmUtc = DateTime.UtcNow,
            };
        }

        return new StatusMonitorResultado
        {
            Disponibilidades = disponibilidades,
            TemposResposta = temposResposta,
            Downdetector = downdetector,
            VerificadoEmUtc = DateTime.UtcNow,
        };
    }
}
