// Verificador Coala — Edge Function que verifica notas técnicas (MDFe, NFSe Nacional, SPED).
// Portado do app desktop (C#/.NET) para Deno. Chamada periodicamente por um cron job
// do Supabase (pg_cron -> net.http_post) e também pelo botão "Atualizar agora" do site.
// NFe/NFCe e CTe (disponibilidade, tempo de resposta e notas técnicas) NÃO ficam aqui:
// nfe.fazenda.gov.br/cte.fazenda.gov.br/sefaz.ba.gov.br derrubam a conexão de clientes
// não-Windows. Essas checagens rodam via GitHub Actions (web/scripts/verificar-sefaz.ps1).
import { DOMParser, Element } from "https://deno.land/x/deno_dom@v0.1.45/deno-dom-wasm.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const supabase = createClient(SUPABASE_URL, SERVICE_ROLE_KEY);

const USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CoalaVerifica/1.0";

interface Resultado {
  notas: number;
  erros: string[];
}

// ---------- utilidades HTTP ----------

/** Alguns portais da Fazenda exigem uma sessão de cookie (AspxAutoDetectCookieSupport) —
 * a primeira requisição fixa o cookie e redireciona; repetimos a chamada carregando-o. */
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

function ehNotaTecnica(titulo: string): boolean {
  return /nota\s*t[eé]cnica/i.test(titulo);
}

function normalizarEspacos(texto: string): string {
  return texto.replace(/\s+/g, " ").trim();
}

async function sha256(texto: string): Promise<string> {
  const dados = new TextEncoder().encode(texto);
  const hashBuffer = await crypto.subtle.digest("SHA-256", dados);
  return Array.from(new Uint8Array(hashBuffer))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("")
    .slice(0, 12);
}

// ---------- notas técnicas: MDFe (portal SVRS) ----------

async function verificarMdfe(resultado: Resultado) {
  const url = "https://dfe-portal.svrs.rs.gov.br/mdfe/Documentos";
  try {
    const html = await fetchComSessao(url);
    const doc = parseHtml(html);
    const artigos = doc.querySelectorAll("article.conteudo-lista__item");

    const linhas: Record<string, unknown>[] = [];
    for (const artigoNode of artigos) {
      const artigo = artigoNode as Element;
      const linkTitulo = artigo.querySelector("h2.conteudo-lista__item__titulo a");
      if (!linkTitulo) continue;

      const titulo = normalizarEspacos(linkTitulo.textContent);
      if (!titulo || !ehNotaTecnica(titulo)) continue;

      const tempo = artigo.querySelector("time.conteudo-lista__item__datahora");
      const data = tempo?.getAttribute("datetime")?.trim() || tempo?.textContent?.trim() || null;

      const paragrafos = Array.from(artigo.querySelectorAll("p")).map((p) => normalizarEspacos(p.textContent));
      let descricao = paragrafos.join(" ").trim();
      if (descricao.length > 220) descricao = descricao.slice(0, 220).trim() + "...";

      linhas.push({
        fonte: "MDFe",
        titulo,
        descricao: descricao || null,
        data_publicacao: data,
        url,
      });
    }

    if (linhas.length > 0) {
      const { error } = await supabase.from("notas_tecnicas").upsert(linhas, {
        onConflict: "fonte,titulo,url",
        ignoreDuplicates: true,
      });
      if (error) resultado.erros.push(`MDFe upsert: ${error.message}`);
      else resultado.notas += linhas.length;
    }
  } catch (ex) {
    resultado.erros.push(`MDFe: ${(ex as Error).message}`);
  }
}

// ---------- notas técnicas: NFSe Nacional ----------

async function verificarNfse(resultado: Resultado) {
  const url = "https://www.gov.br/nfse/pt-br/noticias";
  try {
    const html = await fetchComSessao(url);
    const doc = parseHtml(html);
    const links = doc.querySelectorAll("h2.titulo a");

    const linhas: Record<string, unknown>[] = [];
    for (const linkNode of links) {
      const link = linkNode as Element;
      const titulo = normalizarEspacos(link.textContent);
      const href = link.getAttribute("href");
      if (!titulo || !href || !ehNotaTecnica(titulo)) continue;

      const li = link.closest("li");
      const dataNode = li?.querySelector("span.data");
      const categoriaNode = li?.querySelector("div.categoria-noticia");

      linhas.push({
        fonte: "NFSe Nacional",
        titulo,
        descricao: categoriaNode ? normalizarEspacos(categoriaNode.textContent) : null,
        data_publicacao: dataNode ? normalizarEspacos(dataNode.textContent) : null,
        url: new URL(href, url).toString(),
      });
    }

    if (linhas.length > 0) {
      const { error } = await supabase.from("notas_tecnicas").upsert(linhas, {
        onConflict: "fonte,titulo,url",
        ignoreDuplicates: true,
      });
      if (error) resultado.erros.push(`NFSe upsert: ${error.message}`);
      else resultado.notas += linhas.length;
    }
  } catch (ex) {
    resultado.erros.push(`NFSe: ${(ex as Error).message}`);
  }
}

