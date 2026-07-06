using System.Windows.Forms.DataVisualization.Charting;

namespace VerificadorNT.Status;

/// <summary>
/// Painel de acompanhamento permanente do status da Sefaz-BA, no estilo de monitores como
/// o da TecnoSpeed: uma aba por documento (NFe, NFCe, CTe), sempre já filtrado para a
/// Bahia, com um gráfico de linha do tempo de resposta real do webservice (Normal / Lento
/// / Muito lento / Timeout / Erro) amostrado a cada verificação (~1x por minuto), além do
/// status oficial por serviço e dos relatos do Downdetector. Fica disponível para consulta
/// a qualquer momento, com ou sem problema em curso.
/// </summary>
public sealed class StatusDashboardForm : Form
{
    private static readonly Color CorFundo = Color.FromArgb(24, 27, 41);
    private static readonly Color CorFundoGrafico = Color.FromArgb(30, 34, 51);
    private static readonly Color CorGrade = Color.FromArgb(50, 55, 75);
    private static readonly Color CorTextoClaro = Color.FromArgb(225, 228, 235);
    private static readonly Color CorTextoApagado = Color.FromArgb(150, 155, 175);
    private static readonly Color CorDestaque = Color.FromArgb(150, 210, 60);

    private readonly StatusMonitor _monitor;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _carregando;

    private Label _lblUltimaVerificacao = null!;
    private Panel _bolinhaGeral = null!;
    private Label _lblResumoGeral = null!;
    private TabControl _abas = null!;
    private readonly Dictionary<string, ListView> _listasPorDocumento = new();
    private readonly Dictionary<string, Chart> _graficosPorDocumento = new();
    private readonly Dictionary<string, List<(DateTime Hora, RespostaTempo Resposta)>> _historicoPorDocumento = new();
    private const int MaxPontosHistorico = 120; // até 2h de histórico, a 1 ponto/minuto

    private Label _lblDowndetectorResumo = null!;
    private Label _lblComentariosTitulo = null!;
    private ListBox _listaComentariosBahia = null!;
    private Button _btnAtualizar = null!;

    public StatusDashboardForm(StatusMonitor monitor)
    {
        _monitor = monitor;

        Text = "Coala Verifica - Status Sefaz (Bahia)";
        Size = new Size(820, 920);
        MinimumSize = new Size(720, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;

        ConstruirUi();

        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += async (_, _) => await AtualizarAsync();
        FormClosed += (_, _) => _timer.Stop();

        Shown += async (_, _) =>
        {
            _timer.Start();
            await AtualizarAsync();
        };
    }

    private void ConstruirUi()
    {
        var painelTopo = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(20, 16, 20, 8) };

        _bolinhaGeral = new Panel { Size = new Size(20, 20), Location = new Point(20, 20), BackColor = Color.Gray };
        _bolinhaGeral.Paint += (_, e) =>
        {
            using var b = new SolidBrush(_bolinhaGeral.BackColor);
            e.Graphics.FillEllipse(b, 0, 0, 19, 19);
        };

        var lblTitulo = new Label
        {
            Text = "Monitor Sefaz — Bahia", Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = true, Location = new Point(50, 12),
        };

        _lblResumoGeral = new Label
        {
            Text = "Verificando...", AutoSize = true, Location = new Point(50, 42),
            MaximumSize = new Size(560, 0), Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(80, 80, 80),
        };

        _lblUltimaVerificacao = new Label
        {
            Text = "", AutoSize = true, Location = new Point(50, 64),
            Font = new Font("Segoe UI", 8.5f), ForeColor = Color.Gray,
        };

        _btnAtualizar = new Button
        {
            Text = "Verificar agora", Size = new Size(130, 32), Location = new Point(660, 20), FlatStyle = FlatStyle.Flat,
        };
        _btnAtualizar.Click += async (_, _) => await AtualizarAsync();

        painelTopo.Controls.Add(_bolinhaGeral);
        painelTopo.Controls.Add(lblTitulo);
        painelTopo.Controls.Add(_lblResumoGeral);
        painelTopo.Controls.Add(_lblUltimaVerificacao);
        painelTopo.Controls.Add(_btnAtualizar);

        _abas = new TabControl { Dock = DockStyle.Top, Height = 560 };
        foreach (var nomeDocumento in new[] { "NFe", "NFCe", "CTe" })
        {
            var pagina = new TabPage(nomeDocumento) { Size = new Size(780, 528), BackColor = CorFundo };

            var lista = new ListView
            {
                Dock = DockStyle.Top, Height = 150, View = View.Details, FullRowSelect = true,
                GridLines = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            lista.Columns.Add("Serviço (dados oficiais nfe/cte.fazenda.gov.br)", 460);
            lista.Columns.Add("Status", 160);

            var lblGrafico = new Label
            {
                Text = "Tempo de resposta do webservice (amostrado a cada verificação)",
                Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 6, 0, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = CorTextoClaro, BackColor = CorFundo,
            };

            var grafico = ConstruirGraficoResposta();

            pagina.Controls.Add(grafico);
            pagina.Controls.Add(lblGrafico);
            pagina.Controls.Add(lista);
            _abas.TabPages.Add(pagina);
            _listasPorDocumento[nomeDocumento] = lista;
            _graficosPorDocumento[nomeDocumento] = grafico;
            _historicoPorDocumento[nomeDocumento] = [];
        }

        var lblSecaoDowndetector = new Label
        {
            Text = "Downdetector — relatos de usuários (visão geral do Sefaz, não específica por UF)",
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(20, 10, 20, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };

        _lblDowndetectorResumo = new Label
        {
            Text = "", Dock = DockStyle.Top, Height = 60, Padding = new Padding(20, 4, 20, 0),
            Font = new Font("Segoe UI", 9.5f),
        };

        _lblComentariosTitulo = new Label
        {
            Text = "Comentários recentes mencionando a Bahia", Dock = DockStyle.Top, Height = 24,
            Padding = new Padding(20, 6, 0, 0), Font = new Font("Segoe UI", 9f, FontStyle.Bold | FontStyle.Italic),
            ForeColor = Color.FromArgb(180, 90, 0), Visible = false,
        };

        _listaComentariosBahia = new ListBox
        {
            Dock = DockStyle.Fill, Margin = new Padding(20, 0, 20, 12), BorderStyle = BorderStyle.FixedSingle, Visible = false,
        };

        var painelInferior = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 16) };
        painelInferior.Controls.Add(_listaComentariosBahia);

        Controls.Add(painelInferior);
        Controls.Add(_listaComentariosBahia);
        Controls.Add(_lblComentariosTitulo);
        Controls.Add(_lblDowndetectorResumo);
        Controls.Add(lblSecaoDowndetector);
        Controls.Add(_abas);
        Controls.Add(painelTopo);
    }

