using VerificadorNT.Config;
using VerificadorNT.Http;
using VerificadorNT.Notifications;
using VerificadorNT.Sources;
using VerificadorNT.Storage;

namespace VerificadorNT;

public sealed class CheckService
{
    public const string PortalNfe = "https://www.nfe.fazenda.gov.br/portal/";
    public const string PortalCte = "https://www.cte.fazenda.gov.br/portal/";

    private readonly AppConfig _config;
    private readonly HistoryStore _historico;
    private readonly HttpClient _http;

    public event Action<string>? Log;

    public CheckService(AppConfig config, HistoryStore historico)
    {
        _config = config;
        _historico = historico;
        _http = SecureHttpClientFactory.Criar();
        SecureHttpClientFactory.Aviso += msg => Log?.Invoke(msg);
    }

    private List<ISourceChecker> ConstruirCheckers()
    {
        var checkers = new List<ISourceChecker>();

        if (_config.NfeNfceAtivo)
            checkers.Add(new FazendaListaConteudoChecker("NFe/NFCe", PortalNfe,
                PortalNfe + "listaConteudo.aspx?tipoConteudo=04BIflQt1aY="));

        if (_config.CteAtivo)
            checkers.Add(new FazendaListaConteudoChecker("CTe", PortalCte,
                PortalCte + "listaConteudo.aspx?tipoConteudo=Y0nErnoZpsg="));

        if (_config.MdfeAtivo)
            checkers.Add(new MdfeSvrsChecker("https://dfe-portal.svrs.rs.gov.br/mdfe/Documentos"));

        if (_config.NfseAtivo)
            checkers.Add(new NfseNacionalChecker("https://www.gov.br/nfse/pt-br/noticias"));

        if (_config.SpedAtivo)
        {
            checkers.Add(new SpedPageWatcher("ECD",
                "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/escrituracao-contabil-digital-ecd/escrituracao-contabil-digital-ecd"));
            checkers.Add(new SpedPageWatcher("ECF",
                "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/escrituracao-contabil-fiscal-ecf/sped-programa-sped-contabil-fiscal"));
            checkers.Add(new SpedPageWatcher("EFD ICMS-IPI",
                "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/escrituracao-fiscal-digital-efd/escrituracao-fiscal-digital-efd"));
            checkers.Add(new SpedPageWatcher("EFD-Contribuições",
                "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/efd-contribuicoes/programa-validador-da-escrituracao-fiscal-digital-das-contribuicoes-incidentes-sobre-a-receita-efd-contribuicoes-2"));
        }

        return checkers;
    }

    /// <summary>
    /// Executa a verificação de todas as fontes ativas. Retorna as notas novas encontradas.
    /// Na primeiríssima execução (histórico vazio), tudo que for encontrado é gravado como
    /// já conhecido, mas nada é notificado — do contrário o usuário levaria uma enxurrada
    /// de alertas com notas técnicas antigas.
    /// </summary>
    public async Task<IReadOnlyList<NotaTecnica>> VerificarAsync(CancellationToken ct = default)
    {
        var primeiraExecucao = _config.UltimaVerificacaoUtc is null;
        var novas = new List<NotaTecnica>();

        foreach (var checker in ConstruirCheckers())
        {
            try
            {
                var notas = await checker.BuscarAsync(_http, ct);
                foreach (var nota in notas)
                {
                    var eraNova = _historico.RegistrarSeNova(nota);
                    if (eraNova && !primeiraExecucao)
                        novas.Add(nota);
                }
                Log?.Invoke($"{checker.Nome}: {notas.Count} item(ns) verificado(s).");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"{checker.Nome}: falha ao verificar ({ex.Message}).");
            }
        }

        _historico.Salvar();
        _config.UltimaVerificacaoUtc = DateTime.UtcNow;
        _config.Salvar();

        foreach (var nota in novas)
            ToastNotifier.Notificar(nota);

        return novas;
    }
}
