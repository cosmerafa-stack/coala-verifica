-- O agendamento nativo do GitHub Actions ("schedule:") não é confiável — chegou
-- a passar mais de 10h sem disparar um job configurado pra rodar a cada 30 min.
-- O pg_cron do Supabase (já usado por "coala-verifica-checagem") nunca falhou,
-- então passa a ser ele quem dispara o workflow do GitHub Actions, via a
-- function "disparar-verificacao-sefaz" (que chama a API workflow_dispatch).
select cron.schedule(
  'coala-verifica-disparo-sefaz',
  '*/30 * * * *',
  $$
  select net.http_post(
    url := 'https://zzbhqsbfxbuvgjidpuss.supabase.co/functions/v1/disparar-verificacao-sefaz',
    headers := '{"Content-Type": "application/json"}'::jsonb
  );
  $$
);
