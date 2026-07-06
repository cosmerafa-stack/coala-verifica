using VerificadorNT.Config;
using VerificadorNT.Forms;
using VerificadorNT.Http;
using VerificadorNT.Notifications;
using VerificadorNT.Status;
using VerificadorNT.Storage;

namespace VerificadorNT;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppConfig _config;
    private readonly HistoryStore _historico;
    private readonly CheckService _servico;
    private readonly StatusMonitor _statusMonitor;
    private System.Windows.Forms.Timer? _timer;
    private System.Windows.Forms.Timer? _statusTimer;
    private bool _verificando;
    private bool _verificandoStatus;
    private StatusOverlayForm? _overlayAberto;
    private StatusDashboardForm? _dashboard;

    public TrayApplicationContext()
    {
        _config = AppConfig.CarregarOuCriar();
        _historico = HistoryStore.CarregarOuCriar();
        _servico = new CheckService(_config, _historico);
        _servico.Log += msg => System.Diagnostics.Debug.WriteLine($"[VerificadorNT] {msg}");

        _statusMonitor = new StatusMonitor(SecureHttpClientFactory.Criar());
        _statusMonitor.Log += msg => System.Diagnostics.Debug.WriteLine($"[VerificadorNT/Status] {msg}");

        StartupManager.Aplicar(_config.IniciarComWindows);

        _trayIcon = new NotifyIcon
        {
            Icon = IconeApp.Criar(),
            Text = "Coala Verifica",
            Visible = true,
            ContextMenuStrip = MontarMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => AbrirHistorico();

        ConfigurarTimer();
        ConfigurarStatusTimer();

        _ = ExecutarVerificacaoAsync(mostrarSemNovidade: false);
        _ = ExecutarVerificacaoStatusAsync(mostrarSemProblema: false);
    }

    private ContextMenuStrip MontarMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Verificar agora", null, async (_, _) => await ExecutarVerificacaoAsync(mostrarSemNovidade: true));
        menu.Items.Add("Ver histórico", null, (_, _) => AbrirHistorico());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Status Sefaz (Bahia) - abrir painel", null, (_, _) => AbrirDashboard());
        menu.Items.Add(new ToolStripSeparator());

        var intervaloMenu = new ToolStripMenuItem("Intervalo de verificação (notas técnicas)");
        foreach (var horas in new[] { 1, 6, 12, 24 })
        {
            var item = new ToolStripMenuItem($"A cada {horas}h") { Checked = _config.IntervaloHoras == horas };
            item.Click += (_, _) =>
            {
                _config.IntervaloHoras = horas;
                _config.Salvar();
                ConfigurarTimer();
                foreach (ToolStripMenuItem irmao in intervaloMenu.DropDownItems)
                    irmao.Checked = irmao == item;
            };
            intervaloMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(intervaloMenu);

        var intervaloStatusMenu = new ToolStripMenuItem("Intervalo de verificação (status Sefaz)");
        foreach (var minutos in new[] { 5, 15, 30, 60 })
        {
            var item = new ToolStripMenuItem($"A cada {minutos}min") { Checked = _config.IntervaloStatusMinutos == minutos };
            item.Click += (_, _) =>
            {
                _config.IntervaloStatusMinutos = minutos;
                _config.Salvar();
                ConfigurarStatusTimer();
                foreach (ToolStripMenuItem irmao in intervaloStatusMenu.DropDownItems)
                    irmao.Checked = irmao == item;
            };
            intervaloStatusMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(intervaloStatusMenu);

        var iniciarComWindows = new ToolStripMenuItem("Iniciar com o Windows") { Checked = _config.IniciarComWindows };
        iniciarComWindows.Click += (_, _) =>
        {
            _config.IniciarComWindows = !_config.IniciarComWindows;
            iniciarComWindows.Checked = _config.IniciarComWindows;
            _config.Salvar();
            StartupManager.Aplicar(_config.IniciarComWindows);
        };
        menu.Items.Add(iniciarComWindows);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Encerrar());

        return menu;
    }

    private void ConfigurarTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromHours(_config.IntervaloHoras).TotalMilliseconds,
        };
        _timer.Tick += async (_, _) => await ExecutarVerificacaoAsync(mostrarSemNovidade: false);
        _timer.Start();
    }

    private void ConfigurarStatusTimer()
    {
        _statusTimer?.Stop();
        _statusTimer?.Dispose();
        _statusTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromMinutes(_config.IntervaloStatusMinutos).TotalMilliseconds,
        };
        _statusTimer.Tick += async (_, _) => await ExecutarVerificacaoStatusAsync(mostrarSemProblema: false);
        _statusTimer.Start();
    }

    private async Task ExecutarVerificacaoAsync(bool mostrarSemNovidade)
    {
        if (_verificando) return;
        _verificando = true;
        try
        {
            _trayIcon.Text = "Coala Verifica - verificando...";
            var novas = await _servico.VerificarAsync();

            if (novas.Count > 0)
                ToastNotifier.NotificarResumo(novas.Count);
            else if (mostrarSemNovidade)
                _trayIcon.ShowBalloonTip(4000, "Coala Verifica",
                    "Nenhuma nota técnica nova encontrada.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ToastNotifier.NotificarErro(ex.Message);
        }
        finally
        {
            _trayIcon.Text = "Coala Verifica";
            _verificando = false;
        }
    }

    private async Task ExecutarVerificacaoStatusAsync(bool mostrarSemProblema)
    {
        if (_verificandoStatus) return;
        _verificandoStatus = true;
        try
        {
            var resultado = await _statusMonitor.VerificarAsync();
            var chaveAtual = resultado.ChaveDoProblema();

            if (resultado.AlertaBahia)
            {
                if (mostrarSemProblema || chaveAtual != _config.UltimaChaveAlertaBahia)
                {
                    MostrarOverlay(resultado);
                }
                _config.UltimaChaveAlertaBahia = chaveAtual;
            }
            else
            {
                _config.UltimaChaveAlertaBahia = "";
                if (mostrarSemProblema)
                {
                    _trayIcon.ShowBalloonTip(5000, "Status Sefaz - Bahia",
                        "Sem intermitência detectada no momento (disponibilidade oficial OK; " +
                        $"Downdetector: {resultado.Downdetector.Resumo}).", ToolTipIcon.Info);
                }
            }

            _config.Salvar();
        }
        catch (Exception ex)
        {
            if (mostrarSemProblema)
                _trayIcon.ShowBalloonTip(5000, "Status Sefaz - Bahia",
                    $"Não foi possível verificar agora: {ex.Message}", ToolTipIcon.Warning);
        }
        finally
        {
            _verificandoStatus = false;
        }
    }

    private void MostrarOverlay(StatusMonitorResultado resultado)
    {
        _overlayAberto?.Close();

        var critico = resultado.Disponibilidades.Any(d => d.ServicosComProblema.Any(s => s.Cor == "vermelho"));
        var severidade = critico ? SeveridadeAlerta.Critico : SeveridadeAlerta.Atencao;

        var detalhes = new List<string>();
        foreach (var disp in resultado.Disponibilidades)
        {
            foreach (var s in disp.ServicosComProblema)
                detalhes.Add($"[Oficial - {disp.Documento}] {s.Nome}: {(s.Cor == "vermelho" ? "FALHA" : "instável")}");
        }
        foreach (var c in resultado.Downdetector.ComentariosSobreBahia)
            detalhes.Add($"[Downdetector - comentário de usuário] \"{c}\"");

        if (detalhes.Count == 0)
            detalhes.Add("Verifique o Downdetector e o portal oficial para mais detalhes.");

        var mensagem = resultado.Disponibilidades.Any(d => d.TemProblema)
            ? "O monitor oficial de disponibilidade da SEFAZ está reportando problema nos webservices da Bahia."
            : "Usuários estão relatando problemas com a Sefaz mencionando a Bahia no Downdetector.";

        _overlayAberto = new StatusOverlayForm(
            "Instabilidade detectada — SEFAZ Bahia", mensagem, severidade, detalhes);
        _overlayAberto.FormClosed += (_, _) => _overlayAberto = null;
        _overlayAberto.Show();
    }

    private void AbrirHistorico()
    {
        using var form = new HistoricoForm(_historico.ObterHistorico());
        form.ShowDialog();
    }

    private void AbrirDashboard()
    {
        if (_dashboard is { IsDisposed: false })
        {
            _dashboard.Activate();
            _dashboard.WindowState = FormWindowState.Normal;
            return;
        }

        _dashboard = new StatusDashboardForm(_statusMonitor);
        _dashboard.FormClosed += (_, _) => _dashboard = null;
        _dashboard.Show();
    }

    private void Encerrar()
    {
        _trayIcon.Visible = false;
        _timer?.Stop();
        _statusTimer?.Stop();
        Application.Exit();
    }
}
