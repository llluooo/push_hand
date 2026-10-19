param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('baseline','editmode-red','editmode-green','playmode','install','configure','generate','build','refresh','resources')]
    [string]$Job,
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
New-Item -ItemType Directory -Force -Path 'Artifacts/Verification' | Out-Null
$taskStatus = Join-Path $taskRoot "Artifacts/Verification/$Job-status.txt"
if (Test-Path -LiteralPath $taskStatus) { Remove-Item -LiteralPath $taskStatus }
Set-Content -LiteralPath 'Artifacts/editor-job.txt' -Value $Job -Encoding utf8
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class PushHandsEditorWindow { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd); }'
$taskNormalizedRoot = $taskRoot.Replace('\','/')
$taskEditor = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine.Replace('\','/') -like "*$taskNormalizedRoot*" } | Select-Object -First 1
if (!$taskEditor) { throw '未找到当前工程的 Unity 编辑器，请先用 2022.3.62f3c1 打开工程。' }
$taskWindow = Get-Process -Id $taskEditor.ProcessId
[PushHandsEditorWindow]::SetForegroundWindow($taskWindow.MainWindowHandle) | Out-Null
$taskDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (!(Test-Path -LiteralPath $taskStatus)) {
    if ((Get-Date) -gt $taskDeadline) { throw "编辑器任务超时：$Job，请检查 Console。" }
    Start-Sleep -Milliseconds 500
}
$taskResult = Get-Content -LiteralPath $taskStatus -Raw
Write-Output $taskResult
if ($taskResult -match '失败：|失败=[1-9]') { exit 1 }
