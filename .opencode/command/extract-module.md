---
description: Extract the content of an OutSystems 11 module open in Service Studio (logic, data, UI) into the project docs folder.
agent: os-extract
---
Extract the module content for: $ARGUMENTS

Steps: confirm the module is fully loaded in Service Studio (call
get_open_module, note PID) → parse_oml_header for metadata → run the
extraction tools matching what was requested (logic/data and/or UI) with the
explicit pid → persist outputs under the consumer project's docs/<ModuleName>/
→ summarize what was extracted and where it landed.
