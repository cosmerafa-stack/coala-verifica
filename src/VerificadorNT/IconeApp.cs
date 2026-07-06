namespace VerificadorNT;

internal static class IconeApp
{
    /// <summary>Usa o mesmo ícone embutido no .exe (definido via ApplicationIcon no
    /// csproj), para que a bandeja do sistema mostre exatamente o mesmo ícone "NT"
    /// visto no executável, atalhos e Alt-Tab.</summary>
    public static Icon Criar()
    {
        var doExecutavel = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        return doExecutavel ?? SystemIcons.Application;
    }
}
