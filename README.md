# Coala Verifica / Verificador Coala

Monitor de notas técnicas e status da Sefaz (NFe, NFCe, CTe, MDFe, NFSe Nacional, SPED), focado na Bahia.

## Como voltar a trabalhar nisso com o Claude Code

Mesmo processo de sempre: abra o terminal, entre na pasta do projeto e chame o `claude`.

```
cd D:\VerificadordeNT
claude
```

Este README existe pra você (ou o Claude) lembrar rápido onde as coisas estão e o que já foi feito, sem precisar reconstruir o contexto do zero.

## O que existe

### 1. App desktop (original, `src/VerificadorNT`)

App de bandeja (C#/.NET 8, WinForms + WebView2) que roda numa máquina Windows e monitora tudo: notas técnicas, disponibilidade oficial da Sefaz, tempo de resposta dos webservices e Downdetector. Continua funcionando normalmente — não foi alterado nesta migração.

### 2. Site na web (`web/`)

Site estático publicado no Cloudflare Pages, com backend no Supabase.

**URL do site:** https://verificadorcoala.pages.dev/
**Senha de acesso:** `any@2019` (proteção só client-side, não é segurança de verdade — qualquer um que veja o código-fonte descobre a senha; a tela de login mostra um lembrete "any....." só depois de errar a senha uma vez)

**Abas do site:**
- **Status Sefaz**: disponibilidade oficial (tabela por serviço), gráfico de tempo de resposta do webservice (janela deslizante da última 1h, com legenda de cores Normal/Lento/Muito lento/Timeout/Erro) e Downdetector (bolinha de status + resumo + comentários, quando tiver dado — hoje não tem fonte alimentando essa tabela, ver seção Downdetector abaixo).
- **Notas Técnicas**: lista filtrável (fonte, título, do que se trata, data) de NFe/NFCe, CTe, MDFe, NFSe Nacional e SPED. Notas do SPED com título "Conteúdo da página atualizado..." são filtradas fora (não são notas técnicas de verdade, é só o monitor de mudança de página).
- **Manuais Técnicos**: mesma lista filtrável, mas pros manuais (MOC, anexos de leiaute, manuais de contingência etc.) do portal NFe.

**Outros recursos:** alterna tema claro/escuro, notificação do navegador quando detecta problema ou nota/manual novo (pede permissão após o login, só se ainda não foi decidida — e mostra um aviso fixo no topo se a notificação estiver bloqueada, já que o navegador não deixa reabrir o pop-up nativo depois de negado uma vez), botão de Sair, mostra "Última verificação" no topo. A tela recarrega os dados a cada 2 min fixo (sem seletor — não fazia sentido configurável já que o backend também verifica nesse ritmo).

O botão "Atualizar agora" existiu por um tempo (disparava uma checagem nova de verdade, não só reexibia o banco) mas foi comentado — com tudo already rodando automático a cada 2 min e "Última verificação" visível, não trazia valor real. Código continua em `index.html` (dentro de `.topo-acoes`) e `app.js` (bloco "forçar atualização (desativado)"), é só descomentar os dois se precisar de volta.

**Favicon:** o coala (emoji 🐨, não existe uma foto de verdade no projeto) sem fundo, com "SEFAZ" em laranja sobreposto na parte de baixo.

#### Estrutura

```
web/
  site/                              # HTML/CSS/JS estático, publicado no Cloudflare Pages
  supabase/
    functions/
      verificar/                    # Edge Function (Deno) — MDFe, NFSe Nacional, SPED
      disparar-verificacao-sefaz/   # Edge Function (Deno) — dispara o workflow do GitHub Actions
    migrations/                     # schema do banco
  scripts/
    verificar-sefaz.ps1             # PowerShell — NFe/NFCe/CTe/manuais (disponibilidade, tempo de resposta, notas/manuais técnicos)
.github/workflows/
  verificar-sefaz.yml                # roda o .ps1 acima num runner windows-latest
```

#### Por que duas fontes de verificação diferentes

`nfe.fazenda.gov.br`, `cte.fazenda.gov.br` e `sefaz.ba.gov.br` derrubam a conexão de clientes HTTP não-Windows (Deno, Node, curl com OpenSSL) — provavelmente fingerprint de TLS (JA3), não bloqueio de IP. O stack nativo do Windows (Schannel, usado por `Invoke-WebRequest`/.NET — e por isso o app desktop em C# sempre funcionou) passa normal. Por isso:

- **Supabase Edge Function** (`verificar`, Deno) cobre só o que funciona nessa rede: MDFe (SVRS), NFSe Nacional, SPED. Agendada via `pg_cron` a cada 2 min.
- **GitHub Actions em `windows-latest`** (PowerShell, `web/scripts/verificar-sefaz.ps1`) cobre NFe/NFCe/CTe e os manuais técnicos, que precisam do Schannel.

As notas técnicas de NFe/NFCe só trazem os "Documentos vigentes" da página oficial (o filtro exclui os "não vigentes" — isso já foi testado e confirmado). Os manuais técnicos (outra `tipoConteudo` da mesma `listaConteudo.aspx`) não têm essa divisão, pega tudo. `Verificar-ListaConteudo` no `.ps1` é genérica (tabela de destino e filtro de vigência são parâmetros) — reaproveitada pra notas de NFe/CTe e pros manuais.

Downdetector nunca foi portado pra nuvem — exige um navegador real pra passar da proteção anti-robô, e isso só o app desktop consegue fazer. A tabela `downdetector_status` e a exibição no site já existem, mas hoje nada escreve nela (fica mostrando "sem dados").

#### Quem dispara o workflow do GitHub Actions

O agendamento nativo do GitHub (`schedule:` no `.yml`) se mostrou pouco confiável — chegou a ficar mais de 10h sem disparar um job configurado pra rodar a cada 30 min. Por isso o disparo vem do **pg_cron do Supabase** (o mesmo cron que já roda a Edge Function "verificar" sem nunca falhar):

`pg_cron` (a cada 2 min) → Edge Function `disparar-verificacao-sefaz` → API do GitHub (`workflow_dispatch`) → roda `verificar-sefaz.ps1` no runner Windows.

O token usado pra chamar a API do GitHub é o do `gh auth token` (guardado como secret `GITHUB_TOKEN` no Supabase). Se ele expirar/for revogado, gera um novo com `gh auth token` e roda `supabase secrets set GITHUB_TOKEN=<token>`.

**O repositório é público** (decisão tomada em 2026-07-07, pra detectar lentidão da Sefaz tão rápido quanto o monitor da Tecnospeed — com repo privado, 2 min de intervalo estouraria de longe o orçamento gratuito de minutos do GitHub Actions). Repo público = Actions ilimitado e grátis num runner Windows. Nenhuma credencial fica no código (tudo em secrets do GitHub/Supabase) — só a lógica do projeto ficou visível (histórico do git foi conferido antes de tornar público, estava limpo). O workflow tem `concurrency` pra enfileirar em vez de rodar em paralelo caso uma execução demore mais que 2 min.

O script tenta de novo (2x, timeout curto) quando a conexão com `nfe.fazenda.gov.br`/`cte.fazenda.gov.br` falha — na prática isso costuma ser o IP do runner (Azure, muda a cada execução) caindo bloqueado dessa vez, não instabilidade passageira, então o retry é só uma rede de segurança rápida. Cerca de 30% das execuções ainda têm alguma falha parcial de conexão — é esperado, então o script **não sai com erro** nesse caso (só registra no campo `erros` do resultado), porque rodando a cada 2 min isso virou spam de e-mail de "workflow failed" do GitHub quando ele saía com exit 1.

O tempo de resposta é medido com "melhor de 2 tentativas" — o runner do GitHub não fica no Brasil, então a medição inclui latência de rede extra por cima do tempo real da Sefaz; pegar a mais rápida das duas reduz picos falsos de "muito lento" causados só por ruído de rede do runner (confirmado comparando com o monitor da Tecnospeed num horário em que bateu diferente).

#### Contas/acessos usados

- **Supabase**: projeto `coala-verifica` (ref `zzbhqsbfxbuvgjidpuss`), região `sa-east-1`. CLI já linkado (`supabase link` já rodado dentro de `web/`).
- **Cloudflare Pages**: projeto `verificadorcoala`, conta `cosmerafa@gmail.com`. `wrangler` autenticado localmente.
- **GitHub**: repo `cosmerafa-stack/coala-verifica` (público desde 2026-07-07). `gh` CLI autenticado, com escopo `workflow` liberado (precisou de `gh auth refresh -h github.com -s workflow` numa sessão anterior).

Se qualquer uma dessas ferramentas pedir login de novo numa sessão futura, é só rodar o comando de login pedido (`wrangler login`, `gh auth login`, `supabase login`) — geralmente abre o navegador pra autorizar.

#### Comandos úteis pra redeploy manual

```bash
# Edge Functions do Supabase (depois de editar web/supabase/functions/*/index.ts)
cd web && supabase functions deploy verificar
cd web && supabase functions deploy disparar-verificacao-sefaz --no-verify-jwt

# Migration nova no banco
cd web && supabase db push

# Site (depois de editar algo em web/site/)
cd web/site && wrangler pages deploy . --project-name verificadorcoala --branch main --commit-dirty=true

# Testar o workflow do GitHub Actions manualmente
gh workflow run "Verificar Sefaz-BA / NFe / CTe"
gh run list --workflow "verificar-sefaz.yml" --limit 3
```

## Pendências conhecidas / decisões já tomadas

- Nada crítico em aberto no momento. Se a Sefaz/Fazenda um dia parar de derrubar clientes não-Windows, dá pra simplificar tudo de volta pra uma única Edge Function em Deno (o motivo de ter duas fontes deixa de existir).
- A senha do site (`any@2019`) é só um portão client-side, não segurança de verdade.
- Repositório é público — cuidado ao commitar qualquer coisa sensível.
- Downdetector não tem fonte de dados na nuvem (só o app desktop) — a exibição no site já está pronta, esperando dado.
- **App Android/Play Store**: avaliado em 2026-07-07 e adiado por decisão do usuário. Publicar na Play Store de verdade exige conta de desenvolvedor Google (US$ 25, taxa única, paga pela equipe — não é algo que dá pra contornar). Se retomar, o caminho mais simples e gratuito é transformar o site num PWA instalável (funciona em Android e iPhone, sem loja nenhuma) e, se quiserem ir além, gerar o pacote AAB/APK (TWA/Bubblewrap) pra Play Store quando alguém da equipe criar a conta e pagar a taxa.
