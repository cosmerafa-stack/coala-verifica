// Verificador Coala — dispara o workflow do GitHub Actions que roda as checagens
// de NFe/NFCe/CTe (web/scripts/verificar-sefaz.ps1, precisa de um runner Windows).
//
// O agendamento nativo do GitHub Actions ("schedule:" no .yml) se mostrou pouco
// confiável — chegou a passar mais de 10h sem disparar um job configurado pra
// rodar a cada 30 min. O cron do Supabase (pg_cron) já é usado pra "verificar" e
// nunca falhou, então essa function só serve de ponte: o pg_cron chama ela, e
// ela chama a API do GitHub pra disparar o workflow via workflow_dispatch.
const GITHUB_TOKEN = Deno.env.get("GITHUB_TOKEN")!;
const GITHUB_REPO = "cosmerafa-stack/coala-verifica";
const GITHUB_WORKFLOW = "verificar-sefaz.yml";

Deno.serve(async (_req) => {
  const resposta = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/actions/workflows/${GITHUB_WORKFLOW}/dispatches`,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${GITHUB_TOKEN}`,
        Accept: "application/vnd.github+json",
        "User-Agent": "verificador-coala-pgcron",
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ ref: "master" }),
    },
  );

  const corpo = resposta.status === 204 ? "" : await resposta.text();
  return new Response(JSON.stringify({ status: resposta.status, corpo }), {
    status: resposta.ok ? 200 : 502,
    headers: { "Content-Type": "application/json" },
  });
});
