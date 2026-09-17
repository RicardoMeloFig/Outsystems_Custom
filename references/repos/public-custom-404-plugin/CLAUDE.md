# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This repository contains XSLT configuration templates for customizing HTTP 404 error handling in OutSystems applications running on IIS. These are not executable code files - they are transformation templates that administrators import into OutSystems Factory Configuration to modify IIS web.config files.

See [ARCHITECTURE.md](./ARCHITECTURE.md) for the identity transformation pattern, architectural tenets, and external system integration details.

See [CONTRIBUTING.md](./CONTRIBUTING.md) for the contribution workflow, file organization conventions, and XSL editing guidelines.

## Repository Structure

See [CONTRIBUTING.md - File Organization](./CONTRIBUTING.md#file-organization) for the complete repository structure.

Key files:
- **CustomErrorHandler.xsl**: Complete working XSLT template
- **\*_snippet.xsl**: Documentation fragments referenced in README

## Validation Commands

### Validate XSL syntax

```bash
xmllint --noout CustomErrorHandler.xsl
```

This checks for well-formed XML and valid XSL syntax. Run this before committing XSL changes.

### Check all XSL files

```bash
xmllint --noout *.xsl
```

### Installing xmllint

If `xmllint` is not available:
- **Windows**: Install via [Chocolatey](https://chocolatey.org/) (`choco install xsltproc`) or download from [xmlsoft.org](http://xmlsoft.org/downloads.html)
- **Mac**: `brew install libxml2`
- **Linux**: `apt-get install libxml2-utils` (Ubuntu/Debian) or equivalent for your distribution

## Common Editing Tasks

### Modifying the example application or page name

The example uses `MyStore` as the application name and `NotFound` as the page name. To change these:

1. Update the `$new404` parameter in `CustomErrorHandler.xsl` (line 7)
2. Update the `path` attribute in the error element (line 20)
3. Ensure both URLs match and use `&amp;` (not `&`) for the ampersand

### XSL syntax requirements

From [ARCHITECTURE.md T4: URL Encoding Must Follow XSLT Rules](./ARCHITECTURE.md):
- Use `&amp;` entity encoding for URL query parameter separators
- Wrap URL strings in single quotes inside the select attribute
- Example: `select="'/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound'"`

### Understanding the transformation pattern

From [ARCHITECTURE.md T1: Identity Transformation with Selective Override](./ARCHITECTURE.md):
- The template at lines 9-13 in `CustomErrorHandler.xsl` copies all nodes unchanged (identity transformation)
- The template at lines 15-23 selectively overrides `/configuration/system.webServer` to inject the httpErrors element
- This preserves existing IIS configuration while only modifying 404 error handling

## Important Context

### Not a plugin repository

This repository does NOT contain the OutSystems Forge plugin code. It only contains:
- Public documentation for configuring the plugin
- XSLT templates that administrators use with OutSystems Factory Configuration
- Example snippets for the README

The actual Custom 404 Plugin must be installed separately from OutSystems Forge.

### Dual configuration requirement

From [ARCHITECTURE.md T3: Dual Error Handler Registration Required](./ARCHITECTURE.md):
Custom 404 pages must be registered in BOTH:
- `system.webServer/httpErrors` (IIS 7+ integrated pipeline) - see `CustomErrorHandler.xsl` lines 18-22
- `system.web/customErrors` (IIS 6 compatibility) - see `CustomErrorHandler.xsl` lines 26-30

### How these files are used

1. Administrator creates a custom 404 error page in OutSystems Service Studio
2. Administrator installs "Factory Configuration" from OutSystems Forge
3. Administrator creates a Shared Configuration in Factory Configuration
4. Administrator copies the XSLT content from `CustomErrorHandler.xsl`, modifying the application and page names
5. OutSystems applies the XSLT transformation to the application's web.config file when the application is published
6. IIS reads the transformed web.config and redirects 404 errors to the custom page

## Git Workflow

This repository uses simple, imperative commit messages focusing on clarity:
- "Update README.md"
- "Fix XSL syntax in CustomErrorHandler"
- "Add troubleshooting section for authentication errors"

From [CONTRIBUTING.md](./CONTRIBUTING.md), the workflow is:
1. Fork and clone
2. Create a branch from `main`
3. Make changes
4. Commit with clear message
5. Push to fork and open PR against `main`

## Common Issues

### Invalid XML warnings in Service Studio

If OutSystems Service Studio shows "config was invalid and will be disregarded" when republishing:
- Check for missing single quotes around URLs in select attributes
- Verify `&amp;` is used instead of `&` in URLs
- Validate syntax with `xmllint --noout CustomErrorHandler.xsl`
- See README.md "Troubleshooting common errors" section for examples

### Snippet files won't transform web.config

The snippet files (`first_snippet.xsl`, `second_snippet.xsl`, `third_snippet.xsl`) are incomplete XSLT documents used for documentation. They are missing:
- XML declaration
- Root stylesheet element
- Namespace declarations
- Identity transformation template

Use `CustomErrorHandler.xsl` as the starting point for actual configurations.
