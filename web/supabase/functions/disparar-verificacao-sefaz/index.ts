// Verificador Coala — dispara o workflow do GitHub Actions que roda as checagens
// de NFe/NFCe/CTe (web/scripts/verificar-sefaz.ps1, precisa de um runner Windows).
//
// O agendamento nativo do GitHub Actions ("schedule:" no .yml) se mostrou pouco
// confiável — chegou a passar mais de 10h sem disparar um job configurado pra
// rodar a cada 30 min. O cron do Supabase (pg_cron) já é usado pra "verificar" e
// nunca falhou, então essa function só serve de ponte: o pg_cron chama ela, e
// ela chama a API do GitHub pra disparar o workflow via workflow_dispatch.
//
// O repositório é público, então o runner Windows do GitHub Actions é grátis e
// ilimitado — por isso dá pra rodar a cada 2 min, igual o monitor da
// Tecnospeed, sem se preocupar com orçamento de minutos.
//
// Também é chamada pelo botão "Atualizar agora" do site — como essa function
// não exige autenticação (verify_jwt=false) pra poder ser chamada pelo
// pg_cron, qualquer um que descobrir a URL poderia chamá-la em rajada. O
// limite mínimo aqui é só pra evitar disparos redundantes muito próximos
// (ex.: clique duplo), não é mais sobre economizar minutos.
const GITHUB_TOKEN = Deno.env.get("GITHUB_TOKEN")!;
const GITHUB_REPO = "cosmerafa-stack/coala-verifica";
const GITHUB_WORKFLOW = "verificar-sefaz.yml";
const INTERVALO_MINIMO_MIN = 1;

const headersGithub = {
  Authorization: `Bearer ${GITHUB_TOKEN}`,
  Accept: "application/vnd.github+json",
  "User-Agent": "verificador-coala-pgcron",
};

// O botão "Atualizar agora" do site chama essa function direto do navegador
// (origem diferente: verificadorcoala.pages.dev -> *.supabase.co), então
// precisa dos cabeçalhos CORS — sem eles o navegador bloqueia a chamada.
// Restrito à origem real do site (em vez de "*") pra que só o próprio site
// consiga disparar essa chamada a partir do navegador de um visitante; o
// pg_cron chama de servidor pra servidor, então não passa pelo CORS.
const cabecalhosCors = {
  "Access-Control-Allow-Origin": "https://verificadorcoala.pages.dev",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Headers": "authorization, apikey, content-type",
};

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { headers: cabecalhosCors });
  }

  const respostaRuns = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/actions/workflows/${GITHUB_WORKFLOW}/runs?per_page=1`,
    { headers: headersGithub },
  );
  if (respostaRuns.ok) {
    const dados = await respostaRuns.json();
    const ultimaExecucao = dados.workflow_runs?.[0];
    if (ultimaExecucao) {
      const minutosDesdeUltima = (Date.now() - new Date(ultimaExecucao.created_at).getTime()) / 60_000;
      if (minutosDesdeUltima < INTERVALO_MINIMO_MIN) {
        return new Response(
          JSON.stringify({
            disparado: false,
            motivo: `Já rodou há ${minutosDesdeUltima.toFixed(1)} min — espera pelo menos ${INTERVALO_MINIMO_MIN} min entre disparos.`,
          }),
          { status: 429, headers: { "Content-Type": "application/json", ...cabecalhosCors } },
        );
      }
    }
  }

  const resposta = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/actions/workflows/${GITHUB_WORKFLOW}/dispatches`,
    {
      method: "POST",
      headers: { ...headersGithub, "Content-Type": "application/json" },
      body: JSON.stringify({ ref: "master" }),
    },
  );

  const corpo = resposta.status === 204 ? "" : await resposta.text();
  return new Response(JSON.stringify({ disparado: resposta.ok, status: resposta.status, corpo }), {
    status: resposta.ok ? 200 : 502,
    headers: { "Content-Type": "application/json", ...cabecalhosCors },
  });
});
