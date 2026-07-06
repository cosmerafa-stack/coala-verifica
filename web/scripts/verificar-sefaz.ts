// Coala Verifica — checagens que a Sefaz/Fazenda bloqueia quando saem da rede do
// Supabase Edge Functions (Deno Deploy). Rodado à parte, via GitHub Actions, de uma
// rede que não está bloqueada: disponibilidade oficial, tempo de resposta e notas
// técnicas de NFe/NFCe e CTe. MDFe, NFSe Nacional e SPED continuam na Edge Function
// "verificar", que já funciona sem problema pra essas fontes.
import { DOMParser, Element } from "https://deno.land/x/deno_dom@v0.1.45/deno-dom-wasm.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const supabase = createClient(SUPABASE_URL, SERVICE_ROLE_KEY);

const USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CoalaVerifica/1.0";

interface Resultado {
  notas: number;
  disponibilidade: number;
  resposta: number;
  erros: string[];
}

async function fetchComSessao(url: string): Promise<string> {
  const primeira = await fetch(url, {
    headers: { "User-Agent": USER_AGENT },
    redirect: "manual",
  });

  const cookie = primeira.headers.get("set-cookie");
  const location = primeira.headers.get("location");

  if (primeira.status >= 300 && primeira.status < 400 && location) {
    const proximaUrl = new URL(location, url).toString();
    const segunda = await fetch(proximaUrl, {
      headers: {
        "User-Agent": USER_AGENT,
        ...(cookie ? { Cookie: cookie.split(";")[0] } : {}),
      },
    });
    return await segunda.text();
  }

  return await primeira.text();
}

function parseHtml(html: string) {
  return new DOMParser().parseFromString(html, "text/html")!;
}

function normalizarEspacos(texto: string): string {
  return texto.replace(/\s+/g, " ").trim();
}

// ---------- notas técnicas: NFe/NFCe e CTe (listaConteudo.aspx) ----------

async function verificarListaConteudo(
  nome: string,
  baseUrl: string,
  listaUrl: string,
  resultado: Resultado,
) {
  try {
    const html = await fetchComSessao(listaUrl);
    const doc = parseHtml(html);
    const spans = doc.querySelectorAll("span.tituloConteudo");

    const linhas: Record<string, unknown>[] = [];
    for (const spanNode of spans) {
      const span = spanNode as Element;
      const link = span.parentElement;
      if (!link || link.tagName.toLowerCase() !== "a") continue;

      const href = link.getAttribute("href");
      if (!href) continue;

      const titulo = normalizarEspacos(span.textContent);
      const dataMatch = titulo.match(/(\d{2}\/\d{2}\/\d{4})/);
      const url = new URL(href, baseUrl).toString();

      const textoCompleto = normalizarEspacos(link.parentElement?.textContent ?? "");
      let descricao = textoCompleto.replace(titulo, "").trim();
      if (descricao.length > 220) descricao = descricao.slice(0, 220).trim() + "...";

      linhas.push({
        fonte: nome,
        titulo,
        descricao: descricao || null,
        data_publicacao: dataMatch ? dataMatch[1] : null,
        url,
      });
    }

    if (linhas.length > 0) {
      const { error } = await supabase.from("notas_tecnicas").upsert(linhas, {
        onConflict: "fonte,titulo,url",
        ignoreDuplicates: true,
      });
      if (error) resultado.erros.push(`${nome} upsert: ${error.message}`);
      else resultado.notas += linhas.length;
    }
  } catch (ex) {
    resultado.erros.push(`${nome}: ${(ex as Error).message}`);
  }
}

// ---------- disponibilidade oficial (NFe/NFCe e CTe), Bahia ----------

