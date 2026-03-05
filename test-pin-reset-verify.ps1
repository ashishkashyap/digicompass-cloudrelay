# Test pin-reset verify endpoint and show status + body (including 400 message)
$relayKey = "123456789123456789123456789123456789"
$body = @{ deviceId = "device-001"; code = "000000" } | ConvertTo-Json   # change code to the one from email

try {
  $r = Invoke-WebRequest -Method Post "http://localhost:7071/api/v1/pin-reset/verify" `
    -Headers @{ "X-Relay-Key" = $relayKey } `
    -ContentType "application/json" `
    -Body $body `
    -UseBasicParsing
  Write-Host "Status:" $r.StatusCode
  Write-Host "Body:" $r.Content
} catch {
  Write-Host "Status:" $_.Exception.Response.StatusCode.value__
  $stream = $_.Exception.Response.GetResponseStream()
  if ($stream) {
    $reader = [System.IO.StreamReader]::new($stream)
    Write-Host "Body:" $reader.ReadToEnd()
    $reader.Close()
  }
}
