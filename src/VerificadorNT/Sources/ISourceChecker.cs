namespace VerificadorNT.Sources;

public interface ISourceChecker
{
    string Nome { get; }

    Task<IReadOnlyList<NotaTecnica>> BuscarAsync(HttpClient http, CancellationToken ct);
}
