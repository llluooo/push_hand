param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('baseline','editmode-red','editmode-green','playmode','physical-tests','install','configure','generate','build','refresh','resources','physical-generate','physical-build','physical-handoff','physical-feedback-setup','physical-arena-setup')]
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
# 任务应以编辑器写回的状态为依据；不依赖受限环境中的 CIM 进程枚举或窗口抢焦点。
$taskDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (!(Test-Path -LiteralPath $taskStatus)) {
    if ((Get-Date) -gt $taskDeadline) { throw "编辑器任务超时：$Job，请检查 Console。" }
    Start-Sleep -Milliseconds 500
}
$taskResult = Get-Content -LiteralPath $taskStatus -Raw
Write-Output $taskResult
if ($taskResult -match '失败：|失败=[1-9]') { exit 1 }