    private Chart ConstruirGraficoResposta()
    {
        var chart = new Chart { Dock = DockStyle.Fill, BackColor = CorFundoGrafico };

        var area = new ChartArea("area")
        {
            BackColor = CorFundoGrafico,
        };
        area.AxisY.Minimum = 0.5;
        area.AxisY.Maximum = 5.5;
        area.AxisY.Interval = 1;
        area.AxisY.CustomLabels.Add(0.5, 1.5, "Normal: <= 2s");
        area.AxisY.CustomLabels.Add(1.5, 2.5, "Lento: <= 5s");
        area.AxisY.CustomLabels.Add(2.5, 3.5, "Muito lento: < 30s");
        area.AxisY.CustomLabels.Add(3.5, 4.5, "Timeout: > 30s");
        area.AxisY.CustomLabels.Add(4.5, 5.5, "Erro");
        area.AxisY.LabelStyle.ForeColor = CorTextoApagado;
        area.AxisY.LineColor = CorGrade;
        area.AxisY.MajorGrid.LineColor = CorGrade;
        area.AxisY.MajorTickMark.Enabled = false;

        area.AxisX.LabelStyle.Format = "HH:mm";
        area.AxisX.LabelStyle.ForeColor = CorTextoApagado;
        area.AxisX.LineColor = CorGrade;
        area.AxisX.MajorGrid.LineColor = CorGrade;

        chart.ChartAreas.Add(area);

        var serie = new Series("resposta")
        {
            ChartType = SeriesChartType.Line,
            XValueType = ChartValueType.DateTime,
            BorderWidth = 3,
            Color = CorDestaque,
            MarkerStyle = MarkerStyle.Circle,
            MarkerSize = 7,
        };
        chart.Series.Add(serie);

        return chart;
    }

    private static double NivelParaEixoY(NivelResposta nivel) => nivel switch
    {
        NivelResposta.Normal => 1,
        NivelResposta.Lento => 2,
        NivelResposta.MuitoLento => 3,
        NivelResposta.Timeout => 4,
        _ => 5,
    };

    private static Color CorDoNivel(NivelResposta nivel) => nivel switch
    {
        NivelResposta.Normal => Color.FromArgb(90, 190, 250),
        NivelResposta.Lento => Color.FromArgb(230, 200, 60),
        NivelResposta.MuitoLento => Color.FromArgb(230, 140, 40),
        NivelResposta.Timeout => Color.FromArgb(220, 70, 70),
        _ => Color.FromArgb(150, 50, 160),
    };

