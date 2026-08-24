# M1.4.3 cross-language integration driver.
#
# Starts the C# integration server (real TCP listener), runs the Dart
# integration client against it, then asserts both sides' machine-readable
# markers:
#   - DART:* lines prove Dart decoded the C# frames correctly.
#   - C#:RX / C#:INPUT_* markers prove C# decoded the Dart frames correctly.
#
# Implementation note: the server's stdout/stderr are kept as unread pipes
# (its total output is a couple of KB, far below the pipe capacity) and are
# drained with ReadToEnd() only after the process exits. Readiness is probed
# with a real TCP connect instead of parsing the LISTENING marker. This avoids
# .NET event-handler scriptblocks (unreliable on threadpool threads under
# Windows PowerShell 5.1) and cmd.exe wrapper quirks.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-integration.ps1
param(
    [string]$DartExe = 'C:\flutter\bin\cache\dart-sdk\bin\dart.exe',
    [string]$DotNetExe = 'C:\Program Files\dotnet\dotnet.exe'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot 'desktop\CTRL.Desktop.csproj'
$mobileDir = Join-Path $repoRoot 'mobile'

if (-not (Test-Path -LiteralPath $DartExe)) { throw "dart.exe not found: $DartExe" }
if (-not (Test-Path -LiteralPath $csproj)) { throw "project not found: $csproj" }

function Require-Count([string]$text, [string]$pattern, [int]$min, [string]$what) {
    $count = @([regex]::Matches($text, "(?m)$pattern")).Count
    if ($count -lt $min) {
        throw "expected >= $min occurrences of '$what'; got $count"
    }
    Write-Host ("  ok: {0} x{1}" -f $what, $count)
}

function Require-Zero([string]$text, [string]$pattern, [string]$what) {
    $count = @([regex]::Matches($text, "(?m)$pattern")).Count
    if ($count -ne 0) {
        throw "expected 0 occurrences of '$what'; got $count"
    }
    Write-Host ("  ok: {0} x0" -f $what)
}

# Pre-build so the server process output contains only runtime markers.
& $DotNetExe build $csproj | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

# Pick a free loopback port for the server.
$probe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
$probe.Start()
$listenPort = ($probe.LocalEndpoint -as [System.Net.IPEndPoint]).Port
$probe.Stop()

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $DotNetExe
$psi.Arguments = "run --project `"$csproj`" --no-build -- --integration $listenPort"
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true
$psi.UseShellExecute = $false
$psi.WorkingDirectory = $repoRoot

$server = New-Object System.Diagnostics.Process
$server.StartInfo = $psi
if (-not $server.Start()) { throw 'failed to start the C# integration server' }

try {
    # Wait until the server accepts TCP connections (readiness probe).
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    while (-not $ready -and [DateTime]::UtcNow -lt $deadline) {
        if ($server.HasExited) {
            throw "server exited early with code $($server.ExitCode)"
        }
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $task = $client.ConnectAsync('127.0.0.1', $listenPort)
            if ($task.Wait(200) -and $client.Connected) { $ready = $true }
        }
        catch {}
        finally { $client.Dispose() }
        if (-not $ready) { Start-Sleep -Milliseconds 50 }
    }
    if (-not $ready) { throw "server never accepted connections on port $listenPort" }
    Write-Host "server listening on 127.0.0.1:$listenPort"

    # Run the Dart client against the real TCP server.
    # Note: `dart run` may emit progress lines on STDERR ("Running build
    # hooks..."); under PowerShell 5.1 with $ErrorActionPreference=Stop those
    # become terminating NativeCommandErrors, so relax EAP for this call.
    Push-Location $mobileDir
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $dartOutput = & $DartExe run tool/integration_client.dart --port $listenPort 2>&1
        $dartCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $ErrorActionPreference = $prevEap
    }
    foreach ($line in @($dartOutput)) { Write-Host ("  dart: " + [string]$line) }

    # Ask the server to stop and wait for a clean exit.
    $server.StandardInput.WriteLine('STOP')
    if (-not $server.WaitForExit(20000)) {
        $server.Kill()
        throw 'server did not stop after STOP'
    }

    # Drain both pipes fully now that the process has exited.
    $serverOut = $server.StandardOutput.ReadToEnd()
    $serverErr = $server.StandardError.ReadToEnd()
    Write-Host 'server output:'
    foreach ($line in @($serverOut -split "`r?`n")) {
        if ($line) { Write-Host ("  csharp: " + $line) }
    }

    if ($dartCode -ne 0) {
        throw "dart integration client failed with exit code $dartCode"
    }
    if (@($dartOutput | Where-Object { [string]$_ -eq 'DART:INTEGRATION:PASS' }).Count -ne 1) {
        throw 'dart client did not report DART:INTEGRATION:PASS'
    }

    Write-Host 'server-side marker assertions:'
    Require-Count $serverOut '^C#:LISTENING:\d+' 1 'listener started'
    Require-Count $serverOut '^C#:AUTHENTICATED:integration-device' 3 'authenticated sessions'
    Require-Count $serverOut '^C#:INPUT_SNAPSHOT:2' 3 'decoded INPUT_SNAPSHOT with 2 entries'
    Require-Count $serverOut '^C#:INPUT_EVENT:btn-fire:0:1:1' 2 'decoded button INPUT_EVENT'
    Require-Count $serverOut '^C#:INPUT_EVENT:thr:3:0:0' 1 'decoded trigger INPUT_EVENT'
    Require-Count $serverOut '^C#:RX:HEARTBEAT:' 1 'received HEARTBEAT'
    Require-Count $serverOut '^C#:TX:PONG:' 1 'sent PONG'
    Require-Count $serverOut '^C#:TX:ERROR:' 2 'protocol ERRORs sent (device-limit + unsupported-message)'
    Require-Count $serverOut '^C#:FLUSH' 3 'input-state flushes (graceful + takeover + violation)'
    Require-Count $serverOut '^C#:CLOSED:integration-device' 3 'closed sessions'
    Require-Zero $serverOut '^C#:TX:ACK' 'unsolicited ACKs (input/heartbeat must never be ACKed)'
    Require-Zero $serverErr '(?i)unhandled|exception' 'server runtime errors'

    Write-Host 'INTEGRATION:PASS'
    exit 0
}
finally {
    try {
        if (-not $server.HasExited) {
            try { $server.StandardInput.WriteLine('STOP') } catch {}
            if (-not $server.WaitForExit(5000)) { $server.Kill() }
        }
    }
    catch {}
    $server.Dispose()
}
