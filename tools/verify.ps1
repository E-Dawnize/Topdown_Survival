param(
    [string]$UnityExe = $env:UNITY_EXE,
    [switch]$PlayMode
)

# 本地验证入口。只写 Temp/系统临时目录，不修改 Assets/Config。
# Unity Test Framework 官方命令：
#   Unity.exe -batchmode -runTests -projectPath <path> -testResults <xml> -testPlatform EditMode
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
    if ($null -eq $python) { throw "找不到 Python；请安装 Python 3 或手动运行 tools/export_config.py" }

    $configOut = Join-Path $env:TEMP "topdown-config-check"
    if (Test-Path $configOut) { Remove-Item -Recurse -Force $configOut }
    & $python.Source tools/export_config.py --out $configOut
    if ($LASTEXITCODE -ne 0) { throw "导表校验失败" }

    if (Test-Path "Game.Runtime.csproj") {
        foreach ($project in @("Game.Runtime.csproj", "Game.Tests.EditMode.csproj",
                               "Game.Tests.PlayMode.csproj", "Game.Editor.csproj")) {
            if (-not (Test-Path $project)) { continue }
            & dotnet build $project -t:Rebuild --nologo -v:m
            if ($LASTEXITCODE -ne 0) { throw "编译失败：$project" }
        }
    } else {
        Write-Warning "未找到 .csproj；请先用 Unity 打开工程生成，再运行本脚本。"
    }

    if ([string]::IsNullOrWhiteSpace($UnityExe)) {
        Write-Warning "未设置 UnityExe/UNITY_EXE；已跳过 Unity EditMode/PlayMode 测试。"
        Write-Host "示例：tools\verify.ps1 -UnityExe 'C:\Program Files\Unity\Hub\Editor\2022.3.34f1c1\Editor\Unity.exe'"
        return
    }

    if (-not (Test-Path $UnityExe)) { throw "找不到 Unity 可执行文件：$UnityExe" }

    $editResults = Join-Path $env:TEMP "topdown-editmode.xml"
    $editLog = Join-Path $env:TEMP "topdown-editmode.log"
    & $UnityExe -batchmode -nographics -projectPath $root -runTests -testPlatform EditMode `
        -testResults $editResults -logFile $editLog -quit
    if ($LASTEXITCODE -ne 0) { throw "EditMode 测试失败；日志：$editLog，结果：$editResults" }

    if ($PlayMode) {
        $playResults = Join-Path $env:TEMP "topdown-playmode.xml"
        $playLog = Join-Path $env:TEMP "topdown-playmode.log"
        & $UnityExe -batchmode -nographics -projectPath $root -runTests -testPlatform PlayMode `
            -testResults $playResults -logFile $playLog -quit
        if ($LASTEXITCODE -ne 0) { throw "PlayMode 测试失败；日志：$playLog，结果：$playResults" }
    }

    Write-Host "验证完成。结果目录：$env:TEMP"
}
finally {
    Pop-Location
}
