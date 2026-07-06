-- Reduz o intervalo padrão de verificação de 5 para 2 minutos.
-- cron.schedule com o mesmo nome de job substitui o agendamento anterior.
select cron.schedule(
  'coala-verifica-checagem',
  '*/2 * * * *',
  $$
  select net.http_post(
    url := 'https://zzbhqsbfxbuvgjidpuss.supabase.co/functions/v1/verificar',
    headers := '{"Content-Type": "application/json"}'::jsonb
  );
  $$
);
