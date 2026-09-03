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
# Staged twice: 'zui/' is the canonical name host bindings map a virtual host to;
# 'core/' keeps the bundled showcase's ../core/... links resolving.
foreach ($name in 'zui', 'core') {
  $dst = Join-Path $out $name
  if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
  Copy-Item -Recurse (Join-Path $root 'core') $dst
}
$showDst = Join-Path $out 'showcase'
if (Test-Path $showDst) { Remove-Item -Recurse -Force $showDst }
Copy-Item -Recurse (Join-Path $root 'showcase') $showDst
Write-Host "  staged core + showcase"

# 2. Compile the ZSL examples (and run compiler tests in the test config).
$pyExe = $null
foreach ($c in 'py','python','python3') { if (Get-Command $c -ErrorAction SilentlyContinue) { $pyExe = $c; break } }
if ($pyExe) {
  if ($Config -eq 'test') { & $pyExe (Join-Path $root 'compiler/tests/test_compile.py') }
  $gen = Join-Path $out 'examples'
  New-Item -ItemType Directory -Force -Path $gen | Out-Null
  Get-ChildItem (Join-Path $root 'examples') -Include *.zsl,*.zml -Recurse | ForEach-Object {
    $out2 = Join-Path $gen ($_.BaseName + $_.Extension.Replace('.', '-') + '.html')
    & $pyExe (Join-Path $root 'compiler/zslc.py') $_.FullName --backend html -o $out2
  }
  Write-Host "  compiled ZSL examples"
} else {
  Write-Warning "python not found - skipping ZSL compilation"
}

# 4. C# binding
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
  $csConf = if ($Config -eq 'release') { 'Release' } else { 'Debug' }
  dotnet build (Join-Path $root 'bindings/csharp/ZUI.csproj') -c $csConf -o (Join-Path $out 'csharp')
  Write-Host "  built C# binding ($csConf)"
} else {
  Write-Warning "dotnet not found - skipping C# binding"
}

# 5. C++ binding
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
