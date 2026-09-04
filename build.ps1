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
foreach ($sub in 'showcase', 'docs') {
  $d = Join-Path $out $sub
  if (Test-Path $d) { Remove-Item -Recurse -Force $d }
  Copy-Item -Recurse (Join-Path $root $sub) $d
}
Write-Host "  staged core + showcase + docs"

# 2. Compile the ZSL examples (and run compiler tests in the test config).
$pyExe = $null
foreach ($c in 'py','python','python3') { if (Get-Command $c -ErrorAction SilentlyContinue) { $pyExe = $c; break } }
if ($pyExe) {
  if ($Config -eq 'test') {
    & $pyExe (Join-Path $root 'compiler/tests/test_compile.py')
    & $pyExe (Join-Path $root 'tests/check-tokens.py')
  }
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

# 3. Runtime self-tests (test config): drive the headless browser test pages.
if ($Config -eq 'test') {
  $chrome = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe"
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
  if ($chrome) {
    foreach ($t in @(
        @{ file = 'tests/io-selftest.html';  marker = 'IO SELFTEST OK';  name = 'IO' },
        @{ file = 'tests/nav-selftest.html'; marker = 'NAV SELFTEST OK'; name = 'nav/reactivity' })) {
      $tmp = Join-Path $out (($t.name -replace '\W', '_') + '.dom.html')
      & $chrome --headless --disable-gpu --virtual-time-budget=5000 --dump-dom `
        ("file:///" + (Join-Path $root $t.file).Replace('\', '/')) 2>$null |
        Out-File -Encoding utf8 $tmp
      if (Select-String -Path $tmp -Pattern $t.marker -Quiet) {
        Write-Host "  $($t.name) self-test: OK"
      } else {
        Write-Warning "  $($t.name) self-test FAILED (see $tmp)"
      }
    }
  } else {
    Write-Warning "no Chrome/Edge - skipping runtime self-tests"
  }
}

# 4. C# binding + sample
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
  $csConf = if ($Config -eq 'release') { 'Release' } else { 'Debug' }
  dotnet build (Join-Path $root 'bindings/csharp/ZUI.csproj') -c $csConf -o (Join-Path $out 'csharp')
  dotnet build (Join-Path $root 'samples/csharp/ZuiSample.csproj') -c $csConf -o (Join-Path $out 'sample-csharp')
  Write-Host "  built C# binding + sample ($csConf)"
} else {
  Write-Warning "dotnet not found - skipping C# binding"
}

# 5. C++ binding (+ sample). Uses a standalone cmake if on PATH, otherwise the
#    one bundled with Visual Studio (found via vswhere); runs inside vcvars64.
function Find-CppToolchain {
  $cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
  $vcvars = $null
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vs) {
      $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
      if (-not $cmake) {
        $bundled = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
        if (Test-Path $bundled) { $cmake = $bundled }
      }
    }
  }
  if ($cmake) { return @{ cmake = $cmake; vcvars = $vcvars } }
  return $null
}

$tc = Find-CppToolchain
if ($tc) {
  $tests = if ($Config -eq 'test') { 'ON' } else { 'OFF' }
  $cppOut = Join-Path $out 'cpp'
  $scOut  = Join-Path $out 'sample-cpp'
  $wv2 = $env:WEBVIEW2_DIR; $wil = $env:WIL_DIR
  if (-not $wv2 -and (Test-Path (Join-Path $root 'pkgs/Microsoft.Web.WebView2'))) {
    $wv2 = Join-Path $root 'pkgs/Microsoft.Web.WebView2'
    $wil = Join-Path $root 'pkgs/Microsoft.Windows.ImplementationLibrary'
  }

  $steps = @(
    "`"$($tc.cmake)`" -S `"$root\bindings\cpp`" -B `"$cppOut`" -G Ninja -DCMAKE_BUILD_TYPE=Release -DZUI_BUILD_TESTS=$tests",
    "`"$($tc.cmake)`" --build `"$cppOut`""
  )
  if ($Config -eq 'test') { $steps += "ctest --test-dir `"$cppOut`" --output-on-failure" }
  if ($wv2 -and (Test-Path $wv2)) {
    $steps += "`"$($tc.cmake)`" -S `"$root\samples\cpp`" -B `"$scOut`" -G Ninja -DCMAKE_BUILD_TYPE=Release -DWEBVIEW2_DIR=`"$wv2`" -DWIL_DIR=`"$wil`""
    $steps += "`"$($tc.cmake)`" --build `"$scOut`""
  }

  $bat = Join-Path $env:TEMP "zui_cpp_$Config.bat"
  $body = @('@echo off', "set `"PATH=%PATH%;${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer`"")
  if ($tc.vcvars) { $body += "call `"$($tc.vcvars)`" >nul" }
  $body += ($steps | ForEach-Object { "$_ || exit /b 1" })
  Set-Content -Encoding Ascii $bat ($body -join "`r`n")
  $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  cmd /c "`"$bat`" 2>&1" | Write-Host
  $ErrorActionPreference = $prev
  if ($LASTEXITCODE -ne 0) { throw "C++ build failed" }
  Write-Host "  built C++ binding$(if ($wv2 -and (Test-Path $wv2)) { ' + sample' })"
} else {
  Write-Warning "no C++ toolchain (cmake / Visual Studio) - skipping C++ binding"
}

Write-Host "done."
