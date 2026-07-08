using System.Text.Json;
using System.Text.Json.Serialization;

namespace VerificadorNT.Config;

public sealed class AppConfig
{
    public int IntervaloHoras { get; set; } = 6;
    public bool IniciarComWindows { get; set; } = true;
    public bool NfeNfceAtivo { get; set; } = true;
    public bool CteAtivo { get; set; } = true;
    public bool MdfeAtivo { get; set; } = true;
    public bool NfseAtivo { get; set; } = true;
    public bool SpedAtivo { get; set; } = true;
    public DateTime? UltimaVerificacaoUtc { get; set; }

    public int IntervaloStatusMinutos { get; set; } = 15;
    public string UltimaChaveAlertaBahia { get; set; } = "";

    private static readonly string PastaDados =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoalaVerifica");

    public static string CaminhoArquivo => Path.Combine(PastaDados, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppConfig CarregarOuCriar()
    {
        try
        {
            if (File.Exists(CaminhoArquivo))
            {
                var json = File.ReadAllText(CaminhoArquivo);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // config corrompida: recomeça com padrão
        }

        var novo = new AppConfig();
        novo.Salvar();
        return novo;
    }

    public void Salvar()
    {
        Directory.CreateDirectory(PastaDados);
        var json = JsonSerializer.Serialize(this, JsonOpts);
        var caminhoTemporario = CaminhoArquivo + ".tmp";
        File.WriteAllText(caminhoTemporario, json);
        File.Move(caminhoTemporario, CaminhoArquivo, overwrite: true);
    }
}