// ---------- notas técnicas: SPED (monitor de mudança por hash) ----------

const PAGINAS_SPED = [
  { nome: "ECD", url: "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/escrituracao-contabil-digital-ecd/escrituracao-contabil-digital-ecd" },
  { nome: "ECF", url: "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/escrituracao-contabil-fiscal-ecf/sped-programa-sped-contabil-fiscal" },
  { nome: "EFD ICMS-IPI", url: "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/escrituracao-fiscal-digital-efd/escrituracao-fiscal-digital-efd" },
  { nome: "EFD-Contribuições", url: "https://www.gov.br/receitafederal/pt-br/assuntos/orientacao-tributaria/declaracoes-e-demonstrativos/sped-sistema-publico-de-escrituracao-digital/efd-contribuicoes/programa-validador-da-escrituracao-fiscal-digital-das-contribuicoes-incidentes-sobre-a-receita-efd-contribuicoes-2" },
];

async function verificarSped(resultado: Resultado) {
  for (const pagina of PAGINAS_SPED) {
    const fonte = `SPED - ${pagina.nome}`;
    try {
      const html = await fetchComSessao(pagina.url);
      const doc = parseHtml(html);
      const texto = normalizarEspacos(doc.body?.textContent ?? html);
      const hash = await sha256(texto);

      const { data: anterior, error: erroLeitura } = await supabase
        .from("sped_hash_atual")
        .select("hash")
        .eq("fonte", fonte)
        .maybeSingle();
      if (erroLeitura) {
        resultado.erros.push(`SPED ${pagina.nome} leitura hash: ${erroLeitura.message}`);
        continue;
      }

      if (anterior?.hash === hash) continue; // nada mudou

      const { error: erroUpsertHash } = await supabase
        .from("sped_hash_atual")
        .upsert({ fonte, hash, atualizado_em: new Date().toISOString() });
      if (erroUpsertHash) {
        resultado.erros.push(`SPED ${pagina.nome} upsert hash: ${erroUpsertHash.message}`);
        continue;
      }

      // Sem hash anterior = primeira vez que vemos essa página, não é uma mudança real.
      if (!anterior) continue;

      const linha = {
        fonte,
        titulo: `Conteúdo da página atualizado (hash ${hash})`,
        descricao: `Alguma mudança foi detectada na página do módulo ${pagina.nome} do SPED (sem detalhamento automático — confira o link para ver o que mudou).`,
        data_publicacao: new Date().toLocaleDateString("pt-BR"),
        url: pagina.url,
      };

      const { error } = await supabase.from("notas_tecnicas").upsert([linha], {
        onConflict: "fonte,titulo,url",
        ignoreDuplicates: true,
      });
      if (error) resultado.erros.push(`SPED ${pagina.nome} upsert: ${error.message}`);
      else resultado.notas += 1;
    } catch (ex) {
      resultado.erros.push(`SPED ${pagina.nome}: ${(ex as Error).message}`);
    }
  }
}

// ---------- handler principal ----------

// O botão "Atualizar agora" do site chama essa function direto do navegador
// (origem diferente: verificadorcoala.pages.dev -> *.supabase.co), então
// precisa dos cabeçalhos CORS — sem eles o navegador bloqueia a chamada.
const cabecalhosCors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Headers": "authorization, apikey, content-type",
};

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { headers: cabecalhosCors });
  }

  const resultado: Resultado = { notas: 0, erros: [] };

  await verificarMdfe(resultado);
  await verificarNfse(resultado);
  await verificarSped(resultado);

  return new Response(JSON.stringify(resultado, null, 2), {
    headers: { "Content-Type": "application/json", ...cabecalhosCors },
  });
});
