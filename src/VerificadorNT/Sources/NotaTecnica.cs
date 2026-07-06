namespace VerificadorNT.Sources;

public sealed class NotaTecnica
{
    public required string Fonte { get; init; }
    public required string Titulo { get; init; }
    public string? Descricao { get; init; }
    public string? DataPublicacao { get; init; }
    public required string Url { get; init; }

    public string Chave => $"{Fonte}|{Titulo}|{Url}".ToLowerInvariant();
}
