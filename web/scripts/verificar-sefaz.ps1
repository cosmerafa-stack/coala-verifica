# Verificador Coala — checagens de NFe/NFCe e CTe (disponibilidade oficial, tempo de
# resposta e notas técnicas) que a Sefaz/Fazenda bloqueia quando vêm de clientes HTTP
# comuns (curl/OpenSSL, Deno, Node, Python) — provavelmente por fingerprint de TLS
# (JA3). O stack nativo do Windows (Schannel, usado por Invoke-WebRequest/.NET, e
# também pelo app desktop em C#) passa normalmente. Por isso esse script roda em
# PowerShell num runner windows-latest do GitHub Actions, não em Deno.
$ErrorActionPreference = "Stop"

$SUPABASE_URL = $env:SUPABASE_URL
$SUPABASE_SERVICE_ROLE_KEY = $env:SUPABASE_SERVICE_ROLE_KEY

$erros = New-Object System.Collections.Generic.List[string]
$totalNotas = 0
$totalDisponibilidade = 0
$totalResposta = 0

function Limpar-Espacos([string]$texto) {
  return ($texto -replace '\s+', ' ').Trim()
}

# A conexão com nfe.fazenda.gov.br/cte.fazenda.gov.br falha às vezes com "Unable
# to connect to the remote server" — não é instabilidade passageira, é o IP do
# runner do GitHub (Azure, muda a cada execução) caindo numa faixa bloqueada
# pela Fazenda dessa vez. Tentar de novo NO MESMO IP não resolve, então o retry
# aqui é só uma rede de segurança rápida (timeout curto, poucas tentativas) —
# se a execução inteira falhar, a próxima (com outro IP) tende a funcionar.
# Não retenta em cima de uma resposta HTTP de erro (403 etc.): isso não é falha
# de rede, é resposta legítima do servidor.
function Invoke-WebRequestComRetry([string]$uri, [int]$timeoutSec = 12, [int]$tentativas = 2) {
  for ($i = 1; $i -le $tentativas; $i++) {
    try {
      return Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec $timeoutSec
    } catch {
      if ($_.Exception.Response) { throw }
      if ($i -eq $tentativas) { throw }
      Start-Sleep -Seconds 2
    }
  }
}

# ConvertTo-Json (Windows PowerShell 5.1) desembrulha arrays de 1 elemento num
# objeto solto em vez de manter "[...]" — monta o array JSON manualmente pra
# funcionar igual em PS 5.1 e PowerShell 7 (pwsh).
function ConvertTo-JsonArray($itens) {
  $partes = @($itens) | ForEach-Object { $_ | ConvertTo-Json -Depth 5 -Compress }
  return "[" + ($partes -join ",") + "]"
}

# Invoke-RestMethod (Windows PowerShell 5.1) se mostrou inconsistente mandando
# corpos JSON com acento pro PostgREST (às vezes voltava "Empty or invalid json"
# mesmo com bytes UTF-8 corretos e -ContentType certo, sem dar pra reproduzir
# isoladamente). curl.exe é o mesmo cliente HTTP usado o resto dessa depuração e
# nunca falhou — grava o JSON num arquivo temporário em UTF-8 e chama curl direto.
function Enviar-Supabase([string]$caminho, [string]$corpoJson, [string]$prefer) {
  $arquivoTemp = [System.IO.Path]::GetTempFileName()
  try {
    [System.IO.File]::WriteAllText($arquivoTemp, $corpoJson, (New-Object System.Text.UTF8Encoding($false)))
    $saida = & curl.exe -s -o - -w "`n%{http_code}" -X POST "$SUPABASE_URL/rest/v1/$caminho" `
      -H "apikey: $SUPABASE_SERVICE_ROLE_KEY" `
      -H "Authorization: Bearer $SUPABASE_SERVICE_ROLE_KEY" `
      -H "Content-Type: application/json; charset=utf-8" `
      -H "Prefer: $prefer" `
      --data-binary "@$arquivoTemp"
    $linhas = $saida -split "`n"
    $codigoHttp = $linhas[-1]
    $corpoResposta = ($linhas[0..($linhas.Count - 2)]) -join "`n"
    if ([int]$codigoHttp -ge 300) {
      throw "HTTP $codigoHttp ao gravar em $caminho`: $corpoResposta"
    }
  } finally {
    Remove-Item $arquivoTemp -ErrorAction SilentlyContinue
  }
}

function Enviar-NotasTecnicas($linhas) {
  if ($linhas.Count -eq 0) { return }
  Enviar-Supabase "notas_tecnicas?on_conflict=fonte,titulo,url" (ConvertTo-JsonArray $linhas) "resolution=ignore-duplicates,return=minimal"
}

function Enviar-Disponibilidade($linhas) {
  if ($linhas.Count -eq 0) { return }
  Enviar-Supabase "disponibilidade_oficial" (ConvertTo-JsonArray $linhas) "return=minimal"
}

