# Quality gate

`./build.ps1 -Config test` must pass without a failed command. It runs parser and
native-code-generation tests, the native-only repository policy, native C# sample
checks, CSV round-trip checks, C++ compilation, and CTest host/envelope checks.

The policy rejects active browser documents, browser package dependencies, and
browser-rendering references. Release artifacts are built separately under
`builds/release` and smoke-checked before publishing.
