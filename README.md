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
**Senha de acesso:** `any@2019` (proteção só client-side, não é segurança de verdade — qualquer um que veja o código-fonte descobre a senha)

Recursos do site: alterna tema claro/escuro, notificação do navegador quando detecta problema ou nota nova (pede permissão após o login), botão de Sair, intervalo de atualização configurável (1/2/5 min), filtro de notas por data de publicação, mostra "Última verificação" no topo.

#### Estrutura

```
web/
  site/                  # HTML/CSS/JS estático, publicado no Cloudflare Pages
  supabase/
    functions/verificar/ # Edge Function (Deno) — MDFe, NFSe Nacional, SPED
    migrations/          # schema do banco
  scripts/
    verificar-sefaz.ps1  # PowerShell — NFe/NFCe/CTe (disponibilidade, tempo de resposta, notas técnicas)
.github/workflows/
  verificar-sefaz.yml    # roda o .ps1 acima num runner windows-latest, a cada 30 min
```

#### Por que duas fontes de verificação diferentes

`nfe.fazenda.gov.br`, `cte.fazenda.gov.br` e `sefaz.ba.gov.br` derrubam a conexão de clientes HTTP não-Windows (Deno, Node, curl com OpenSSL) — provavelmente fingerprint de TLS (JA3), não bloqueio de IP. O stack nativo do Windows (Schannel, usado por `Invoke-WebRequest`/.NET — e por isso o app desktop em C# sempre funcionou) passa normal. Por isso:

- **Supabase Edge Function** (`verificar`, Deno) cobre só o que funciona nessa rede: MDFe (SVRS), NFSe Nacional, SPED. Agendada via `pg_cron` a cada 2 min.
- **GitHub Actions em `windows-latest`** (PowerShell) cobre NFe/NFCe/CTe, que precisam do Schannel. Agendada a cada 30 min (intervalo escolhido pra caber no plano gratuito do GitHub — repo é privado, runner Windows conta 2x o minuto).

As notas técnicas de NFe/NFCe só trazem os "Documentos vigentes" da página oficial (o filtro exclui os "não vigentes" — isso já foi testado e confirmado).

Downdetector nunca foi portado pra nuvem — exige um navegador real pra passar da proteção anti-robô, e isso só o app desktop consegue fazer.

#### Contas/acessos usados

- **Supabase**: projeto `coala-verifica` (ref `zzbhqsbfxbuvgjidpuss`), região `sa-east-1`. CLI já linkado (`supabase link` já rodado dentro de `web/`).
- **Cloudflare Pages**: projeto `verificadorcoala`, conta `cosmerafa@gmail.com`. `wrangler` autenticado localmente.
- **GitHub**: repo `cosmerafa-stack/coala-verifica` (privado). `gh` CLI autenticado, com escopo `workflow` liberado (precisou de `gh auth refresh -h github.com -s workflow` numa sessão anterior).

Se qualquer uma dessas ferramentas pedir login de novo numa sessão futura, é só rodar o comando de login pedido (`wrangler login`, `gh auth login`, `supabase login`) — geralmente abre o navegador pra autorizar.

#### Comandos úteis pra redeploy manual

```bash
# Edge Function do Supabase (depois de editar web/supabase/functions/verificar/index.ts)
cd web && supabase functions deploy verificar

# Migration nova no banco
cd web && supabase db push

# Site (depois de editar algo em web/site/)
cd web/site && wrangler pages deploy . --project-name verificadorcoala --branch main --commit-dirty=true

# Testar o workflow do GitHub Actions manualmente
gh workflow run "Verificar Sefaz-BA / NFe / CTe"
gh run list --workflow "verificar-sefaz.yml" --limit 3
```

## Pendências conhecidas

- Nada crítico em aberto no momento. Se a Sefaz/Fazenda um dia parar de derrubar clientes não-Windows, dá pra simplificar tudo de volta pra uma única Edge Function em Deno (o motivo de ter duas fontes deixa de existir).
- O intervalo de 30 min do GitHub Actions pode ser reduzido se o repositório virar público (Actions ilimitado) ou se a conta habilitar cobrança.
