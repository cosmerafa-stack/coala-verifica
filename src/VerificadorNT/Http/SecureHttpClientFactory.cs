using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace VerificadorNT.Http;

/// <summary>
/// Cria HttpClients para os portais oficiais de documentos fiscais eletrônicos.
///
/// Esses portais (ex.: nfe.fazenda.gov.br, cte.fazenda.gov.br) usam certificados
/// emitidos por CAs públicas (Let's Encrypt, GlobalSign), mas em máquinas Windows
/// cujo repositório de certificados raiz não foi atualizado recentemente a validação
/// pode falhar com "unable to get local issuer certificate" mesmo o certificado sendo
/// legítimo. Para não perder alertas por causa disso, se a cadeia padrão do Windows
/// falhar, validamos manualmente se o certificado é válido (não expirado, nome correto)
/// e se o problema é só a ausência da raiz/intermediária no repositório local — nesse
/// caso aceitamos e registramos um aviso, em vez de desabilitar a validação por completo.
/// </summary>
public static class SecureHttpClientFactory
{
    public static event Action<string>? Aviso;

    public static HttpClient Criar(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = ValidarCertificado,
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) VerificadorNT/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9");

        return client;
    }

    private static bool ValidarCertificado(HttpRequestMessage request, X509Certificate2? cert,
        X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (cert is null || chain is null) return false;

        // Só relaxamos a checagem de cadeia (raiz/intermediária não confiável localmente).
        // Nome incorreto ou certificado expirado continuam rejeitados.
        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        var somenteProblemaDeCadeia = chain.ChainStatus.All(s =>
            s.Status is X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain);

        if (!somenteProblemaDeCadeia) return false;

        var host = request.RequestUri?.Host ?? "";
        var dominioConhecido = host.EndsWith(".fazenda.gov.br", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".gov.br", StringComparison.OrdinalIgnoreCase);

        if (!dominioConhecido) return false;

        var agora = DateTime.Now;
        if (agora < cert.NotBefore || agora > cert.NotAfter) return false;

        Aviso?.Invoke(
            $"Certificado de {host} aceito sem cadeia de confiança completa " +
            "(provável causa: repositório de certificados raiz do Windows desatualizado).");
        return true;
    }
}
