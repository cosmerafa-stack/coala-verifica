using VerificadorNT;
using VerificadorNT.Forms;
using VerificadorNT.Http;
using VerificadorNT.Status;
using VerificadorNT.Storage;

if (args.Contains("--test-status"))
{
    var crashPath = Path.Combine(Path.GetTempPath(), "verificadornt-teste-status-crash.txt");
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    Application.ThreadException += (_, e) => File.WriteAllText(crashPath, "ThreadException: " + e.Exception);
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        File.WriteAllText(crashPath, "UnhandledException: " + e.ExceptionObject);

    ApplicationConfiguration.Initialize();
    using var bootstrap = new Form { WindowState = FormWindowState.Minimized, ShowInTaskbar = false };
    bootstrap.Load += async (_, _) =>
    {
        try
        {
            await TesteStatus.ExecutarAsync();
        }
        catch (Exception ex)
        {
            File.WriteAllText(crashPath, "ExecutarAsync throw: " + ex);
        }
        finally
        {
            Application.Exit();
        }
    };
    Application.Run(bootstrap);
    return;
}

if (args.Contains("--test-overlay"))
{
    ApplicationConfiguration.Initialize();
    var overlay = new StatusOverlayForm(
        "Instabilidade detectada — SEFAZ Bahia",
        "O monitor oficial de disponibilidade da SEFAZ está reportando problema nos webservices da Bahia.",
        SeveridadeAlerta.Critico,
        [
            "[Oficial - nfe.fazenda.gov.br] Autorização: FALHA",
            "[Oficial - nfe.fazenda.gov.br] Retorno Autorização: instável",
            "[Downdetector - comentário de usuário] \"Estamos Enfrentando Problemas no SEFAZ aqui na Bahia também.\"",
        ]);
    Application.Run(overlay);
    return;
}

if (args.Contains("--test-historico"))
{
    ApplicationConfiguration.Initialize();
    var historico = HistoryStore.CarregarOuCriar();
    Application.Run(new HistoricoForm(historico.ObterHistorico()));
    return;
}

if (args.Contains("--test-dashboard"))
{
    var crashPath = Path.Combine(Path.GetTempPath(), "verificadornt-teste-dashboard-crash.txt");
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    Application.ThreadException += (_, e) => File.WriteAllText(crashPath, "ThreadException: " + e.Exception);
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        File.WriteAllText(crashPath, "UnhandledException: " + e.ExceptionObject);

    ApplicationConfiguration.Initialize();
    var monitor = new StatusMonitor(SecureHttpClientFactory.Criar());
    Application.Run(new StatusDashboardForm(monitor));
    return;
}

using var mutex = new Mutex(initiallyOwned: true, "VerificadorNT_InstanciaUnica_9F3B2E", out var criouNovo);

if (!criouNovo)
{
    MessageBox.Show("O Coala Verifica já está em execução (veja a bandeja do sistema).",
        "Coala Verifica", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

var logErro = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoalaVerifica", "erro-fatal.log");
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException += (_, e) => RegistrarErroFatal(e.Exception);
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    RegistrarErroFatal(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

ApplicationConfiguration.Initialize();
Application.Run(new TrayApplicationContext());

void RegistrarErroFatal(Exception ex)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logErro)!);
        File.AppendAllText(logErro, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
    }
    catch
    {
        // se nem isso funcionar, não há mais o que fazer
    }
}
