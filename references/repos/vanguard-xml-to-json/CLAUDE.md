# Claude Code Instructions

This document provides guidance for Claude Code when working with the `vanguard-xml-to-json` repository.

## Project Overview

This is an **OutSystems Developer Cloud (ODC) External Logic Library** that converts XML documents to JSON. It's a .NET 10.0 library that wraps Newtonsoft.Json's XML-to-JSON conversion functionality for use in OutSystems ODC applications.

**Key Context:**
- **NOT officially supported by OutSystems** - this is a community-maintained port
- Port from OutSystems 11 to ODC platform
- Simple, focused codebase with ~50 lines of core logic
- No tests currently (this is a known gap)

## Quick Reference

### Project Files
- `XmlToJson/IXmlToJson.cs` - Public interface with OutSystems SDK attributes
- `XmlToJson/XmlToJson.cs` - Core implementation (~50 lines)
- `XmlToJson/Node.cs` - Data structure for ArrayNodes parameter
- `XmlToJson/XmlToJson.csproj` - .NET 10.0 project file
- `XmlToJson/generate_upload_package.ps1` - Packaging script for ODC deployment

### Build Commands
```bash
# Standard build
dotnet build

# Release build for ODC
cd XmlToJson
dotnet publish -c Release -r linux-x64 --self-contained false

# Package for upload
.\generate_upload_package.ps1  # Windows
# Creates ExternalLibrary.zip
```

### Git Workflow
- Main branch: `main`
- Clean working tree (no uncommitted changes at session start)
- Branch naming: `feature/`, `fix/`, `docs/`, `refactor/`

## Architecture

For detailed architecture information, see [ARCHITECTURE.md](./ARCHITECTURE.md).

**Key architectural points:**
- **Single responsibility**: Convert XML to JSON, nothing more
- **Delegates to Newtonsoft.Json**: Uses `JsonConvert.SerializeXmlNode()`
- **OutSystems integration**: Decorated with `[OSInterface]`, `[OSAction]`, `[OSParameter]` attributes
- **ArrayNodes handling**: Special logic to force single-element nodes to render as JSON arrays

**Important implementation detail:** The code uses `SelectNodes("//*")` and filters by `LocalName` to handle arbitrary XML namespaces. This is by design (see comment in `XmlToJson.cs:27-28`).

## Development Guidelines

For complete development guidelines, see [CONTRIBUTING.md](./CONTRIBUTING.md).

### When Making Code Changes

**Always:**
1. Follow C# naming conventions (PascalCase for public APIs, camelCase for locals)
2. Maintain OutSystems SDK attributes on public APIs
3. Add explanatory comments for non-obvious logic
4. Test the packaged library in an ODC environment if possible

**Never:**
- Remove or modify SDK attributes without understanding impact on ODC integration
- Change the namespace `OutSystems.XmlToJson` (breaks ODC references)
- Add breaking changes to the public API without discussion

### Code Style
- PascalCase: `IXmlToJson`, `ConvertXmlToJson`, `ArrayNodes`
- camelCase: local variables and parameters
- XML doc comments (`///`) on public APIs
- Inline comments (`//`) to explain **why**, not **what**

### Common Tasks

#### Adding a New Method to the Interface

1. Update `IXmlToJson.cs` with `[OSAction]` attribute
2. Implement in `XmlToJson.cs`
3. Add `[OSParameter]` attributes with descriptions
4. Update README.md with usage documentation
5. Test in ODC environment

#### Updating Dependencies

```bash
cd XmlToJson
dotnet add package Newtonsoft.Json --version <version>
# Verify compatibility with ODC External Libraries SDK
dotnet build
```

#### Fixing Bugs

1. Read the relevant code (likely `XmlToJson.cs`)
2. Understand the Newtonsoft.Json behavior (it handles the conversion)
3. Check if the issue is in pre-processing (ArrayNodes) or post-processing
4. Add inline comments explaining the fix
5. Consider adding a note in README.md if it affects users

## Testing Strategy

**Current state:** No automated tests exist.

**When asked to add tests:**
1. Create `XmlToJson.Tests/` directory
2. Use xUnit or NUnit framework
3. Add `XmlToJson.Tests.csproj` referencing main project
4. Test cases to prioritize:
   - XML with namespaces
   - XML with attributes
   - Single vs multiple elements (with and without ArrayNodes)
   - Empty XML
   - Malformed XML (should throw)
   - Special characters and encoding

**Manual testing approach:**
1. Build and package the library
2. Upload to ODC test environment
3. Create ODC app that calls `ConvertXmlToJson`
4. Verify JSON output

## OutSystems-Specific Context

### External Logic Library Concepts

This library integrates with ODC using the **OutSystems External Libraries SDK**:

- `[OSInterface]` - Marks the interface as an External Logic library
  - `Name`: Display name in ODC
  - `Description`: Shown in ODC UI
  - `IconResourceName`: Path to embedded PNG icon

- `[OSAction]` - Exposes a method as an ODC action
  - `Description`: Tooltip in ODC
  - `ReturnName`: Name of output parameter
  - `OriginalName`: Legacy name for compatibility

- `[OSParameter]` - Describes method parameters
  - `DataType`: Maps to ODC data types (`OSDataType.Text`)
  - `Description`: Parameter tooltip in ODC

- `[OSStructure]` / `[OSStructureField]` - Defines data structures
  - `IsMandatory`: Required fields in ODC

### Deployment Model

ODC requires linux-x64 binaries:
- Build with `-r linux-x64 --self-contained false`
- Zip the publish folder as `ExternalLibrary.zip`
- Upload via ODC Portal → External Logic
- Library appears as "XmlToJson" with the icon from `resources/xmltojson.png`

### Limitations Inherited from Newtonsoft.Json

