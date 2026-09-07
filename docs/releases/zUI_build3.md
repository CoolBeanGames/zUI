# zUI build 3 — native architecture contract

This release completes the native-rendering migration by removing the dormant
legacy document generator and making the repository design contract explicitly
native-only.

## Changes

- Replaced `design.txt` with the canonical architecture, component mapping,
  theme translation, event/lookup, build, test, and release guidance for native
  C# and C++ implementations.
- Replaced the compiler implementation with a smaller native-only ZSL/ZML
  parser and C#/C++ source generator. Only `csharp` and `cpp` are valid targets.
- Removed all dormant document generation symbols and templates.
- Updated the grammar reference to describe typed native-node output.
- Strengthened tests and policy checks to fail if a document generator returns.
- Preserved byte-equivalent ZSL/ZML output and fixed same-line sibling lookahead
  in the clean parser implementation.

## Verification

- 12/12 compiler/parser/native-output tests passed.
- Native-only repository policy passed.
- C# runtime, generated showcase, and zSheets built with zero warnings/errors.
- C++ runtime and sample built; both CTests passed.
- Debug and release C#, C++, and zSheets smoke checks all returned success.
