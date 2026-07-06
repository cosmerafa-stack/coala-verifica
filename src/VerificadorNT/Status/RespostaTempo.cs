namespace VerificadorNT.Status;

public enum NivelResposta
{
    Normal,      // <= 2s
    Lento,       // <= 5s
    MuitoLento,  // < 30s
    Timeout,     // >= 30s ou sem resposta dentro do prazo
    Erro,        // falha de conexão/DNS/TLS - nem chegou a responder
}

public sealed record RespostaTempo(NivelResposta Nivel, double? Segundos, string Detalhe);

/// <summary>Documento cujo tempo de resposta do webservice real é cronometrado.</summary>
public sealed record DocumentoResposta(string Nome, string Url);
