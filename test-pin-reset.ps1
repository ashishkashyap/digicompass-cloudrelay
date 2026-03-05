# Test pin-reset endpoint and show status + body (works for 2xx and 4xx)
$relayKey = "123456789123456789123456789123456789"
$body = @{ deviceId = "device-001" } | ConvertTo-Json

try {
  $r = Invoke-WebRequest -Method Post "http://localhost:7071/api/v1/pin-reset/request" `
    -Headers @{ "X-Relay-Key" = $relayKey } `
    -ContentType "application/json" `
    -Body $body `
    -UseBasicParsing
  Write-Host "Status:" $r.StatusCode
  Write-Host "Body:" $r.Content
} catch {
  Write-Host "Status:" $_.Exception.Response.StatusCode.value__
  $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
  Write-Host "Body:" $reader.ReadToEnd()
  $reader.Close()
}
