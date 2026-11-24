$Host.UI.RawUI.WindowTitle = "PC-Ovladac — Sken zařízení v domácí síti"
Write-Host "Skenuji domácí síť, prosím čekejte..." -ForegroundColor Cyan

$localIp = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -ne "127.0.0.1" -and $_.IPAddress -notlike "169.254.*" -and $_.PrefixOrigin -ne "WellKnown" } |
    Select-Object -First 1 -ExpandProperty IPAddress)

if (-not $localIp) {
    Write-Host "Nepodařilo se zjistit lokální IP adresu." -ForegroundColor Red
    Read-Host "Stiskněte Enter pro zavření"
    exit 1
}

$parts = $localIp.Split('.')
$prefix = "$($parts[0]).$($parts[1]).$($parts[2])"
Write-Host "Podsíť: $prefix.0/24 (tento PC: $localIp)`n" -ForegroundColor Gray

$tasks = 1..254 | ForEach-Object {
    [System.Net.NetworkInformation.Ping]::new().SendPingAsync("$prefix.$_", 350)
}
[System.Threading.Tasks.Task]::WaitAll($tasks, 3000) | Out-Null

$neighbors = Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -like "$prefix.*" -and
        $_.State -ne "Incomplete" -and
        $_.State -ne "Unreachable" -and
        $_.LinkLayerAddress -ne "FF-FF-FF-FF-FF-FF" -and
        $_.LinkLayerAddress -ne "00-00-00-00-00-00"
    } |
    Sort-Object { [int]($_.IPAddress.Split('.')[3]) }

$rows = @()
$rows += [PSCustomObject]@{
    IP    = $localIp
    MAC   = "(tento PC)"
    Stav  = "Aktivní"
    Nazev = $env:COMPUTERNAME
}

foreach ($n in $neighbors) {
    $name = ""
    try {
        $name = [System.Net.Dns]::GetHostEntry($n.IPAddress).HostName
    } catch { }
    $rows += [PSCustomObject]@{
        IP    = $n.IPAddress
        MAC   = $n.LinkLayerAddress
        Stav  = $n.State
        Nazev = $name
    }
}

$rows | Sort-Object { [int]($_.IP.Split('.')[3]) } | Format-Table -AutoSize
Write-Host "Nalezeno zařízení: $($rows.Count)`n" -ForegroundColor Green
Read-Host "Stiskněte Enter pro zavření okna"
