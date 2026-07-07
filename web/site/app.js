const headers = {
  apikey: SUPABASE_ANON_KEY,
  Authorization: `Bearer ${SUPABASE_ANON_KEY}`,
};

let ultimosPontosGrafico = null;

// ---------- portão de senha ----------

const SENHA_ACESSO = "any@2019";
const CHAVE_SESSAO_SENHA = "coala-verifica-autenticado";

function pedirPermissaoNotificacao() {
  if (typeof Notification === "undefined") return;
  if (Notification.permission === "default") Notification.requestPermission();
}

function notificar(titulo, corpo) {
  if (typeof Notification === "undefined") return;
  if (Notification.permission !== "granted") return;
  new Notification(titulo, { body: corpo, icon: "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/1f428.png" });
}

const portao = document.getElementById("portaoSenha");
if (sessionStorage.getItem(CHAVE_SESSAO_SENHA) === "1") {
  portao.classList.add("oculto");
  pedirPermissaoNotificacao();
}

document.getElementById("formSenha").addEventListener("submit", (ev) => {
  ev.preventDefault();
  const valor = document.getElementById("campoSenha").value;
  if (valor === SENHA_ACESSO) {
    sessionStorage.setItem(CHAVE_SESSAO_SENHA, "1");
    portao.classList.add("oculto");
    pedirPermissaoNotificacao();
  } else {
    document.getElementById("erroSenha").style.display = "block";
  }
});

document.getElementById("botaoSair").addEventListener("click", () => {
  sessionStorage.removeItem(CHAVE_SESSAO_SENHA);
  document.getElementById("campoSenha").value = "";
  document.getElementById("erroSenha").style.display = "none";
  portao.classList.remove("oculto");
});

// ---------- tema claro/escuro ----------

const CHAVE_TEMA = "coala-verifica-tema";

function aplicarTema(tema) {
  document.documentElement.setAttribute("data-theme", tema);
  document.getElementById("botaoTema").textContent = tema === "light" ? "🌙" : "☀️";
  localStorage.setItem(CHAVE_TEMA, tema);
  if (ultimosPontosGrafico) atualizarGrafico(ultimosPontosGrafico);
}

document.getElementById("botaoTema").addEventListener("click", () => {
  const atual = document.documentElement.getAttribute("data-theme") === "light" ? "dark" : "light";
  aplicarTema(atual);
});

aplicarTema(localStorage.getItem(CHAVE_TEMA) || "dark");

async function supabaseGet(caminho) {
  const resp = await fetch(`${SUPABASE_URL}/rest/v1/${caminho}`, { headers });
  if (!resp.ok) throw new Error(`Erro ${resp.status} ao consultar ${caminho}`);
  return await resp.json();
}

// ---------- navegação entre seções ----------

document.querySelectorAll(".aba-principal").forEach((botao) => {
  botao.addEventListener("click", () => {
    document.querySelectorAll(".aba-principal").forEach((b) => b.classList.remove("ativa"));
    botao.classList.add("ativa");
    const secao = botao.dataset.secao;
    document.getElementById("secao-status").classList.toggle("oculta", secao !== "status");
    document.getElementById("secao-notas").classList.toggle("oculta", secao !== "notas");
  });
});

let documentoAtivo = "NFe";
let grafico = null;

document.querySelectorAll(".aba-doc").forEach((botao) => {
  botao.addEventListener("click", async () => {
    document.querySelectorAll(".aba-doc").forEach((b) => b.classList.remove("ativa"));
    botao.classList.add("ativa");
    documentoAtivo = botao.dataset.doc;
    await carregarStatus();
  });
});

// ---------- seção status Sefaz ----------

const DOCUMENTO_OFICIAL_POR_ABA = { NFe: "NFe / NFCe", NFCe: "NFe / NFCe", CTe: "CTe" };
const NIVEL_PARA_Y = { Normal: 1, Lento: 2, MuitoLento: 3, Timeout: 4, Erro: 5 };
const COR_NIVEL = {
  Normal: "#5eb1fa",
  Lento: "#e6c83c",
  MuitoLento: "#e68c28",
  Timeout: "#dc4646",
  Erro: "#9632a0",
};