Users may report issues that are actually Newtonsoft.Json limitations:
- Namespace handling quirks
- Attribute naming (always "@" prefix)
- Date format handling
- Special character encoding

**When in doubt**, test the behavior directly with `JsonConvert.SerializeXmlNode()` to determine if it's a Newtonsoft.Json constraint.

## Domain-Specific Knowledge

### XML-to-JSON Conversion Challenges

**The Array Problem:**
XML doesn't distinguish between single elements and collections:
```xml
<items>
  <item>A</item>
</items>
```
Could convert to `{"items": {"item": "A"}}` or `{"items": {"item": ["A"]}}`.

**Solution:** The `ArrayNodes` parameter lets users specify which nodes should always be arrays:
```csharp
ConvertXmlToJson(xml, new[] { new Node { Name = "item" } })
// Ensures "item" is always an array, even with one element
```

**Implementation:** Adds `json:Array="true"` attribute in the `http://james.newtonking.com/projects/json` namespace, which Newtonsoft.Json recognizes.

### XML Namespaces

The code uses `LocalName` (unprefixed node name) rather than full qualified names:
```csharp
if (node.LocalName == item.Name)
```

This means `<ns:item>`, `<abc:item>`, and `<item>` are all matched by `Name = "item"`.

**Why:** The comment in the code explains - XPath queries with namespace predicates were unreliable with arbitrary namespace prefixes.

## Common User Requests

### "Add XML validation"
The library currently calls `XmlDocument.LoadXml()` which throws on malformed XML. Consider:
- Whether to catch and return friendly error messages
- Input size limits (XML bomb protection)
- DTD processing settings (XXE attack mitigation)

See [ARCHITECTURE.md - Security Considerations](./ARCHITECTURE.md#security-considerations) for details.

### "Support for XDocument"
Not feasible - `JsonConvert.SerializeXmlNode()` requires `XmlDocument`, not `XDocument`.

### "Custom attribute prefix"
Would require changes to Newtonsoft.Json's behavior. Not in scope for this wrapper library.

### "Support for JSON-to-XML"
Out of scope. This library is explicitly one-way (XML → JSON). Newtonsoft.Json does support the reverse with `JsonConvert.DeserializeXmlNode()`, but adding it would require careful API design.

### "Add async support"
The XML parsing and JSON serialization are CPU-bound, not I/O-bound. Async would not provide meaningful performance benefits. ODC actions can be called asynchronously from the ODC side if needed.

## Working with Git

### Current State
- **Branch:** `main`
- **Status:** Clean (no uncommitted changes)
- **Recent work:** Upgraded from .NET 6 to .NET 8 (commits `6c251c6` and `36c7c8f`), then from .NET 8 to .NET 10

### When Creating Commits

**Do:**
- Use clear, imperative commit messages
- Reference issue numbers if applicable
- Keep commits focused on a single change

**Don't:**
- Commit build outputs (`bin/`, `obj/`, `ExternalLibrary.zip`)
- Commit IDE files (`.vs/`, `*.cache`)
- Use `--no-verify` unless explicitly requested

These are already in `.gitignore`.

## Clarifying Questions to Ask

When given ambiguous requests, ask:

**For new features:**
- "Should this be exposed as a new ODC action, or modify the existing `ConvertXmlToJson` method?"
- "Have you considered how this will appear in the ODC UI?"
- "Do you need this to be backward compatible with existing ODC apps?"

**For bug fixes:**
- "Can you provide a sample XML input and the unexpected JSON output?"
- "Have you tested this directly with Newtonsoft.Json's `SerializeXmlNode()`?"

**For dependency updates:**
- "Have you verified this version is compatible with OutSystems.ExternalLibraries.SDK?"
- "Do you want me to test the package in an ODC environment first?"

## Helpful Context for Common Scenarios

### Scenario: "The JSON output is wrong"
1. First, test with pure Newtonsoft.Json to isolate whether it's a library issue
2. Check if they're using `ArrayNodes` correctly
3. Look at namespace handling (are prefixes causing confusion?)
4. Check the XML structure (attributes, nesting, special characters)

### Scenario: "Package upload fails in ODC"
1. Verify the build target is `linux-x64`
2. Check that `--self-contained false` was used
3. Ensure all required DLLs are in the publish folder
4. Verify SDK attributes are correct (ODC validates these)

### Scenario: "Upgrade to .NET X"
1. Check OutSystems documentation for supported .NET versions in ODC
2. Update `<TargetFramework>` in `.csproj`
3. Verify Newtonsoft.Json and SDK compatibility
4. Rebuild and retest packaging
5. Update documentation

### Scenario: "Add tests"
1. Create `XmlToJson.Tests` project
2. Reference main project
3. Use xUnit or NUnit
4. Focus on XML variations (namespaces, attributes, arrays)
5. Add GitHub Actions workflow for CI

## Additional Resources

- **OutSystems Documentation**: [ODC External Logic](https://success.outsystems.com/)
- **Newtonsoft.Json**: [Documentation](https://www.newtonsoft.com/json/help/html/M_Newtonsoft_Json_JsonConvert_SerializeXmlNode_1.htm)
- **Original O11 Component**: [XmlToJson on Forge](https://www.outsystems.com/forge/component-overview/3709/xmltojson-o11)

## Remember

- This is a **simple, focused library** - resist scope creep
- **Newtonsoft.Json does the heavy lifting** - most behavior comes from there
- **OutSystems SDK attributes are critical** - changes affect ODC integration
- **No official support** - set expectations accordingly when discussing limitations
- **Linux-x64 target** - ODC runtime requirement, don't change this

When in doubt, refer to [ARCHITECTURE.md](./ARCHITECTURE.md) for design decisions and [CONTRIBUTING.md](./CONTRIBUTING.md) for development workflow.
