using System.Globalization;
using VerificadorNT.Storage;

namespace VerificadorNT.Forms;

public sealed class HistoricoForm : Form
{
    private readonly List<HistoricoItem> _itens;
    private ListView _lista = null!;

    private ComboBox _cmbFonte = null!;
    private TextBox _txtTitulo = null!;
    private TextBox _txtAssunto = null!;
    private ComboBox _cmbCampoData = null!;
    private DateTimePicker _dtDe = null!;
    private DateTimePicker _dtAte = null!;
    private Label _lblContagem = null!;

    public HistoricoForm(IReadOnlyList<HistoricoItem> itens)
    {
        _itens = itens.ToList();

        Text = "Coala Verifica - Histórico de Notas Técnicas";
        Width = 1280;
        Height = 660;
        MinimumSize = new Size(940, 500);
        StartPosition = FormStartPosition.CenterScreen;

        var painelFiltros = ConstruirPainelFiltros();

        _lista = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
        };
        _lista.Columns.Add("Fonte", 130);
        _lista.Columns.Add("Título", 300);
        _lista.Columns.Add("Do que se trata", 380);
        _lista.Columns.Add("Publicada em", 90);
        _lista.Columns.Add("Detectada em", 120);
        _lista.DoubleClick += (_, _) => AbrirSelecionado();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir link", null, (_, _) => AbrirSelecionado());
        _lista.ContextMenuStrip = menu;

        Controls.Add(_lista);
        Controls.Add(painelFiltros);

        AplicarFiltros();
    }

    private Panel ConstruirPainelFiltros()
    {
        var painel = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(12, 8, 12, 4) };

        var fontes = new[] { "Todas as fontes" }
            .Concat(_itens.Select(i => i.Fonte).Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var lblFonte = new Label { Text = "Fonte", Location = new Point(12, 6), AutoSize = true };
        _cmbFonte = new ComboBox
        {
            Location = new Point(12, 24), Width = 170, DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cmbFonte.Items.AddRange(fontes);
        _cmbFonte.SelectedIndex = 0;

        var lblTitulo = new Label { Text = "Título contém", Location = new Point(196, 6), AutoSize = true };
        _txtTitulo = new TextBox { Location = new Point(196, 24), Width = 220 };

        var lblAssunto = new Label { Text = "Do que se trata contém", Location = new Point(430, 6), AutoSize = true };
        _txtAssunto = new TextBox { Location = new Point(430, 24), Width = 260 };

        var lblCampoData = new Label { Text = "Filtrar data por", Location = new Point(704, 6), AutoSize = true };
        _cmbCampoData = new ComboBox
        {
            Location = new Point(704, 24), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cmbCampoData.Items.AddRange(["Data detectada", "Data publicada"]);
        _cmbCampoData.SelectedIndex = 0;

        var lblDe = new Label { Text = "De:", Location = new Point(12, 62), AutoSize = true };
        _dtDe = new DateTimePicker
        {
            Location = new Point(38, 58), Width = 140,
            Format = DateTimePickerFormat.Short,
            MinDate = new DateTime(2000, 1, 1),
            Value = new DateTime(2000, 1, 1),
        };

        var lblAte = new Label { Text = "Até:", Location = new Point(190, 62), AutoSize = true };
        _dtAte = new DateTimePicker
        {
            Location = new Point(220, 58), Width = 140,
            Format = DateTimePickerFormat.Short,
            MaxDate = DateTime.Today.AddDays(1),
            Value = DateTime.Today.AddDays(1),
        };

        var btnLimpar = new Button
        {
            Text = "Limpar filtros", Location = new Point(374, 57), Width = 120, Height = 26, FlatStyle = FlatStyle.Flat,
        };
        btnLimpar.Click += (_, _) => LimparFiltros();

        _lblContagem = new Label
        {
            Text = "", Location = new Point(704, 62), AutoSize = true, ForeColor = Color.Gray,
        };

        painel.Controls.AddRange([
            lblFonte, _cmbFonte, lblTitulo, _txtTitulo, lblAssunto, _txtAssunto, lblCampoData, _cmbCampoData,
            lblDe, _dtDe, lblAte, _dtAte, btnLimpar, _lblContagem,
        ]);

        _cmbFonte.SelectedIndexChanged += (_, _) => AplicarFiltros();
        _txtTitulo.TextChanged += (_, _) => AplicarFiltros();
        _txtAssunto.TextChanged += (_, _) => AplicarFiltros();
        _cmbCampoData.SelectedIndexChanged += (_, _) => AplicarFiltros();
        _dtDe.ValueChanged += (_, _) => AplicarFiltros();
        _dtAte.ValueChanged += (_, _) => AplicarFiltros();

        return painel;
    }

    private void LimparFiltros()
    {
        _cmbFonte.SelectedIndex = 0;
        _txtTitulo.Clear();
        _txtAssunto.Clear();
        _cmbCampoData.SelectedIndex = 0;
        _dtDe.Value = new DateTime(2000, 1, 1);
        _dtAte.Value = DateTime.Today.AddDays(1);
        AplicarFiltros();
    }

    private void AplicarFiltros()
    {
        if (_lista is null) return; // ainda em construção

        IEnumerable<HistoricoItem> filtrados = _itens;

        if (_cmbFonte.SelectedIndex > 0)
        {
            var fonte = (string)_cmbFonte.SelectedItem!;
            filtrados = filtrados.Where(i => i.Fonte == fonte);
        }

        if (!string.IsNullOrWhiteSpace(_txtTitulo.Text))
            filtrados = filtrados.Where(i => i.Titulo.Contains(_txtTitulo.Text, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_txtAssunto.Text))
            filtrados = filtrados.Where(i => (i.Descricao ?? "").Contains(_txtAssunto.Text, StringComparison.OrdinalIgnoreCase));

        var usarDataPublicada = _cmbCampoData.SelectedIndex == 1;
        var de = _dtDe.Value.Date;
        var ate = _dtAte.Value.Date;
        filtrados = filtrados.Where(i => ObterData(i, usarDataPublicada) is { } d && d.Date >= de && d.Date <= ate);

        var lista = filtrados.ToList();
        PreencherLista(lista);
        _lblContagem.Text = $"{lista.Count} de {_itens.Count} item(ns)";
    }

    private static DateTime? ObterData(HistoricoItem item, bool usarDataPublicada)
    {
        if (usarDataPublicada)
        {
            if (string.IsNullOrWhiteSpace(item.DataPublicacao)) return null;
            return DateTime.TryParseExact(item.DataPublicacao.Trim(), "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : null;
        }

        return item.DetectadoEmUtc.ToLocalTime();
    }

    private void PreencherLista(List<HistoricoItem> itens)
    {
        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var item in itens)
        {
            var linha = new ListViewItem(item.Fonte);
            linha.SubItems.Add(item.Titulo);
            linha.SubItems.Add(item.Descricao ?? "-");
            linha.SubItems.Add(item.DataPublicacao ?? "-");
            linha.SubItems.Add(item.DetectadoEmUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            linha.Tag = item.Url;
            _lista.Items.Add(linha);
        }

        _lista.EndUpdate();
    }

    private void AbrirSelecionado()
    {
        if (_lista.SelectedItems.Count == 0) return;
        var url = _lista.SelectedItems[0].Tag as string;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignora falha ao abrir navegador
        }
    }
}
