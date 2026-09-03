<#
  zUI build script.

    ./build.ps1 -Config debug     # -> builds/debug
    ./build.ps1 -Config test      # -> builds/test   (for test runs)
    ./build.ps1 -Config release   # -> builds/release

  Stages the shared core assets and builds whichever bindings have a toolchain
  available (dotnet / cmake). Missing toolchains are skipped with a warning, so
  the asset stage always succeeds.
#>
param(
  [ValidateSet('debug','test','release')]
  [string]$Config = 'debug'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root "builds/$Config"

Write-Host "zUI build ($Config) -> $out"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# 1. Shared core (CSS + JS) - the heart of the library.
$coreDst = Join-Path $out 'zui'
if (Test-Path $coreDst) { Remove-Item -Recurse -Force $coreDst }
Copy-Item -Recurse (Join-Path $root 'core') $coreDst
Copy-Item -Recurse (Join-Path $root 'showcase') (Join-Path $out 'showcase')
Write-Host "  staged core + showcase"

# 2. C# binding
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
  $csConf = if ($Config -eq 'release') { 'Release' } else { 'Debug' }
  dotnet build (Join-Path $root 'bindings/csharp/ZUI.csproj') -c $csConf -o (Join-Path $out 'csharp')
  Write-Host "  built C# binding ($csConf)"
} else {
  Write-Warning "dotnet not found - skipping C# binding"
}

# 3. C++ binding
if (Get-Command cmake -ErrorAction SilentlyContinue) {
  $cppOut = Join-Path $out 'cpp'
  $tests  = if ($Config -eq 'test') { 'ON' } else { 'OFF' }
  cmake -S (Join-Path $root 'bindings/cpp') -B $cppOut -DZUI_BUILD_TESTS=$tests
  cmake --build $cppOut
  if ($Config -eq 'test') { ctest --test-dir $cppOut --output-on-failure }
  Write-Host "  built C++ binding"
} else {
  Write-Warning "cmake not found - skipping C++ binding"
}

Write-Host "done."
