# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Pack NuGet package
dotnet pack
```

## Architecture

**HtmlTag** is a single-class, single-pass HTML opening tag parser for .NET 8+. The entire implementation lives in `HtmlTag/HtmlTag.cs` (~670 lines) in the `DotNetXtensions.Html` namespace.

### Parsing Flow

```
Parse(string | ReadOnlySpan<char>, int startIndex, bool findTagEnd)
  → Initialize()               — validates tag structure, finds closing >, detects self-closing
  → TryParseTagNameAtCurr()    — extracts and validates tag name
  → loop: TryFindAndParseNextAttr()
      → TryParseAttrAtIndicatedPos()
          → ProcessEqualsSignAndDetermineIfBoolean()
          → ParseQuotedAttributeValue() or ParseUnquotedAttributeValue()
          → AddAttribute()
```

### Key Design Decisions

- **Zero-allocation**: `ReadOnlySpan<char>` used throughout for validation and boundary detection; strings only allocated when storing results.
- **Lazy dictionary**: `Attributes` dictionary is `null` until the first attribute is found.
- **Reusable instance**: State fields are reset on each `Parse()` call — the same `HtmlTag` object can parse many tags.
- **Boolean attributes**: Stored with a `null` value in the dictionary.
- **Duplicate attributes**: Last value wins (matches browser behavior).
- **No HTML entity decoding**: Attribute values are returned as-is from the source.
- **Validation paths**: ASCII alphanumeric tag/attribute names use a fast path; non-ASCII falls back to `XmlConvert.VerifyName()` / `XmlConvert.IsNCNameChar()`.

### Navigation helper conventions

- `Count*` methods — read-only, return a length/offset without moving `_currPos`.
- `Skip*` methods — advance `_currPos` as a side effect.

### Test structure

- `Tests/GeneralTests.cs` — standard scenarios from the README.
- `Tests/TechnicalTests.cs` — edge cases and technical validation.

Tests use xUnit. Helper methods `RunIt2()` / `RunTest()` centralise assertion logic.

## Dependencies

- **DotNetXtensions.Mini** v1.0.0-alpha-1.0 — provides extension methods (`IsNulle()`, `NullIfEmptyTrimmed()`, etc.).
- `System.Xml` — used for `XmlConvert` name validation only.
