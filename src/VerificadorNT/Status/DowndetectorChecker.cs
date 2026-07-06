using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VerificadorNT.Status;

/// <summary>
/// O Downdetector fica atrás de uma proteção Cloudflare com desafio JavaScript, que um
/// HttpClient comum não consegue passar. Por isso usamos um WebView2 (motor Chromium real,
/// via runtime do Edge já instalado no Windows) numa janela fora da área visível da tela
/// para carregar a página como um navegador de verdade faria.
///
/// O WebView2 exige uma thread STA com seu próprio loop de mensagens Windows. Para não
/// disputar o modelo de apartment COM com o resto do app (isso causava
/// RPC_E_CHANGED_MODE), toda a verificação roda numa thread dedicada, criada e destruída
/// a cada checagem.
/// </summary>
public static class DowndetectorChecker
{
    private const string Url = "https://downdetector.com.br/fora-do-ar/sefaz/";

    public static Task<DowndetectorResultado> VerificarAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<DowndetectorResultado>();

        var thread = new Thread(() =>
        {
            try
            {
                using var contexto = new WebViewThreadContext();
                contexto.Executar(Url, ct, tcs);
                System.Windows.Forms.Application.Run(contexto.MensagemLoop);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new DowndetectorResultado
                {
                    ProblemaDetectado = false,
                    Resumo = "Não foi possível verificar o Downdetector agora.",
                    Erro = ex.Message,
                    VerificadoEmUtc = DateTime.UtcNow,
                });
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}

/// <summary>Form invisível cujo único papel é fornecer um loop de mensagens Windows
/// (Application.Run) próprio para a thread STA do WebView2.</summary>
internal sealed partial class WebViewThreadContext : IDisposable
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    public readonly Form MensagemLoop;

    public WebViewThreadContext()
    {
        MensagemLoop = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-32000, -32000),
            Size = new System.Drawing.Size(1366, 900),
        };
        MensagemLoop.Controls.Add(_webView);
    }

    public void Executar(string url, CancellationToken ct, TaskCompletionSource<DowndetectorResultado> tcs)
    {
        MensagemLoop.Shown += async (_, _) =>
        {
            try
            {
                var resultado = await CarregarEExtrairAsync(url, ct);
                tcs.TrySetResult(resultado);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new DowndetectorResultado
                {
                    ProblemaDetectado = false,
                    Resumo = "Não foi possível verificar o Downdetector agora.",
                    Erro = ex.Message,
                    VerificadoEmUtc = DateTime.UtcNow,
                });
            }
            finally
            {
                System.Windows.Forms.Application.ExitThread();
            }
        };
    }

    private async Task<DowndetectorResultado> CarregarEExtrairAsync(string url, CancellationToken ct)
    {
        await _webView.EnsureCoreWebView2Async();

        var carregou = new TaskCompletionSource();
        void OnNavegacaoConcluida(object? s, CoreWebView2NavigationCompletedEventArgs e) =>
            carregou.TrySetResult();
        _webView.CoreWebView2.NavigationCompleted += OnNavegacaoConcluida;

        _webView.CoreWebView2.Navigate(url);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(25));
        await carregou.Task.WaitAsync(cts.Token);
        _webView.CoreWebView2.NavigationCompleted -= OnNavegacaoConcluida;

        // O desafio do Cloudflare ("Just a moment...") resolve sozinho via JS e redireciona;
        // damos uma folga para isso acontecer antes de ler o conteúdo final.
        var texto = await AguardarConteudoFinalAsync(ct);

        if (Environment.GetEnvironmentVariable("VERIFICADORNT_DEBUG_DD") == "1")
        {
            await File.WriteAllTextAsync(
                Path.Combine(Path.GetTempPath(), "verificadornt-downdetector-raw.txt"), texto, ct);
        }

        return InterpretarConteudo(texto);
    }

    private async Task<string> AguardarConteudoFinalAsync(CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < 6; tentativa++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            var titulo = await ExecutarJsStringAsync("document.title");
            if (!titulo.Contains("moment", StringComparison.OrdinalIgnoreCase) &&
                !titulo.Contains("momento", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(titulo))
            {
                break;
            }
        }

        return await ExecutarJsStringAsync("document.body.innerText");
    }

    private async Task<string> ExecutarJsStringAsync(string expressao)
    {
        var json = await _webView.CoreWebView2.ExecuteScriptAsync(expressao);
        return JsonSerializer.Deserialize<string>(json) ?? "";
    }

    private static DowndetectorResultado InterpretarConteudo(string texto)
    {
        var textoNormalizado = texto.Trim();

        if (string.IsNullOrWhiteSpace(textoNormalizado))
        {
            return new DowndetectorResultado
            {
                ProblemaDetectado = false,
                Resumo = "Não foi possível ler o conteúdo da página do Downdetector.",
                VerificadoEmUtc = DateTime.UtcNow,
            };
        }

        var linhas = textoNormalizado
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // A manchete principal é algo como "Relatos dos usuários não mostram problemas
        // atuais com Sefaz" (sem problema) ou, quando há pico de relatos, uma frase
        // equivalente sem a negação "não".
        var manchete = linhas.FirstOrDefault(l =>
            l.Contains("com Sefaz", StringComparison.OrdinalIgnoreCase) &&
            l.Contains("problema", StringComparison.OrdinalIgnoreCase));

        var problema = manchete is not null && !manchete.Contains("não", StringComparison.OrdinalIgnoreCase);

        var falhasMaisRelatadas = ExtrairFalhasMaisRelatadas(linhas);
        var comentariosBahia = ExtrairComentariosSobreBahia(textoNormalizado);

        var resumo = manchete ?? "Página carregada, mas não foi possível identificar a manchete de status.";
        if (falhasMaisRelatadas.Count > 0)
            resumo += " — Falhas mais relatadas: " + string.Join(", ", falhasMaisRelatadas);

        return new DowndetectorResultado
        {
            ProblemaDetectado = problema,
            Resumo = resumo,
            FalhasMaisRelatadas = falhasMaisRelatadas,
            ComentariosSobreBahia = comentariosBahia,
            VerificadoEmUtc = DateTime.UtcNow,
        };
    }

    /// <summary>Extrai o bloco "Falhas mais relatadas" (ex.: "58% Website", "19% Atualizações").</summary>
    private static List<string> ExtrairFalhasMaisRelatadas(List<string> linhas)
    {
        var idx = linhas.FindIndex(l => l.Contains("Falhas mais relatadas", StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return [];

        var resultado = new List<string>();
        for (var i = idx + 1; i + 1 < linhas.Count && resultado.Count < 3; i += 2)
        {
            if (!linhas[i].EndsWith('%')) break;
            resultado.Add($"{linhas[i + 1]} ({linhas[i]})");
        }

        return resultado;
    }

    /// <summary>Downdetector agrega relatos num placar geral, mas comentários individuais de
    /// usuários costumam citar o estado antes disso virar um pico visível — por isso
    /// procuramos menções à Bahia diretamente nos comentários recentes, como sinal antecipado.</summary>
    private static List<string> ExtrairComentariosSobreBahia(string texto)
    {
        var comentarios = RegexComentario().Matches(texto).Select(m => m.Groups[1].Value.Trim());

        return comentarios
            .Where(c => c.Contains("bahia", StringComparison.OrdinalIgnoreCase) || RegexSiglaBa().IsMatch(c))
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"^há .+\r?\n(.+)$", RegexOptions.Multiline)]
    private static partial Regex RegexComentario();

    [GeneratedRegex(@"\bBA\b")]
    private static partial Regex RegexSiglaBa();

    public void Dispose()
    {
        _webView.Dispose();
        MensagemLoop.Dispose();
    }
}
