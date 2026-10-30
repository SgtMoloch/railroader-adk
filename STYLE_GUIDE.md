# Railroader ADK Tools — style guide (v3.0 Beta 1.1)

The cleanup is modeled on the provided PassengerHelper source rather than a generic autoformatter.

- **Readable flow first:** keep guard clauses / early returns and expand one-line conditional bodies into visible blocks.
- **Allman braces:** place the opening/closing braces on their own lines; avoid one-line `try`, `catch`, and utility methods.
- **Types that are obvious:** use explicit type declarations for direct `new Type(...)` construction; retain `var` where the inferred type is clearer or an anonymous type is involved.
- **Pragmatic C#:** retain useful LINQ, local functions, expression-bodied properties, and target-typed construction. Don't turn compact data definitions into procedural code.
- **Avoid magic renames:** names serialized by Unity (`CustomColorizer` fields, `.asmdef` names, serialized map names, `.meta` GUIDs) and referenced by Railroader definitions are a compatibility contract. Beta 1 does not rename them.
- **Editor namespaces:** use `Railroader.AssetTools` for editor utilities and `Railroader.AssetTools.Colorizer` for Colorizer code; keep serialized type identities stable. The asmdef `rootNamespace` setting does not wrap C# types automatically.
- **No functional rewrites:** algorithm, persistence, prefab hierarchy, and game-facing definition behavior remain those of 2.9.2.

The formatting is a best-effort stylistic match; PassengerHelper itself contains several variations in naming and declaration style. The ADK project uses editor-specific conventions that need to remain compatible with Unity.
