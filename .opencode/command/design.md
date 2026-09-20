---
description: Design a screen, flow, or cross-module change through the architect before implementation (produces a design contract, changes nothing).
agent: os-architect
---
Produce a design contract for: $ARGUMENTS

Follow the output contract: goal/target, evidence inspected, reuse,
sequencing (producers before consumers, bottom-up by module dependency),
changes, data + dependencies, security, UI states, acceptance checks,
assumptions, unknowns. Decide what needs doing
first and where (which module, which layer) before designing details. If the
evidence you have (project notes, extraction outputs) is insufficient, say
exactly what must be extracted or researched first — do not invent element
names.
