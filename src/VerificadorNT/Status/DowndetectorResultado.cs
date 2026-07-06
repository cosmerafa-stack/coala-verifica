namespace VerificadorNT.Status;

public sealed class DowndetectorResultado
{
    public required bool ProblemaDetectado { get; init; }
    public required string Resumo { get; init; }
    public IReadOnlyList<string> FalhasMaisRelatadas { get; init; } = [];
    public IReadOnlyList<string> ComentariosSobreBahia { get; init; } = [];
    public string? Erro { get; init; }
    public required DateTime VerificadoEmUtc { get; init; }
}
