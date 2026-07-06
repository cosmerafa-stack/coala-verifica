-- Coala Verifica - schema inicial
-- Notas técnicas (NFe/NFCe, CTe, MDFe, NFSe Nacional, SPED)
create table if not exists notas_tecnicas (
  id bigint generated always as identity primary key,
  fonte text not null,
  titulo text not null,
  descricao text,
  data_publicacao text,
  url text not null,
  detectado_em timestamptz not null default now(),
  unique (fonte, titulo, url)
);

create index if not exists idx_notas_tecnicas_detectado_em on notas_tecnicas (detectado_em desc);
create index if not exists idx_notas_tecnicas_fonte on notas_tecnicas (fonte);

-- Disponibilidade oficial por serviço (nfe.fazenda.gov.br / cte.fazenda.gov.br), Bahia
create table if not exists disponibilidade_oficial (
  id bigint generated always as identity primary key,
  documento text not null,
  servico text not null,
  cor text not null check (cor in ('verde', 'amarela', 'vermelho')),
  verificado_em timestamptz not null default now()
);

create index if not exists idx_disponibilidade_verificado_em on disponibilidade_oficial (verificado_em desc);

-- Tempo de resposta real dos webservices (Normal / Lento / Muito lento / Timeout / Erro)
create table if not exists resposta_tempo (
  id bigint generated always as identity primary key,
  documento text not null check (documento in ('NFe', 'NFCe', 'CTe')),
  nivel text not null check (nivel in ('Normal', 'Lento', 'MuitoLento', 'Timeout', 'Erro')),
  segundos numeric,
  detalhe text,
  verificado_em timestamptz not null default now()
);

create index if not exists idx_resposta_tempo_documento_verificado on resposta_tempo (documento, verificado_em desc);

-- Downdetector (visão geral do Sefaz, não específica por UF)
create table if not exists downdetector_status (
  id bigint generated always as identity primary key,
  problema_detectado boolean not null,
  resumo text not null,
  comentarios_bahia jsonb not null default '[]'::jsonb,
  verificado_em timestamptz not null default now()
);

create index if not exists idx_downdetector_verificado_em on downdetector_status (verificado_em desc);

-- RLS: leitura pública (a chave anon só lê), escrita só via service_role (usada pela Edge Function)
alter table notas_tecnicas enable row level security;
alter table disponibilidade_oficial enable row level security;
alter table resposta_tempo enable row level security;
alter table downdetector_status enable row level security;

create policy "leitura publica notas_tecnicas" on notas_tecnicas for select using (true);
create policy "leitura publica disponibilidade_oficial" on disponibilidade_oficial for select using (true);
create policy "leitura publica resposta_tempo" on resposta_tempo for select using (true);
create policy "leitura publica downdetector_status" on downdetector_status for select using (true);
