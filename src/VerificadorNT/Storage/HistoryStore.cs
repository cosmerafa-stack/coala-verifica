using System.Text.Json;
using VerificadorNT.Sources;

namespace VerificadorNT.Storage;

public sealed class HistoricoItem
{
    public required string Fonte { get; init; }
    public required string Titulo { get; init; }
    public string? Descricao { get; init; }
    public string? DataPublicacao { get; init; }
    public required string Url { get; init; }
    public DateTime DetectadoEmUtc { get; init; }
}

public sealed class HistoryStore
{
    private const int MaxItensHistorico = 1000;

    private static readonly string PastaDados =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoalaVerifica");

    private static readonly string CaminhoArquivo = Path.Combine(PastaDados, "historico.json");

    private readonly object _lock = new();
    private HashSet<string> _chavesVistas = new(StringComparer.Ordinal);
    private List<HistoricoItem> _itens = new();

    public static HistoryStore CarregarOuCriar()
    {
        var store = new HistoryStore();
        try
        {
            if (File.Exists(CaminhoArquivo))
            {
                var json = File.ReadAllText(CaminhoArquivo);
                var dados = JsonSerializer.Deserialize<List<HistoricoItem>>(json);
                if (dados is not null)
                {
                    store._itens = dados;
                    store._chavesVistas = dados
                        .Select(i => ChaveDe(i.Fonte, i.Titulo, i.Url))
                        .ToHashSet(StringComparer.Ordinal);
                }
            }
        }
        catch
        {
            // histórico corrompido: recomeça vazio
        }

        return store;
    }

    private static string ChaveDe(string fonte, string titulo, string url) =>
        $"{fonte}|{titulo}|{url}".ToLowerInvariant();

    public bool JaConhecida(NotaTecnica nota)
    {
        lock (_lock)
        {
            return _chavesVistas.Contains(nota.Chave);
        }
    }

    /// <summary>Registra a nota como vista e retorna true se ela era nova (ainda não conhecida).</summary>
    public bool RegistrarSeNova(NotaTecnica nota)
    {
        lock (_lock)
        {
            if (!_chavesVistas.Add(nota.Chave)) return false;

            _itens.Insert(0, new HistoricoItem
            {
                Fonte = nota.Fonte,
                Titulo = nota.Titulo,
                Descricao = nota.Descricao,
                DataPublicacao = nota.DataPublicacao,
                Url = nota.Url,
                DetectadoEmUtc = DateTime.UtcNow,
            });

            if (_itens.Count > MaxItensHistorico)
                _itens.RemoveRange(MaxItensHistorico, _itens.Count - MaxItensHistorico);

            return true;
        }
    }

    public IReadOnlyList<HistoricoItem> ObterHistorico()
    {
        lock (_lock)
        {
            return _itens.ToList();
        }
    }

    public void Salvar()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(PastaDados);
            var json = JsonSerializer.Serialize(_itens, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CaminhoArquivo, json);
        }
    }
}
