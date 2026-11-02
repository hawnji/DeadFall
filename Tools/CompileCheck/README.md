# Compile check (no Unity editor)

For environments without Unity (like Claude's cloud container): compiles every assembly definition against only its
declared references and runs the engine-free Rules unit tests.

```bash
Tools/CompileCheck/setup.sh     # once: Mono, Roslyn, NUnit, Unity 2021.3 reference DLLs (from NuGet)
Tools/CompileCheck/check.py     # compile all asmdefs + run Rules tests
```

Limits: uses Unity 2021.3 reference assemblies and hand-written stubs for packages (`stubs/`), so a pass here
means "very likely compiles", not "verified in Unity 6". Always confirm in the editor.
