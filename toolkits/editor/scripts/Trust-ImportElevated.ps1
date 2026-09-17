$ErrorActionPreference = "Stop"
$log = "C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\editor\scripts\trust-import.log"
$cer = "C:\Users\RICARD~1\AppData\Local\Temp\oslive-sign.cer"
try {
    Add-Type -AssemblyName System.Security
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root","LocalMachine")
    $store.Open("ReadWrite")
    $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
    $exists = $store.Certificates | Where-Object { $_.Thumbprint -eq $c.Thumbprint }
    if (-not $exists) { $store.Add($c); "added to LocalMachine\Root" | Set-Content $log }
    else { "already in LocalMachine\Root" | Set-Content $log }
    $store.Close()
    $store2 = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher","LocalMachine")
    $store2.Open("ReadWrite")
    $exists2 = $store2.Certificates | Where-Object { $_.Thumbprint -eq $c.Thumbprint }
    if (-not $exists2) { $store2.Add($c); "added to LocalMachine\TrustedPublisher" | Add-Content $log }
    else { "already in LocalMachine\TrustedPublisher" | Add-Content $log }
    $store2.Close()
    "DONE" | Add-Content $log
}
catch {
    "ERROR: $($_.Exception.Message)" | Set-Content $log
}