function Enviar-RespostaTempo($linha) {
  Enviar-Supabase "resposta_tempo" (ConvertTo-JsonArray @($linha)) "return=minimal"
}

# ---------- notas técnicas: NFe/NFCe e CTe (listaConteudo.aspx) ----------

function Verificar-ListaConteudo([string]$nome, [string]$baseUrl, [string]$listaUrl) {
  try {
    $resp = Invoke-WebRequestComRetry $listaUrl
    $html = $resp.Content

    # NFe/NFCe separa "Documentos vigentes" de "Documentos não vigentes"; só
    # interessa a seção de vigentes. CTe não tem essa divisão (pega tudo).
    $escopo = $html
    $idxVigentes = $html.IndexOf("Documentos vigentes")
    if ($idxVigentes -ge 0) {
      $idxNaoVigentes = $html.IndexOf("Documentos não vigentes", $idxVigentes)
      if ($idxNaoVigentes -ge 0) {
        $escopo = $html.Substring($idxVigentes, $idxNaoVigentes - $idxVigentes)
      } else {
        $escopo = $html.Substring($idxVigentes)
      }
    }

    $opcoes = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $padrao = '<a[^>]*href="([^"]+)"[^>]*>\s*<span class="tituloConteudo">([^<]*)</span>\s*</a>\s*(?:<br\s*/?>)?\s*(.*?)</p>'
    $matches = [regex]::Matches($escopo, $padrao, $opcoes)

    $linhas = @()
    foreach ($m in $matches) {
      $href = $m.Groups[1].Value
      $titulo = Limpar-Espacos ($m.Groups[2].Value -replace '&amp;', '&')
      $descricaoBruta = $m.Groups[3].Value -replace '<[^>]+>', ' '
      $descricao = Limpar-Espacos $descricaoBruta
      if ($descricao.Length -gt 220) { $descricao = $descricao.Substring(0, 220).Trim() + "..." }

      $dataMatch = [regex]::Match($titulo, '(\d{2}/\d{2}/\d{4})')
      $url = [Uri]::new([Uri]$baseUrl, $href).ToString()

      $linhas += [ordered]@{
        fonte           = $nome
        titulo          = $titulo
        descricao       = $(if ($descricao) { $descricao } else { $null })
        data_publicacao = $(if ($dataMatch.Success) { $dataMatch.Value } else { $null })
        url             = $url
      }
    }

    if ($linhas.Count -gt 0) {
      Enviar-NotasTecnicas $linhas
      $script:totalNotas += $linhas.Count
    }
  } catch {
    $erros.Add("$nome`: $($_.Exception.Message)")
  }
}

# ---------- disponibilidade oficial (NFe/NFCe e CTe), Bahia ----------

function Verificar-Disponibilidade([string]$documento, [string]$url, [string]$linhaChave) {
  try {
    $resp = Invoke-WebRequestComRetry $url
    $html = $resp.Content

    $idxTabela = $html.IndexOf("gdvDisponibilidade")
    if ($idxTabela -lt 0) {
      $erros.Add("$documento`: tabela de disponibilidade não encontrada")
      return
    }
    $idxFimTabela = $html.IndexOf("</table>", $idxTabela)
    $tabelaHtml = $html.Substring($idxTabela, $idxFimTabela - $idxTabela)

    $opcoes = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $linhasTr = [regex]::Matches($tabelaHtml, '<tr[^>]*>(.*?)</tr>', $opcoes)
    if ($linhasTr.Count -eq 0) { return }

    $cabecalhos = [regex]::Matches($linhasTr[0].Groups[1].Value, '<th[^>]*>(.*?)</th>', $opcoes) |
      ForEach-Object { (Limpar-Espacos ($_.Groups[1].Value -replace '<[^>]+>', '')) -replace '4$', '' }

    $registros = @()
    for ($i = 1; $i -lt $linhasTr.Count; $i++) {
      $celulas = [regex]::Matches($linhasTr[$i].Groups[1].Value, '<td[^>]*>(.*?)</td>', $opcoes)
      if ($celulas.Count -eq 0) { continue }

      $chave = Limpar-Espacos ($celulas[0].Groups[1].Value -replace '<[^>]+>', '')
      if ($chave.ToLowerInvariant() -ne $linhaChave.ToLowerInvariant()) { continue }

      for ($c = 1; $c -lt $celulas.Count -and $c -lt $cabecalhos.Count; $c++) {
        $nomeServico = $cabecalhos[$c]
        if ($nomeServico.ToLowerInvariant().Contains("tempo médio")) { continue }

        $conteudoCelula = $celulas[$c].Groups[1].Value.ToLowerInvariant()
        $cor = $null
        if ($conteudoCelula.Contains("verde")) { $cor = "verde" }
        elseif ($conteudoCelula.Contains("amarel")) { $cor = "amarela" }
        elseif ($conteudoCelula.Contains("vermelh")) { $cor = "vermelho" }
        if (-not $cor) { continue }

        $registros += [ordered]@{ documento = $documento; servico = $nomeServico; cor = $cor }
      }
      break
    }

    if ($registros.Count -gt 0) {
      Enviar-Disponibilidade $registros
      $script:totalDisponibilidade += $registros.Count
    }
  } catch {
    $erros.Add("$documento disponibilidade: $($_.Exception.Message)")
  }
}