    private void AtualizarGraficoResposta(string nomeDocumento, RespostaTempo resposta, DateTime horario)
    {
        var historico = _historicoPorDocumento[nomeDocumento];
        historico.Add((horario, resposta));
        if (historico.Count > MaxPontosHistorico) historico.RemoveAt(0);

        var serie = _graficosPorDocumento[nomeDocumento].Series[0];
        serie.Points.Clear();
        foreach (var (hora, r) in historico)
        {
            var idx = serie.Points.AddXY(hora, NivelParaEixoY(r.Nivel));
            var cor = CorDoNivel(r.Nivel);
            serie.Points[idx].Color = cor;
            serie.Points[idx].MarkerColor = cor;
            serie.Points[idx].ToolTip = $"{hora:HH:mm} — {r.Detalhe}";
        }
    }

    private async Task AtualizarAsync()
    {
        if (_carregando) return;
        _carregando = true;
        _btnAtualizar.Enabled = false;
        _btnAtualizar.Text = "Verificando...";

        try
        {
            var resultado = await _monitor.VerificarAsync();
            PreencherTela(resultado);
        }
        catch (Exception ex)
        {
            _lblResumoGeral.Text = $"Não foi possível verificar agora: {ex.Message}";
            _lblResumoGeral.ForeColor = Color.DarkRed;
            _bolinhaGeral.BackColor = Color.Gray;
            _bolinhaGeral.Invalidate();
        }
        finally
        {
            _btnAtualizar.Enabled = true;
            _btnAtualizar.Text = "Verificar agora";
            _carregando = false;
        }
    }

    private static readonly Dictionary<string, string> DocumentoOficialPorAba = new()
    {
        ["NFe"] = "NFe / NFCe",
        ["NFCe"] = "NFe / NFCe",
        ["CTe"] = "CTe",
    };

    private void PreencherTela(StatusMonitorResultado r)
    {
        _lblUltimaVerificacao.Text = $"Última verificação: {r.VerificadoEmUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}";

        foreach (var (nomeAba, lista) in _listasPorDocumento)
        {
            lista.Items.Clear();
            var nomeOficial = DocumentoOficialPorAba[nomeAba];
            var disp = r.Disponibilidades.FirstOrDefault(d => d.Documento == nomeOficial);
            if (disp is null)
            {
                var item = new ListViewItem("Não foi possível consultar o portal oficial agora");
                item.SubItems.Add("-");
                lista.Items.Add(item);
            }
            else
            {
                foreach (var s in disp.Servicos)
                {
                    var item = new ListViewItem(s.Nome);
                    var (texto, cor) = s.Cor switch
                    {
                        "verde" => ("OK", Color.FromArgb(30, 140, 60)),
                        "amarela" => ("Instável", Color.FromArgb(200, 140, 0)),
                        _ => ("Falha", Color.FromArgb(200, 30, 30)),
                    };
                    var sub = new ListViewItem.ListViewSubItem(item, texto) { ForeColor = cor, Font = new Font(Font, FontStyle.Bold) };
                    item.SubItems.Add(sub);
                    lista.Items.Add(item);
                }
            }

            var tempoResposta = r.TemposResposta.FirstOrDefault(t => t.Documento == nomeAba);
            if (tempoResposta is not null)
                AtualizarGraficoResposta(nomeAba, tempoResposta.Resposta, r.VerificadoEmUtc.ToLocalTime());
        }

        var dd = r.Downdetector;
        _lblDowndetectorResumo.Text = dd.Resumo;

        if (dd.ComentariosSobreBahia.Count > 0)
        {
            _lblComentariosTitulo.Visible = true;
            _listaComentariosBahia.Visible = true;
            _listaComentariosBahia.Items.Clear();
            foreach (var c in dd.ComentariosSobreBahia) _listaComentariosBahia.Items.Add(c);
        }
        else
        {
            _lblComentariosTitulo.Visible = false;
            _listaComentariosBahia.Visible = false;
        }

        if (r.AlertaBahia)
        {
            _bolinhaGeral.BackColor = Color.FromArgb(214, 40, 40);
            _lblResumoGeral.Text = "Instabilidade detectada para a Bahia — veja as abas de cada documento abaixo.";
            _lblResumoGeral.ForeColor = Color.FromArgb(180, 30, 30);
        }
        else
        {
            _bolinhaGeral.BackColor = Color.FromArgb(30, 140, 60);
            _lblResumoGeral.Text = "Tudo normal — nenhuma instabilidade detectada para a Bahia no momento.";
            _lblResumoGeral.ForeColor = Color.FromArgb(40, 100, 50);
        }
        _bolinhaGeral.Invalidate();
    }
}
