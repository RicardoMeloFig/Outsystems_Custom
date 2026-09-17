# Contributing to Custom 404 Plugin

This repository contains public documentation and XSL configuration templates for the OutSystems Custom 404 Plugin. Contributions help improve the documentation and example configurations for the community.

## Repository Overview

This is a documentation repository containing:
- **README.md**: Complete setup and configuration guide
- **CustomErrorHandler.xsl**: Full XSL transformation example
- **\*_snippet.xsl**: Individual configuration snippets referenced in the README

## Development Setup

No special development environment is required. You only need:
- A text editor for editing XSL and Markdown files
- Git for version control

## Making Contributions

### Types of Contributions

Welcome contributions include:
- Documentation improvements and clarifications
- Corrections to XSL syntax or configuration examples
- Additional troubleshooting guidance
- Typo and formatting fixes

### Editing XSL Files

When modifying XSL files, ensure:
- Valid XML syntax (check opening/closing tags)
- Proper escaping of special characters (use `&amp;` for `&`)
- URLs in `select` attributes are enclosed in both single and double quotes
- Consistent indentation (4 spaces)

To validate XSL syntax:
```bash
xmllint --noout CustomErrorHandler.xsl
```

If `xmllint` is not installed:
- **Windows**: Install via [Chocolatey](https://chocolatey.org/) (`choco install xsltproc`) or download from [xmlsoft.org](http://xmlsoft.org/downloads.html)
- **Mac**: `brew install libxml2`
- **Linux**: `apt-get install libxml2-utils` (Ubuntu/Debian) or equivalent for your distribution

### Editing Documentation

The README.md follows the actual configuration workflow. When updating:
- Keep step numbers sequential
- Include code examples for configuration changes
- Reference the example files in this repository
- Add troubleshooting entries for common issues you've encountered

## Pull Request Process

1. **Fork and clone** the repository
2. **Create a branch** from `main`:
   ```bash
   git checkout -b your-branch-name
   ```
3. **Make your changes** and test them locally
4. **Commit your changes** with a clear message:
   ```bash
   git commit -m "Fix XSL syntax in second snippet"
   ```
5. **Push to your fork** and open a pull request against `main`

### Commit Message Style

Based on the git history, this repository uses simple, imperative commit messages:
- "Update README.md"
- "Fix XSL syntax in CustomErrorHandler"
- "Add troubleshooting section for authentication errors"

Focus on clarity rather than following a strict convention.

## Content Guidelines

### Documentation
- Write for OutSystems developers who may be unfamiliar with XSL
- Include complete examples, not just fragments
- Explain the "why" for non-obvious configurations
- Test instructions on a fresh OutSystems environment when possible

### XSL Templates
- Templates should be ready to use with minimal modification
- Include XML declaration and proper namespace declarations
- Add comments explaining non-obvious transformations
- Use placeholder values like "MyStore" and "NotFound" that are clearly meant to be replaced

## File Organization

```
/
├── README.md                    # Main documentation
├── CustomErrorHandler.xsl       # Complete working example
├── first_snippet.xsl            # Parameter definition snippet
├── second_snippet.xsl           # Error configuration snippet
└── third_snippet.xsl            # Template transformation snippet
```

Keep this structure. If adding new examples, follow the naming pattern.

## Getting Help

For questions about:
- **This repository**: Open an issue
- **The OutSystems plugin**: Check the Forge plugin page or OutSystems Community
- **OutSystems Platform**: Refer to official OutSystems documentation

## Code of Conduct

Be respectful and constructive. This is a community resource for OutSystems developers.