# ---------- tempo de resposta real dos webservices ----------

function Registrar-Resposta([string]$documento, [double]$segundos, [int]$statusCode) {
  $nivel = if ($segundos -le 2) { "Normal" } elseif ($segundos -le 5) { "Lento" } elseif ($segundos -lt 30) { "MuitoLento" } else { "Timeout" }
  $detalhe = "Respondeu em {0:N1}s (HTTP {1})" -f $segundos, $statusCode
  Enviar-RespostaTempo ([ordered]@{ documento = $documento; nivel = $nivel; segundos = $segundos; detalhe = $detalhe })
  $script:totalResposta += 1
}

function Medir-UmaTentativa([string]$url) {
  $cronometro = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 35
    $cronometro.Stop()
    return [ordered]@{ sucesso = $true; segundos = $cronometro.Elapsed.TotalSeconds; statusCode = [int]$resp.StatusCode }
  } catch {
    $cronometro.Stop()
    $segundos = $cronometro.Elapsed.TotalSeconds
    $respostaHttp = $_.Exception.Response
    if ($respostaHttp -and $respostaHttp.StatusCode) {
      # O servidor respondeu, só que com status de erro (ex.: 403 sem certificado
      # digital — esperado nesses webservices sem operação de negócio real). O
      # tempo até a resposta chegar já indica se o servidor está de pé, no mesmo
      # espírito do app desktop original.
      return [ordered]@{ sucesso = $true; segundos = $segundos; statusCode = [int]$respostaHttp.StatusCode }
    }
    return [ordered]@{ sucesso = $false; segundos = $segundos; erro = $_.Exception.Message }
  }
}

# O runner do GitHub Actions não fica no Brasil, então o tempo medido inclui a
# viagem de ida e volta até lá fora — uma variação de rede pontual naquele
# runner específico pode parecer "Sefaz lenta" sem ter nada a ver com o
# servidor. Faz 2 tentativas rápidas e fica com a mais rápida das que tiveram
# sucesso, pra reduzir esse ruído (o pior caso real da Sefaz continua sendo
# capturado se as duas tentativas vierem lentas).
function Verificar-Resposta([string]$documento, [string]$url) {
  $tentativas = 1, 2 | ForEach-Object { Medir-UmaTentativa $url }
  $sucessos = $tentativas | Where-Object { $_.sucesso }

  if ($sucessos.Count -gt 0) {
    $melhor = $sucessos | Sort-Object segundos | Select-Object -First 1
    Registrar-Resposta $documento $melhor.segundos $melhor.statusCode
  } else {
    $pior = $tentativas | Sort-Object segundos | Select-Object -First 1
    $nivel = if ($pior.segundos -ge 30) { "Timeout" } else { "Erro" }
    Enviar-RespostaTempo ([ordered]@{
      documento = $documento
      nivel     = $nivel
      segundos  = $(if ($nivel -eq "Erro") { $null } else { $pior.segundos })
      detalhe   = "Falha de conexão: $($pior.erro)"
    })
    $script:totalResposta += 1
  }
}

# ---------- execução ----------

Verificar-Disponibilidade "NFe / NFCe" "https://www.nfe.fazenda.gov.br/portal/disponibilidade.aspx" "BA"
Verificar-Disponibilidade "CTe" "https://www.cte.fazenda.gov.br/portal/disponibilidade.aspx" "SVRS"

Verificar-Resposta "NFe" "https://nfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx"
Verificar-Resposta "NFCe" "https://nfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx"
Verificar-Resposta "CTe" "https://cte.svrs.rs.gov.br/ws/CTeStatusServicoV4/CTeStatusServicoV4.asmx"

Verificar-ListaConteudo "NFe/NFCe" "https://www.nfe.fazenda.gov.br/portal/" "https://www.nfe.fazenda.gov.br/portal/listaConteudo.aspx?tipoConteudo=04BIflQt1aY="
Verificar-ListaConteudo "CTe" "https://www.cte.fazenda.gov.br/portal/" "https://www.cte.fazenda.gov.br/portal/listaConteudo.aspx?tipoConteudo=Y0nErnoZpsg="

$resultado = [ordered]@{
  notas           = $totalNotas
  disponibilidade = $totalDisponibilidade
  resposta        = $totalResposta
  erros           = $erros
}
$resultado | ConvertTo-Json -Depth 5

if ($erros.Count -gt 0) { exit 1 }

