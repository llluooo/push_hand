param(
    [string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3c1/Editor/Data'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$physicalRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $physicalRoot
$physicalOutput = Join-Path $physicalRoot 'Artifacts/Verification/PhysicalCompile'
New-Item -ItemType Directory -Force -Path $physicalOutput | Out-Null
$physicalFramework = "$EditorData/MonoBleedingEdge/lib/mono/4.8-api"
$physicalReferences = @()
$physicalReferences += Get-ChildItem -LiteralPath $physicalFramework -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName }
$physicalReferences += Get-ChildItem -LiteralPath "$physicalFramework/Facades" -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName }
$physicalReferences += Get-ChildItem -LiteralPath "$EditorData/Managed/UnityEngine" -Filter 'UnityEngine*.dll' | ForEach-Object { '/reference:' + $_.FullName }
$physicalSdk = & dotnet --version
$physicalCompiler = Join-Path (Split-Path (Get-Command dotnet).Source -Parent) "sdk/$physicalSdk/Roslyn/bincore/csc.dll"
function Compile-PhysicalAssembly([string]$Name, [string]$Folder, [string[]]$ExtraReferences) {
    $physicalArgs = @('/nologo', '/nostdlib+', '/langversion:9', '/target:library', "/out:$physicalOutput/$Name.dll")
    $physicalArgs += $physicalReferences
    $physicalArgs += $ExtraReferences | ForEach-Object { '/reference:' + $_ }
    $physicalArgs += Get-ChildItem -LiteralPath $Folder -Filter '*.cs' | ForEach-Object FullName
    $physicalResponse = "$physicalOutput/$Name.rsp"
    $physicalArgs | ForEach-Object { '"' + $_ + '"' } | Set-Content -LiteralPath $physicalResponse -Encoding utf8
    & dotnet $physicalCompiler /noconfig "@$physicalResponse"
    if ($LASTEXITCODE -ne 0) { throw "编译失败：$Name" }
    Write-Output "真实 Unity 引用编译通过：$Name"
}
$physicalPackages = @("$physicalRoot/Library/ScriptAssemblies/Unity.InputSystem.dll", "$physicalRoot/Library/ScriptAssemblies/Unity.TextMeshPro.dll", "$physicalRoot/Library/ScriptAssemblies/UnityEngine.UI.dll")
Compile-PhysicalAssembly 'PushHands.Core' 'Assets/_Project/Scripts/Core' @()
$physicalCoreRefs = @("$physicalOutput/PushHands.Core.dll") + $physicalPackages
Compile-PhysicalAssembly 'PushHands.Runtime' 'Assets/_Project/Scripts/Runtime' $physicalCoreRefs
$physicalRuntimeRefs = $physicalCoreRefs + @("$physicalOutput/PushHands.Runtime.dll")
Compile-PhysicalAssembly 'PushHands.Physics' 'Assets/_Project/Scripts/Physics' $physicalRuntimeRefs
$physicalFeatureRefs = $physicalRuntimeRefs + @("$physicalOutput/PushHands.Physics.dll")
$physicalTestRefs = $physicalFeatureRefs + @("$physicalRoot/Library/PackageCache/com.unity.ext.nunit@1.0.6/net35/unity-custom/nunit.framework.dll", "$physicalRoot/Library/ScriptAssemblies/UnityEngine.TestRunner.dll")
Compile-PhysicalAssembly 'PushHands.EditMode' 'Assets/_Project/Tests/EditMode' $physicalTestRefs
Compile-PhysicalAssembly 'PushHands.PlayMode' 'Assets/_Project/Tests/PlayMode' $physicalTestRefs
$physicalEditorRefs = $physicalFeatureRefs + @("$physicalRoot/Library/ScriptAssemblies/UnityEditor.TestRunner.dll", "$physicalRoot/Library/ScriptAssemblies/UnityEngine.TestRunner.dll")
$physicalEditorRefs += Get-ChildItem -LiteralPath "$EditorData/Managed/UnityEngine" -Filter 'UnityEditor*.dll' | ForEach-Object FullName
$physicalEditorRefs += "$physicalRoot/Library/PackageCache/com.unity.ext.nunit@1.0.6/net35/unity-custom/nunit.framework.dll"
Compile-PhysicalAssembly 'PushHands.Editor' 'Assets/_Project/Editor' $physicalEditorRefs
Write-Output '仅源码和程序集引用编译；不代表 Unity 导入、Console、物理、画面、测试或构建已通过。'
