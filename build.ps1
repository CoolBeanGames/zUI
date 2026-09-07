param([ValidateSet('debug','test','release')][string]$Config = 'debug')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root "builds/$Config"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Host "zUI native build ($Config) -> $out"

$pyExe = @('py','python','python3') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
if (-not $pyExe) { throw 'Python 3 is required for the ZML/ZSL compiler' }
if ($Config -eq 'test') {
  & $pyExe (Join-Path $root 'compiler/tests/test_compile.py')
  if ($LASTEXITCODE -ne 0) { throw 'compiler tests failed' }
  & $pyExe (Join-Path $root 'tests/check-native.py')
  if ($LASTEXITCODE -ne 0) { throw 'native-only policy check failed' }
}

$generated = Join-Path $out 'generated'
New-Item -ItemType Directory -Force -Path $generated | Out-Null
foreach ($source in Get-ChildItem (Join-Path $root 'examples') -Include *.zsl,*.zml -File) {
  foreach ($backend in 'csharp','cpp') {
    $extension = if ($backend -eq 'csharp') { '.g.cs' } else { '.g.cpp' }
    & $pyExe (Join-Path $root 'compiler/zslc.py') $source.FullName --backend $backend -o (Join-Path $generated ($source.BaseName + '-' + $source.Extension.TrimStart('.') + $extension))
    if ($LASTEXITCODE -ne 0) { throw "zslc failed: $($source.Name) ($backend)" }
  }
}

# The zForge sample keeps its compiled UI in-tree; regenerate it so it can't drift.
& $pyExe (Join-Path $root 'compiler/zslc.py') (Join-Path $root 'samples/zforge/CharacterForge.zsl') `
  --backend csharp --class CharacterForgeUi --namespace ZForge.Generated `
  -o (Join-Path $root 'samples/zforge/generated/CharacterForgeUi.g.cs')
if ($LASTEXITCODE -ne 0) { throw 'zslc failed: CharacterForge.zsl' }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK is required' }
$csConf = if ($Config -eq 'release') { 'Release' } else { 'Debug' }
dotnet build (Join-Path $root 'bindings/csharp/ZUI.csproj') -c $csConf -o (Join-Path $out 'csharp') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# runtime build failed' }
dotnet build (Join-Path $root 'samples/csharp/ZuiSample.csproj') -c $csConf -o (Join-Path $out 'sample-csharp') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# sample build failed' }
dotnet build (Join-Path $root 'samples/zsheets/ZSheets.csproj') -c $csConf -o (Join-Path $out 'zsheets') --nologo
if ($LASTEXITCODE -ne 0) { throw 'zSheets build failed' }
dotnet build (Join-Path $root 'samples/zforge/ZForge.csproj') -c $csConf -o (Join-Path $out 'zforge') --nologo
if ($LASTEXITCODE -ne 0) { throw 'zForge build failed' }
if ($Config -eq 'test') {
  dotnet build (Join-Path $root 'tests/csharp/ZuiHostTests.csproj') -c $csConf -o (Join-Path $out 'csharp-tests') --nologo
  if ($LASTEXITCODE -ne 0) { throw 'C# host test build failed' }
  & (Join-Path $out 'csharp-tests/ZuiHostTests.exe')
  if ($LASTEXITCODE -ne 0) { throw 'C# incremental mutation host tests failed' }
  & (Join-Path $out 'sample-csharp/ZuiSample.exe') --self-test
  if ($LASTEXITCODE -ne 0) { throw 'native C# control self-test failed' }
  & (Join-Path $out 'zsheets/zSheets.exe') --self-test
  if ($LASTEXITCODE -ne 0) { throw 'zSheets self-test failed' }
  & (Join-Path $out 'zforge/ZForge.exe') --self-test
  if ($LASTEXITCODE -ne 0) { throw 'zForge self-test failed' }
}

function Find-CppToolchain {
  $cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
  $vcvars = $null
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vs) {
      $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
      if (-not $cmake) {
        $candidate = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
        if (Test-Path $candidate) { $cmake = $candidate }
      }
    }
  }
  if ($cmake) { return @{ cmake = $cmake; vcvars = $vcvars } }
  return $null
}

$tc = Find-CppToolchain
if (-not $tc) { throw 'CMake and an MSVC C++ toolchain are required' }
$cppOut = Join-Path $out 'cpp'; $sampleOut = Join-Path $out 'sample-cpp'
$tests = if ($Config -eq 'test') { 'ON' } else { 'OFF' }
$steps = @(
  "`"$($tc.cmake)`" -S `"$root\bindings\cpp`" -B `"$cppOut`" -G Ninja -DCMAKE_BUILD_TYPE=Release -DZUI_BUILD_TESTS=$tests",
  "`"$($tc.cmake)`" --build `"$cppOut`""
)
if ($Config -eq 'test') { $steps += "ctest --test-dir `"$cppOut`" --output-on-failure" }
$steps += "`"$($tc.cmake)`" -S `"$root\samples\cpp`" -B `"$sampleOut`" -G Ninja -DCMAKE_BUILD_TYPE=Release"
$steps += "`"$($tc.cmake)`" --build `"$sampleOut`""
$bat = Join-Path $env:TEMP "zui_native_$Config.bat"
$lines = @('@echo off')
if ($tc.vcvars) { $lines += "call `"$($tc.vcvars)`" >nul" }
$lines += $steps | ForEach-Object { "$_ || exit /b 1" }
Set-Content -Encoding Ascii $bat ($lines -join "`r`n")
$oldPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
cmd /c "`"$bat`" 2>&1" | Write-Host
$ErrorActionPreference = $oldPreference
if ($LASTEXITCODE -ne 0) { throw 'C++ native build/test failed' }
Write-Host 'done: native C#, native C++, compiler and samples'
