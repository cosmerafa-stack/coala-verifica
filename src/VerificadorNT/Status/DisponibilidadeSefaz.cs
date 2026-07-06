namespace VerificadorNT.Status;

public sealed record ServicoStatus(string Nome, string Cor);

/// <summary>
/// Define uma página oficial de disponibilidade a monitorar (NFe/NFCe, CTe, ...) e qual
/// linha da tabela representa a Bahia. Alguns documentos usam um autorizador próprio por
/// UF (NFe/NFCe tem uma linha "BA" dedicada); outros usam infraestrutura compartilhada
/// (CTe na Bahia é atendido pela SVRS - Sefaz Virtual do Rio Grande do Sul), então a
/// "linha da Bahia" nesses casos é a do autorizador compartilhado.
/// </summary>
public sealed record DocumentoMonitorado(string Nome, string Url, string LinhaBahia);

public sealed class DisponibilidadeResultado
{
    public required string Documento { get; init; }
    public required string LinhaConsultada { get; init; }
    public required IReadOnlyList<ServicoStatus> Servicos { get; init; }
    public required DateTime VerificadoEmUtc { get; init; }

    public bool TemProblema => Servicos.Any(s => s.Cor != "verde");
    public IEnumerable<ServicoStatus> ServicosComProblema => Servicos.Where(s => s.Cor != "verde");
}
