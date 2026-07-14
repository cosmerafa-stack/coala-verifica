-- A Edge Function "verificar" é pública (verify_jwt=false, precisa ser chamada
-- pelo pg_cron e pelo botão "Atualizar agora" do navegador) e não tinha nenhum
-- limite de chamadas — qualquer um que descobrisse a URL podia martelar o
-- endpoint, que faz scraping em sites de terceiros (SVRS, gov.br/nfse, Receita),
-- arriscando o IP da function ser bloqueado por esses sites ou gerando custo de
-- invocação à toa. Essa tabela guarda a última execução por function pra
-- permitir um throttle simples, no mesmo espírito do que "disparar-verificacao-sefaz"
-- já fazia consultando a API do GitHub.
create table if not exists funcao_ultima_execucao (
  funcao text primary key,
  executado_em timestamptz not null default now()
);

alter table funcao_ultima_execucao enable row level security;
-- Estado interno de controle, não é conteúdo pra exibir no site — sem policy de leitura pública.
