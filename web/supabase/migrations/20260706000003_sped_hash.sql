-- Guarda o último hash conhecido de cada página do SPED, separado de notas_tecnicas.
-- Sem isso, a primeira verificação de cada página (sem hash anterior pra comparar)
-- sempre gerava uma nota falsa de "mudança" — era só a inicialização, não uma mudança real.
create table if not exists sped_hash_atual (
  fonte text primary key,
  hash text not null,
  atualizado_em timestamptz not null default now()
);

alter table sped_hash_atual enable row level security;
-- Estado interno de comparação, não é conteúdo pra exibir no site — sem policy de leitura pública.

-- Remove as notas de "mudança" geradas na inicialização (falso positivo).
delete from notas_tecnicas where titulo like 'Conteúdo da página atualizado (hash %';
