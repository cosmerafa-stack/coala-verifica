using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;

namespace VerificadorNT.Status;

public enum SeveridadeAlerta
{
    Atencao,
    Critico,
}

/// <summary>
/// Janela de alerta que aparece por cima de todos os outros aplicativos quando uma
/// instabilidade é detectada para a Bahia. Não é modal (não trava o uso do computador),
/// mas fica sempre no topo e chama atenção visualmente.
/// </summary>
public sealed class StatusOverlayForm : Form
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public StatusOverlayForm(string titulo, string mensagem, SeveridadeAlerta severidade, IReadOnlyList<string> detalhes)
    {
        var corDestaque = severidade == SeveridadeAlerta.Critico
            ? Color.FromArgb(214, 40, 40)
            : Color.FromArgb(240, 160, 20);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(620, 440);
        BackColor = Color.White;
        TopMost = true;
        ShowInTaskbar = true;
        Text = "Alerta - Coala Verifica";

        var bounds = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(bounds.Left + (bounds.Width - Width) / 2, bounds.Top + (bounds.Height - Height) / 2);

        var faixa = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = corDestaque };

        var painelConteudo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24), BackColor = Color.White };

        var linhaTitulo = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
        };
        var icone = new Label
        {
            Text = "⚠",
            Font = new Font("Segoe UI Emoji", 28, FontStyle.Bold),
            ForeColor = corDestaque,
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0),
        };
        var lblTitulo = new Label
        {
            Text = titulo,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 6, 0, 0),
        };
        linhaTitulo.Controls.Add(icone);
        linhaTitulo.Controls.Add(lblTitulo);

        var lblMensagem = new Label
        {
            Text = mensagem,
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Color.FromArgb(70, 70, 70),
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 60,
            Padding = new Padding(0, 12, 0, 0),
        };

        var listaDetalhes = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 250, 250),
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine + Environment.NewLine, detalhes),
        };

        var painelBotoes = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, AutoSize = false,
        };
        var btnFechar = new Button
        {
            Text = "Fechar", Width = 100, Height = 32, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 240, 240),
        };
        btnFechar.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        btnFechar.Click += (_, _) => Close();
        painelBotoes.Controls.Add(btnFechar);

        painelConteudo.Controls.Add(listaDetalhes);
        painelConteudo.Controls.Add(painelBotoes);
        painelConteudo.Controls.Add(lblMensagem);
        painelConteudo.Controls.Add(linhaTitulo);

        Controls.Add(painelConteudo);
        Controls.Add(faixa);

        Paint += (_, e) => DesenharBorda(e.Graphics, corDestaque);
        Resize += (_, _) => Invalidate();
    }

    private void DesenharBorda(Graphics g, Color cor)
    {
        using var caneta = new Pen(cor, 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawRectangle(caneta, 1, 1, Width - 3, Height - 3);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        SetForegroundWindow(Handle);
        SystemSounds.Exclamation.Play();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            return cp;
        }
    }
}
