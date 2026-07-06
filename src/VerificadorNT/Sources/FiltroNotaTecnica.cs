using System.Text.RegularExpressions;

namespace VerificadorNT.Sources;

/// <summary>
/// Fontes como a de notícias da NFSe Nacional e o portal do MDFe misturam notas técnicas
/// com avisos, notícias e outros documentos. Esse filtro mantém só o que de fato é nota
/// técnica, para não poluir os alertas com conteúdo irrelevante.
/// </summary>
public static partial class FiltroNotaTecnica
{
    public static bool EhNotaTecnica(string titulo) => RegexNotaTecnica().IsMatch(titulo);

    [GeneratedRegex(@"nota\s*t[eé]cnica", RegexOptions.IgnoreCase)]
    private static partial Regex RegexNotaTecnica();
}