async function verificarDisponibilidade(
  documento: string,
  url: string,
  linhaChave: string,
  resultado: Resultado,
) {
  try {
    const html = await fetchComSessao(url);
    const doc = parseHtml(html);
    const tabela = Array.from(doc.querySelectorAll("table")).find((t) =>
      (t as Element).getAttribute("id")?.includes("gdvDisponibilidade")
    ) as Element | undefined;
    if (!tabela) {
      resultado.erros.push(`${documento}: tabela de disponibilidade não encontrada`);
      return;
    }

    const linhas = tabela.querySelectorAll("tr");
    const cabecalhos = Array.from(linhas[0]?.querySelectorAll("th") ?? []).map((th) =>
      normalizarEspacos((th as Element).textContent).replace(/4$/, "")
    );

    const registros: Record<string, unknown>[] = [];
    for (let i = 1; i < linhas.length; i++) {
      const celulas = (linhas[i] as Element).querySelectorAll("td");
      if (celulas.length === 0) continue;

      const chave = normalizarEspacos((celulas[0] as Element).textContent);
      if (chave.toLowerCase() !== linhaChave.toLowerCase()) continue;

      for (let c = 1; c < celulas.length && c < cabecalhos.length; c++) {
        const nomeServico = cabecalhos[c];
        if (nomeServico.toLowerCase().includes("tempo médio")) continue;

        const img = (celulas[c] as Element).querySelector("img");
        const src = img?.getAttribute("src")?.toLowerCase() ?? "";
        let cor: string | null = null;
        if (src.includes("verde")) cor = "verde";
        else if (src.includes("amarel")) cor = "amarela";
        else if (src.includes("vermelh")) cor = "vermelho";
        if (!cor) continue;

        registros.push({ documento, servico: nomeServico, cor });
      }
      break;
    }

    if (registros.length > 0) {
      const { error } = await supabase.from("disponibilidade_oficial").insert(registros);
      if (error) resultado.erros.push(`${documento} disponibilidade insert: ${error.message}`);
      else resultado.disponibilidade += registros.length;
    }
  } catch (ex) {
    resultado.erros.push(`${documento} disponibilidade: ${(ex as Error).message}`);
  }
}

// ---------- tempo de resposta real dos webservices ----------

async function verificarResposta(documento: string, url: string, resultado: Resultado) {
  const inicio = performance.now();
  try {
    const controle = new AbortController();
    const timeoutId = setTimeout(() => controle.abort(), 35_000);
    const resposta = await fetch(url, { headers: { "User-Agent": USER_AGENT }, signal: controle.signal });
    clearTimeout(timeoutId);

    const segundos = (performance.now() - inicio) / 1000;
    const nivel = segundos <= 2 ? "Normal" : segundos <= 5 ? "Lento" : segundos < 30 ? "MuitoLento" : "Timeout";
    const detalhe = `Respondeu em ${segundos.toFixed(1)}s (HTTP ${resposta.status})`;

    const { error } = await supabase.from("resposta_tempo").insert({ documento, nivel, segundos, detalhe });
    if (error) resultado.erros.push(`${documento} resposta insert: ${error.message}`);
    else resultado.resposta += 1;
  } catch (ex) {
    const segundos = (performance.now() - inicio) / 1000;
    const nivel = segundos >= 30 ? "Timeout" : "Erro";
    await supabase.from("resposta_tempo").insert({
      documento,
      nivel,
      segundos: nivel === "Erro" ? null : segundos,
      detalhe: `Falha de conexão: ${(ex as Error).message}`,
    });
    resultado.erros.push(`${documento} resposta: falhou (nivel ${nivel})`);
    resultado.resposta += 1;
  }
}

// ---------- execução ----------

async function main() {
  const resultado: Resultado = { notas: 0, disponibilidade: 0, resposta: 0, erros: [] };

  await verificarDisponibilidade(
    "NFe / NFCe",
    "https://www.nfe.fazenda.gov.br/portal/disponibilidade.aspx",
    "BA",
    resultado,
  );
  await verificarDisponibilidade(
    "CTe",
    "https://www.cte.fazenda.gov.br/portal/disponibilidade.aspx",
    "SVRS",
    resultado,
  );

  await verificarResposta(
    "NFe",
    "https://nfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx",
    resultado,
  );
  await verificarResposta(
    "NFCe",
    "https://nfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx",
    resultado,
  );
  await verificarResposta(
    "CTe",
    "https://cte.svrs.rs.gov.br/ws/CTeStatusServicoV4/CTeStatusServicoV4.asmx",
    resultado,
  );

  await verificarListaConteudo(
    "NFe/NFCe",
    "https://www.nfe.fazenda.gov.br/portal/",
    "https://www.nfe.fazenda.gov.br/portal/listaConteudo.aspx?tipoConteudo=04BIflQt1aY=",
    resultado,
  );
  await verificarListaConteudo(
    "CTe",
    "https://www.cte.fazenda.gov.br/portal/",
    "https://www.cte.fazenda.gov.br/portal/listaConteudo.aspx?tipoConteudo=Y0nErnoZpsg=",
    resultado,
  );

  console.log(JSON.stringify(resultado, null, 2));
  if (resultado.erros.length > 0) Deno.exit(1);
}

await main();