async function carregarStatus() {
  const nomeOficial = DOCUMENTO_OFICIAL_POR_ABA[documentoAtivo];

  try {
    const servicos = await supabaseGet(
      `disponibilidade_oficial?documento=eq.${encodeURIComponent(nomeOficial)}&select=*&order=verificado_em.desc&limit=30`
    );
    preencherTabelaServicos(servicos);
  } catch (e) {
    console.error(e);
  }

  try {
    const pontos = await supabaseGet(
      `resposta_tempo?documento=eq.${encodeURIComponent(documentoAtivo)}&select=*&order=verificado_em.desc&limit=120`
    );
    pontos.reverse();
    atualizarGrafico(pontos);
  } catch (e) {
    console.error(e);
  }

  await atualizarStatusGeral();
  await atualizarDowndetector();
}

async function atualizarDowndetector() {
  const bolinha = document.getElementById("bolinhaDowndetector");
  const resumo = document.getElementById("downdetectorResumo");
  const lista = document.getElementById("downdetectorComentarios");
  try {
    const registros = await supabaseGet("downdetector_status?select=*&order=verificado_em.desc&limit=1");
    if (registros.length === 0) {
      bolinha.className = "bolinha";
      resumo.textContent = "Sem dados no momento — o Downdetector exige um navegador real para ser consultado (proteção anti-robô), disponível apenas no app desktop.";
      lista.innerHTML = "";
      return;
    }

    const [status] = registros;
    bolinha.className = "bolinha " + (status.problema_detectado ? "problema" : "ok");
    const hora = new Date(status.verificado_em).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
    resumo.textContent = `${status.resumo} (verificado às ${hora})`;

    const comentarios = Array.isArray(status.comentarios_bahia) ? status.comentarios_bahia : [];
    lista.innerHTML = comentarios.map((c) => `<li>${typeof c === "string" ? c : (c.texto || JSON.stringify(c))}</li>`).join("");
  } catch (e) {
    console.error(e);
  }
}

function preencherTabelaServicos(servicos) {
  const vistos = new Set();
  const linhas = [];
  for (const s of servicos) {
    if (vistos.has(s.servico)) continue;
    vistos.add(s.servico);
    linhas.push(s);
  }

  const corpo = document.querySelector("#tabelaServicos tbody");
  corpo.innerHTML = "";
  if (linhas.length === 0) {
    corpo.innerHTML = `<tr><td colspan="2" class="texto-apagado">Sem dados ainda — aguarde a próxima verificação automática.</td></tr>`;
    return;
  }

  for (const s of linhas) {
    const { texto, classe } =
      s.cor === "verde" ? { texto: "OK", classe: "status-ok" } :
      s.cor === "amarela" ? { texto: "Instável", classe: "status-instavel" } :
      { texto: "Falha", classe: "status-falha" };
    corpo.innerHTML += `<tr><td>${s.servico}</td><td class="${classe}">${texto}</td></tr>`;
  }
}

