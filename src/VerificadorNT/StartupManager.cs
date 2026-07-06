using Microsoft.Win32;

namespace VerificadorNT;

internal static class StartupManager
{
    private const string ChaveExecucao = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomeValor = "CoalaVerifica";
    private const string NomeValorAntigo = "VerificadorNT";

    public static void Aplicar(bool iniciarComWindows)
    {
        using var chave = Registry.CurrentUser.OpenSubKey(ChaveExecucao, writable: true);
        if (chave is null) return;

        chave.DeleteValue(NomeValorAntigo, throwOnMissingValue: false);

        if (iniciarComWindows)
        {
            var caminhoExe = Environment.ProcessPath ?? Application.ExecutablePath;
            chave.SetValue(NomeValor, $"\"{caminhoExe}\"");
        }
        else
        {
            chave.DeleteValue(NomeValor, throwOnMissingValue: false);
        }
    }
}
