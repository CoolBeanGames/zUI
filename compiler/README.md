# zslc

`zslc.py` parses both brace-style ZSL and angle-bracket ZML into one AST and
emits native source:

```powershell
py compiler/zslc.py screen.zml --backend csharp -o Screen.g.cs
py compiler/zslc.py screen.zml --backend cpp -o screen.g.cpp
```

The result contains typed node construction and event wiring. It contains no
markup document, scripts, stylesheets, or runtime parser.
