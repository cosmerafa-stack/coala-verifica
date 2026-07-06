-- Agenda a Edge Function "verificar" para rodar a cada 5 minutos via pg_cron + pg_net.
create extension if not exists pg_cron with schema extensions;
create extension if not exists pg_net with schema extensions;

select cron.schedule(
  'coala-verifica-checagem',
  '*/5 * * * *',
  $$
  select net.http_post(
    url := 'https://zzbhqsbfxbuvgjidpuss.supabase.co/functions/v1/verificar',
    headers := '{"Content-Type": "application/json"}'::jsonb
  );
  $$
);