function atualizarGrafico(pontos) {
  ultimosPontosGrafico = pontos;

  const estilo = getComputedStyle(document.documentElement);
  const corTexto = estilo.getPropertyValue("--texto-apagado").trim();
  const corGrade = estilo.getPropertyValue("--borda").trim();
  const corDestaque = estilo.getPropertyValue("--destaque").trim();

  const ctx = document.getElementById("graficoResposta");
  const labels = pontos.map((p) => new Date(p.verificado_em).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" }));
  const dados = pontos.map((p) => NIVEL_PARA_Y[p.nivel] ?? null);
  const cores = pontos.map((p) => COR_NIVEL[p.nivel] ?? "#888");

  if (grafico) grafico.destroy();
  grafico = new Chart(ctx, {
    type: "line",
    data: {
      labels,
      datasets: [{
        data: dados,
        borderColor: corDestaque,
        pointBackgroundColor: cores,
        pointBorderColor: cores,
        pointRadius: 5,
        tension: 0,
      }],
    },
    options: {
      responsive: true,
      plugins: { legend: { display: false } },
      scales: {
        y: {
          min: 0.5, max: 5.5,
          ticks: {
            stepSize: 1,
            color: corTexto,
            callback: (v) => ({ 1: "Normal: <= 2s", 2: "Lento: <= 5s", 3: "Muito lento: < 30s", 4: "Timeout: > 30s", 5: "Erro" }[v] ?? ""),
          },
          grid: { color: corGrade },
        },
        x: {
          ticks: {
            color: corTexto,
            // Por padrão o Chart.js decide sozinho quais rótulos cabem sem
            // sobrepor e pode esconder justamente o mais recente. Mostra só
            // ~15 espaçados, mas o último (dado mais novo) sempre aparece.
            autoSkip: false,
            maxRotation: 60,
            minRotation: 60,
            callback: (_valor, indice) => {
              const passo = Math.max(1, Math.ceil(labels.length / 15));
              return indice === labels.length - 1 || indice % passo === 0 ? labels[indice] : "";
            },
          },
          grid: { color: corGrade },
        },
      },
    },
  });
}

let problemaConhecido = null; // null = ainda não temos uma leitura de referência

async function atualizarStatusGeral() {
  const bolinha = document.getElementById("bolinhaGeral");
  const texto = document.getElementById("textoStatusGeral");
  const ultimaVerificacao = document.getElementById("ultimaVerificacao");
  try {
    const disp = await supabaseGet("disponibilidade_oficial?select=*&order=verificado_em.desc&limit=30");
    const temProblema = disp.some((d) => d.cor !== "verde");
    bolinha.className = "bolinha " + (temProblema ? "problema" : "ok");
    texto.textContent = temProblema
      ? "Instabilidade detectada para a Bahia"
      : "Tudo certo na Bahia!";

    if (disp.length > 0) {
      const maisRecente = disp.reduce((a, b) => (a.verificado_em > b.verificado_em ? a : b));
      const hora = new Date(maisRecente.verificado_em).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
      ultimaVerificacao.textContent = `Última verificação: ${hora}`;
    }

    if (problemaConhecido !== null && temProblema && !problemaConhecido) {
      notificar("Verificador Coala — instabilidade na Sefaz", "Foi detectado um problema de disponibilidade para a Bahia.");
    }
    problemaConhecido = temProblema;
  } catch {
    bolinha.className = "bolinha";
    texto.textContent = "Sem dados";
  }
}

// ---------- seção notas técnicas ----------

let todasNotas = [];
let idsNotasConhecidas = null; // null = ainda não temos uma leitura de referência

// O monitor de mudança do SPED grava "Conteúdo da página atualizado (hash ...)"
// pra saber quando algo mudou, mas isso não é uma nota técnica de verdade —
// não deve aparecer nessa lista.
function ehNotaTecnicaDeVerdade(nota) {
  return !nota.titulo.startsWith("Conteúdo da página atualizado");
}

async function carregarNotas() {
  const todas = await supabaseGet("notas_tecnicas?select=*&order=detectado_em.desc&limit=1000");
  todasNotas = todas.filter(ehNotaTecnicaDeVerdade);

  const idsAtuais = new Set(todasNotas.map((n) => n.id));
  if (idsNotasConhecidas) {
    for (const n of todasNotas) {
      if (!idsNotasConhecidas.has(n.id)) {
        notificar("Verificador Coala — nova nota técnica", `${n.fonte}: ${n.titulo}`);
      }
    }
  }
  idsNotasConhecidas = idsAtuais;

  const selectFonte = document.getElementById("filtroFonte");
  const fontes = [...new Set(todasNotas.map((n) => n.fonte))].sort();
  selectFonte.innerHTML = `<option value="">Todas as fontes</option>` + fontes.map((f) => `<option value="${f}">${f}</option>`).join("");

  aplicarFiltrosNotas();
}

function aplicarFiltrosNotas() {
  const fonte = document.getElementById("filtroFonte").value;
  const titulo = document.getElementById("filtroTitulo").value.toLowerCase();
  const assunto = document.getElementById("filtroAssunto").value.toLowerCase();
  const campoData = document.getElementById("filtroCampoData").value;
  const de = document.getElementById("filtroDe").value;
  const ate = document.getElementById("filtroAte").value;

  let filtradas = todasNotas;
  if (fonte) filtradas = filtradas.filter((n) => n.fonte === fonte);
  if (titulo) filtradas = filtradas.filter((n) => n.titulo.toLowerCase().includes(titulo));
  if (assunto) filtradas = filtradas.filter((n) => (n.descricao || "").toLowerCase().includes(assunto));

  if (de || ate) {
    filtradas = filtradas.filter((n) => {
      let dataStr = n[campoData];
      if (!dataStr) return false;
      let data;
      if (campoData === "data_publicacao") {
        const partes = dataStr.split("/");
        if (partes.length !== 3) return false;
        data = new Date(`${partes[2]}-${partes[1]}-${partes[0]}`);
      } else {
        data = new Date(dataStr);
      }
      if (de && data < new Date(de)) return false;
      if (ate && data > new Date(ate + "T23:59:59")) return false;
      return true;
    });
  }

  const corpo = document.querySelector("#tabelaNotas tbody");
  corpo.innerHTML = filtradas.map((n) => `
    <tr>
      <td>${n.fonte}</td>
      <td title="${n.titulo}"><a href="${n.url}" target="_blank" rel="noopener" style="color:inherit">${n.titulo}</a></td>
      <td>${n.descricao || "-"}</td>
      <td>${n.data_publicacao || "-"}</td>
      <td>${new Date(n.detectado_em).toLocaleString("pt-BR")}</td>
    </tr>
  `).join("");

  document.getElementById("contagemNotas").textContent = `${filtradas.length} de ${todasNotas.length} item(ns)`;
}

["filtroFonte", "filtroTitulo", "filtroAssunto", "filtroCampoData", "filtroDe", "filtroAte"].forEach((id) => {
  document.getElementById(id).addEventListener("input", aplicarFiltrosNotas);
});

document.getElementById("btnLimparFiltros").addEventListener("click", () => {
  document.getElementById("filtroFonte").value = "";
  document.getElementById("filtroTitulo").value = "";
  document.getElementById("filtroAssunto").value = "";
  document.getElementById("filtroCampoData").value = "data_publicacao";
  document.getElementById("filtroDe").value = "";
  document.getElementById("filtroAte").value = "";
  aplicarFiltrosNotas();
});

// ---------- intervalo de atualização ----------
//
// Fixo em 2 min — o mesmo ritmo em que o backend verifica todas as fontes,
// então não faz sentido deixar configurável pra mais devagar (só atrasaria a
// exibição) nem mais rápido (não teria dado novo pra mostrar).

const INTERVALO_ATUALIZACAO_MS = 120_000;

function aplicarIntervalo(ms) {
  setInterval(carregarStatus, ms);
  setInterval(carregarNotas, ms);
}

// ---------- forçar atualização ----------
//
// "Atualizar agora" não só reexibe o que já está no banco — dispara uma
// checagem nova de verdade. MDFe/NFSe/SPED (Edge Function "verificar") são
// rápidos e o resultado sai na hora. NFe/NFCe/CTe dependem do workflow do
// GitHub Actions (runner Windows, ~30s pra rodar) e têm um limite de 1 min
// entre disparos, só pra evitar disparo duplicado em cliques muito próximos
// (o repo é público, então rodar o runner não tem custo).

const botaoAtualizar = document.getElementById("botaoAtualizar");
const mensagemAtualizar = document.getElementById("mensagemAtualizar");

botaoAtualizar.addEventListener("click", async () => {
  botaoAtualizar.disabled = true;
  botaoAtualizar.textContent = "🔄 Verificando...";
  mensagemAtualizar.textContent = "";

  try {
    const [resultadoVerificar, resultadoSefaz] = await Promise.all([
      fetch(`${SUPABASE_URL}/functions/v1/verificar`, { method: "POST" }).then((r) => r.json()).catch(() => null),
      fetch(`${SUPABASE_URL}/functions/v1/disparar-verificacao-sefaz`, { method: "POST" }).then((r) => r.json()).catch(() => null),
    ]);

    await Promise.all([carregarStatus(), carregarNotas()]);

    const partes = [];
    if (resultadoVerificar) partes.push(`MDFe/NFSe/SPED verificados agora (${resultadoVerificar.notas} notas conferidas)`);
    if (resultadoSefaz?.disparado) partes.push("NFe/NFCe/CTe: verificação disparada, deve refletir em ~1 min");
    else if (resultadoSefaz?.motivo) partes.push(`NFe/NFCe/CTe: ${resultadoSefaz.motivo}`);
    mensagemAtualizar.textContent = partes.join(" · ");

    if (resultadoSefaz?.disparado) {
      setTimeout(() => { carregarStatus(); carregarNotas(); }, 45_000);
    }
  } finally {
    botaoAtualizar.disabled = false;
    botaoAtualizar.textContent = "🔄 Atualizar agora";
  }
});

// ---------- inicialização ----------

carregarStatus();
carregarNotas();
aplicarIntervalo(INTERVALO_ATUALIZACAO_MS);
