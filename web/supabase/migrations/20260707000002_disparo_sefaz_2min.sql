-- Repositório virou público: Actions do GitHub fica ilimitado e grátis num
-- runner Windows, então dá pra checar NFe/NFCe/CTe a cada 2 min (igual o
-- monitor da Tecnospeed) em vez de a cada 30 min.
select cron.schedule(
  'coala-verifica-disparo-sefaz',
  '*/2 * * * *',
  $$
  select net.http_post(
    url := 'https://zzbhqsbfxbuvgjidpuss.supabase.co/functions/v1/disparar-verificacao-sefaz',
    headers := '{"Content-Type": "application/json"}'::jsonb
  );
  $$
);
