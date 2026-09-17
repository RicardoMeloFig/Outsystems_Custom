# Contributing to vanguard-xml-to-json

Thank you for your interest in contributing to this project! This document provides guidelines and instructions for developing, building, and contributing to the XmlToJson ODC External Library.

## Important Notice

**THIS CODE IS NOT SUPPORTED BY OUTSYSTEMS.**

This is a community-maintained port of the OutSystems 11 XmlToJson Forge component to OutSystems Developer Cloud (ODC). While contributions are welcome, OutSystems does not provide official support for this library.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Project Structure](#project-structure)
- [Building the Library](#building-the-library)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing](#testing)
- [Packaging and Deployment](#packaging-and-deployment)
- [Pull Request Process](#pull-request-process)

## Prerequisites

To contribute to this project, you'll need:

- **.NET 10.0 SDK** or later - [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Visual Studio 2022** or **Visual Studio Code** (recommended IDEs)
- **PowerShell** (for running the packaging script on Windows)
- **Git** for version control
- Basic understanding of:
  - C# and .NET development
  - XML and JSON data formats
  - OutSystems ODC External Logic concepts

## Getting Started

1. **Clone the repository**
   ```bash
   git clone https://github.com/OutSystems/vanguard-xml-to-json.git
   cd vanguard-xml-to-json
   ```

2. **Open the solution**
   ```bash
   # Using Visual Studio
   start XmlToJson.sln

   # Or using VS Code
   code .
   ```

3. **Restore NuGet packages**
   ```bash
   dotnet restore
   ```

4. **Build the project**
   ```bash
   dotnet build
   ```

## Project Structure

```
vanguard-xml-to-json/
├── XmlToJson/
│   ├── IXmlToJson.cs              # Public interface exposed to ODC
│   ├── XmlToJson.cs               # Core implementation
│   ├── Node.cs                    # Data structure for array node specification
│   ├── XmlToJson.csproj           # Project configuration
│   ├── generate_upload_package.ps1 # Packaging script
│   └── resources/
│       └── xmltojson.png          # Library icon for ODC
├── XmlToJson.sln                  # Visual Studio solution
├── README.md                      # Project documentation
├── LICENSE                        # License information
└── .gitignore                     # Git ignore rules
```

## Building the Library

### Development Build

For local development and testing:

```bash
cd XmlToJson
dotnet build
```

### Release Build

For a production-ready build:

```bash
cd XmlToJson
dotnet build -c Release
```

### Platform-Specific Build

ODC requires linux-x64 binaries. To build for the target platform:

```bash
cd XmlToJson
dotnet publish -c Release -r linux-x64 --self-contained false
```

The output will be in `bin/Release/net10.0/linux-x64/publish/`

## Development Workflow

### Making Changes

1. **Create a feature branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes**
   - Edit the relevant source files
   - Follow the coding standards (see below)
   - Add inline comments for complex logic

3. **Test your changes**
   - Build the project to ensure no compilation errors
   - If possible, test in an ODC environment

4. **Commit your changes**
   ```bash
   git add .
   git commit -m "Brief description of your changes"
   ```

### Branch Naming Conventions

- **Feature branches**: `feature/description` (e.g., `feature/add-xml-validation`)
- **Bug fixes**: `fix/description` (e.g., `fix/namespace-handling`)
- **Documentation**: `docs/description` (e.g., `docs/update-readme`)
- **Refactoring**: `refactor/description` (e.g., `refactor/simplify-array-logic`)

## Coding Standards

### C# Style Guidelines

This project follows standard C# coding conventions:

#### Naming Conventions
- **Classes, interfaces, methods**: PascalCase (e.g., `XmlToJson`, `IXmlToJson`, `ConvertXmlToJson`)
- **Local variables, parameters**: camelCase (e.g., `arrayNodes`, `xmlDocument`)
- **Private fields**: camelCase with underscore prefix (e.g., `_fieldName`)
- **Constants**: PascalCase (e.g., `MaxNodeDepth`)

#### Code Organization
- Keep methods focused and single-purpose
- Limit method length to ~50 lines when possible
- Use meaningful variable names that indicate purpose
- Add XML documentation comments for public APIs

#### Example
```csharp
/// <summary>
/// Converts an XML document to its JSON equivalent.
/// </summary>
/// <param name="xml">The XML string to convert.</param>
/// <param name="arrayNodes">Optional nodes to treat as arrays.</param>
/// <returns>The resulting JSON string.</returns>
public string ConvertXmlToJson(string xml, IEnumerable<Node>? arrayNodes = null)
{
    // Implementation
}
```

### OutSystems SDK Attributes

When modifying or adding public APIs:

- Always use `[OSInterface]` on interfaces
- Always use `[OSAction]` on public methods
- Always use `[OSParameter]` on method parameters with:
  - `DataType` for type mapping
  - `Description` for ODC UI tooltips
- Always use `[OSStructure]` and `[OSStructureField]` on data structures

### Code Comments

- Use `//` for inline explanations of complex logic
- Use `///` XML comments for public API documentation
- Explain **why** something is done, not **what** (the code shows what)

Example from the codebase:
```csharp
// It may be possible to be a better select than fetch all,
// but I couldn't get it to work with random namespaces
foreach (XmlNode node in doc.SelectNodes("//*"))
```

## Testing

### Current State
The project currently lacks automated tests. This is a known gap.

### Testing Guidelines (Future)
If you're adding test coverage:

1. **Unit Tests**: Place in `XmlToJson.Tests/` directory
2. **Framework**: Use xUnit or NUnit
3. **Test Naming**: `MethodName_Scenario_ExpectedResult`
4. **Coverage Focus**:
   - Various XML structures (nested, flat, namespaced)
   - Edge cases (empty XML, single elements, multiple elements)
   - ArrayNodes parameter behavior
   - Error handling for malformed XML

### Manual Testing
Until automated tests are in place:

1. Build the library
2. Package it (see below)
3. Upload to an ODC environment
4. Create a test application that calls `ConvertXmlToJson`
5. Verify output JSON matches expectations

## Packaging and Deployment

### Using the PowerShell Script

The repository includes a packaging script for Windows:

```powershell
cd XmlToJson
.\generate_upload_package.ps1
```

This script:
1. Sets execution policy for the current user
2. Publishes for linux-x64 release configuration
3. Creates `ExternalLibrary.zip` in the `XmlToJson/` directory

### Manual Packaging

If you prefer manual control or are on a non-Windows platform:

```bash
cd XmlToJson
dotnet publish -c Release -r linux-x64 --self-contained false

# On Linux/macOS
zip -r ExternalLibrary.zip bin/Release/net10.0/linux-x64/publish/*

# On Windows (PowerShell)
Compress-Archive -Path .\bin\Release\net10.0\linux-x64\publish\* -DestinationPath ExternalLibrary.zip
```

### Uploading to ODC

1. Log in to your ODC Portal
2. Navigate to **External Logic** section
3. Upload `ExternalLibrary.zip`
4. The library will be available as "XmlToJson" in your ODC environment

## Pull Request Process

### Before Submitting

1. **Ensure your code builds**
   ```bash
   dotnet build -c Release
   ```

2. **Update documentation** if you've:
   - Changed public APIs
   - Added new features
   - Modified behavior

3. **Test locally** with the ODC runtime if possible

### Submitting a Pull Request

1. **Push your branch**
   ```bash
   git push origin feature/your-feature-name
   ```

2. **Open a Pull Request** on GitHub with:
   - Clear title describing the change
   - Description explaining:
     - What changed and why
     - How to test the changes
     - Any breaking changes or migration notes

3. **Link related issues** if applicable

### PR Review Process

- At least one maintainer review is required
- Address review feedback by pushing new commits
- Once approved, a maintainer will merge your PR
- The main branch should always be in a releasable state

### Commit Message Guidelines

- Use imperative mood ("Add feature" not "Added feature")
- Keep first line under 72 characters
- Provide details in the body if needed

Example:
```
Add XML validation before conversion

- Check for well-formed XML structure
- Return meaningful error messages for malformed input
- Update documentation with validation details
```

## Dependency Updates

### NuGet Package Updates

When updating dependencies:

1. **Check compatibility** with OutSystems.ExternalLibraries.SDK requirements
2. **Test thoroughly** in an ODC environment
3. **Update version** in `XmlToJson.csproj`
4. **Document changes** in the PR description

### Current Dependencies
- `Newtonsoft.Json` 13.0.3
- `OutSystems.ExternalLibraries.SDK` 1.5.0

To update a dependency:
```bash
cd XmlToJson
dotnet add package Newtonsoft.Json --version <new-version>
```

## .NET Version Upgrades

The project currently targets .NET 10.0. When upgrading to a new .NET version:

1. **Verify ODC runtime support** - Check OutSystems documentation for supported .NET versions
2. **Update TargetFramework** in `XmlToJson.csproj`
3. **Test compilation** and packaging
4. **Test in ODC** environment before merging

## Getting Help

- **Issues**: Open a GitHub issue for bugs or feature requests
- **Discussions**: Use GitHub Discussions for questions and ideas
- **Documentation**: Refer to the [README.md](./README.md) for usage information

## Acknowledgments

This project builds upon the work of the OutSystems Community:
- Tiago Bojikian Costa Vital
- Borislav Shumarov
- Kilian Hekhuis

We appreciate all contributors who help maintain and improve this library!

## License

This project is licensed under the terms specified in the [LICENSE](./LICENSE) file.
