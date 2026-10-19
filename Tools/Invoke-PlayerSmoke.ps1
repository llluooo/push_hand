param([ValidateRange(15,240)][int]$FrameRate = 60)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExecutable = Join-Path $taskRoot 'Builds/Windows/PushHandsGreybox.exe'
if (!(Test-Path -LiteralPath $taskExecutable)) { throw '请先通过 Unity 的“推手/构建 Windows 灰盒”生成开发包。' }
$taskOutput = Join-Path $taskRoot "Artifacts/Verification/PlayerSmoke$FrameRate"
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskPreviousReport = Join-Path $taskOutput 'smoke.txt'
if (Test-Path -LiteralPath $taskPreviousReport) { Remove-Item -LiteralPath $taskPreviousReport }
$taskArguments = '-windowed -screen-width 1600 -screen-height 900 -greybox-smoke -smokeFPS {0} -smokeOutput "{1}" -logFile "{1}/player.log"' -f $FrameRate,$taskOutput
# 实际画面验收需要可见窗口；测试结束后程序自行退出。
$taskPlayer = Start-Process -FilePath $taskExecutable -ArgumentList $taskArguments -WindowStyle Normal -PassThru
if (!$taskPlayer.WaitForExit(30000)) {
    # 这里只关闭本脚本启动的自动测试进程，不影响用户手动运行的游戏或编辑器。
    Stop-Process -Id $taskPlayer.Id
    throw '自动试玩超过 30 秒，已关闭本次测试程序。'
}
$taskResult = Join-Path $taskOutput 'smoke.txt'
if (!(Test-Path -LiteralPath $taskResult)) { throw '没有生成自动试玩报告，请检查 player.log。' }
Get-Content -LiteralPath $taskResult
if ($taskPlayer.ExitCode -ne 0 -or (Get-Content -LiteralPath $taskResult -Raw) -notmatch '通过=True') { exit 1 }
