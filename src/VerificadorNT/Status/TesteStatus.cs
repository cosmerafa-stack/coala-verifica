using System.Text;
using VerificadorNT.Http;

namespace VerificadorNT.Status;

/// <summary>Modo de teste manual (--test-status): roda os checkers de status e grava o
/// resultado em um arquivo, para depuração sem precisar da bandeja do sistema.</summary>
public static class TesteStatus
{
    public static async Task ExecutarAsync()
    {
        var sb = new StringBuilder();
        try
        {
            using var http = SecureHttpClientFactory.Criar();
            foreach (var documento in DisponibilidadeSefazChecker.Documentos)
            {
                var disp = await DisponibilidadeSefazChecker.VerificarAsync(http, documento, CancellationToken.None);
                sb.AppendLine($"=== Disponibilidade {documento.Nome} - Bahia (linha '{documento.LinhaBahia}') ===");
                if (disp is null)
                {
                    sb.AppendLine($"Não encontrou a linha '{documento.LinhaBahia}' na tabela.");
                }
                else
                {
                    sb.AppendLine($"TemProblema: {disp.TemProblema}");
                    foreach (var s in disp.Servicos)
                        sb.AppendLine($"  {s.Nome}: {s.Cor}");
                }
                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERRO disponibilidade: {ex}");
        }

        sb.AppendLine();

        try
        {
            sb.AppendLine("=== Downdetector Sefaz ===");
            var dd = await DowndetectorChecker.VerificarAsync(CancellationToken.None);
            sb.AppendLine($"ProblemaDetectado: {dd.ProblemaDetectado}");
            sb.AppendLine($"Resumo: {dd.Resumo}");
            if (dd.ComentariosSobreBahia.Count > 0)
            {
                sb.AppendLine("Comentários mencionando Bahia:");
                foreach (var c in dd.ComentariosSobreBahia) sb.AppendLine($"  - {c}");
            }
            if (dd.Erro is not null) sb.AppendLine($"Erro: {dd.Erro}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERRO downdetector: {ex}");
        }

        var caminho = Path.Combine(Path.GetTempPath(), "verificadornt-teste-status.txt");
        await File.WriteAllTextAsync(caminho, sb.ToString());
    }
}
