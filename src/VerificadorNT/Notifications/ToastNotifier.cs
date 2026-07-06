using Microsoft.Toolkit.Uwp.Notifications;
using VerificadorNT.Sources;

namespace VerificadorNT.Notifications;

public static class ToastNotifier
{
    public static void Notificar(NotaTecnica nota)
    {
        var builder = new ToastContentBuilder()
            .AddText($"Nova nota técnica: {nota.Fonte}")
            .AddText(nota.Titulo);

        if (!string.IsNullOrWhiteSpace(nota.Descricao))
            builder.AddText(nota.Descricao);

        if (!string.IsNullOrWhiteSpace(nota.DataPublicacao))
            builder.AddText($"Publicada em {nota.DataPublicacao}");

        builder.AddArgument("url", nota.Url);
        builder.Show();
    }

    public static void NotificarResumo(int quantidade)
    {
        new ToastContentBuilder()
            .AddText("Coala Verifica")
            .AddText($"{quantidade} nova(s) nota(s) técnica(s) encontradas. Veja o histórico na bandeja do sistema.")
            .Show();
    }

    public static void NotificarErro(string mensagem)
    {
        new ToastContentBuilder()
            .AddText("Coala Verifica - erro na verificação")
            .AddText(mensagem)
            .Show();
    }
}
