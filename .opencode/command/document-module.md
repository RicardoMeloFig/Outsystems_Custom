---
description: Full documentation pipeline for a module — extract from Service Studio, then synthesize technical documentation and/or the interactive HTML site.
agent: os-docs
---
Produce documentation for: $ARGUMENTS

If extraction outputs for the target module do not exist yet or are stale,
first delegate extraction (get_open_module → parse_oml_header → extraction
tools), then synthesize the requested documents (TECHNICAL_DOCUMENTATION.md
and/or USER_GUIDE.md per the toolkit skills), and, when asked for a site,
follow the html-docs-generation skill and run
toolkits/html-docs/scripts/Render-Site.ps1 -Project <consumer root> -Clean.
Report every output path.
