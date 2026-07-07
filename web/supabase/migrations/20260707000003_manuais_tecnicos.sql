-- Manuais técnicos (MOC, anexos, manuais de contingência etc.) do portal
-- nfe.fazenda.gov.br — mesma estrutura de notas_tecnicas, mas em tabela
-- separada porque é outro tipo de conteúdo (não são "notas técnicas").
create table if not exists manuais_tecnicos (
  id bigint generated always as identity primary key,
  fonte text not null,
  titulo text not null,
  descricao text,
  data_publicacao text,
  url text not null,
  detectado_em timestamptz not null default now(),
  unique (fonte, titulo, url)
);

create index if not exists idx_manuais_tecnicos_detectado_em on manuais_tecnicos (detectado_em desc);
create index if not exists idx_manuais_tecnicos_fonte on manuais_tecnicos (fonte);

alter table manuais_tecnicos enable row level security;
create policy "leitura publica manuais_tecnicos" on manuais_tecnicos for select using (true);
