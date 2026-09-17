---
name: expression-reference
description: OutSystems expression bible - consult BEFORE writing any expression the model sends to the module (Assign values, CP values, filters, sorts, widget expressions). Iron rules for quoting/operators/null, fixture-verified function tables (Text, conversions, Date/Time, Math, Format, Validate family), scope grammar for aggregates/lists, and the official built-in catalog doc pass. Triggers on "expression", "function name", "filter expression", "assign value", "red expression error".
---

# Skill: expression-reference

# OutSystems expression bible — consult BEFORE writing any expression

Every `value`/`SetValue`/filter/sort/CP expression the model writes must come from this
file. Expressions are validated by Service Studio at verify/publish — a guessed function
name or wrong quoting = red error. When in doubt, pick the dumber, proven construct.

**Authoritative precedence:** live SS verify (`get_verify_errors`, P2+) > this file >
model memory. If SS flags an entry here, fix the entry, not the expression.

**Fixture:** screen `ScratchHome` (flow `ScratchApp`, module `FitnessManager`), container
`ExprBible`, widgets `Expr*` (39 and counting) — one live widget per function below.
After ANY change here, re-check the fixture (bridge accepts any string at set-time;
red only shows at verify). Per-widget check: `get_verify_errors(kind=widget,
screen, name)` — 0 errors = green. Container-level check aggregates children.

## 0. Iron rules (quoting, operators, null)

1. **Text literals are double-quoted, always:** `"Success"`, `""`, `"a" + "b"`.
   NEVER single quotes (`'x'` = syntax error). This applies to Assign values, CP values,
   filters, sorts, JS-adjacent strings alike.
2. **Booleans/numbers/identifiers are bare:** `True`, `False`, `1`, `3.14`,
   `AllExceptions.ExceptionMessage`, `UserId`.
3. **Concatenation is `+`:** `"Logins: " + IntegerToText(Total)`. There is no `&` operator.
4. **Comparison is `=`** (not `==`): `If(Status = "Admin", ...)`. `<>` means not-equal.
5. **Date/time literals are `#...#`:** `#2026-01-15#`, `#10:30:00#`, `#2026-01-15 10:30:00#`.
6. **Null discipline (corrected P1.4):** there is **no `IsNull()` function** and
   **no `NullText()`** — both are red. Test nulls by direct comparison against
   the type default: Text → `x = ""`; Integer/Identifier → `x = NullIdentifier()`;
   Date → `x = NullDate()`; Object → `x = NullObject()`; Binary →
   `x = NullBinary()`; Text Identifier → `x = NullTextIdentifier()` (or `= ""`).
   The only Null* constructors that exist: `NullDate`, `NullIdentifier`,
   `NullObject`, `NullBinary`, `NullTextIdentifier`.
7. **Case:** function names are case-insensitive in SS, but write them exactly as below.

## 1. Text (all ✅ fixture-verified shape)

| Function | Example (good) | Bad (red) |
|---|---|---|
| `Length(t)` | `Length("hello")` → 5 ✅ | `Len("hello")` — no such function |
| `Index(t, sub)` | `Index("hello world", "world")` → 6 ✅ — **zero-based** (official docs), returns **-1** when not found OR when search is empty | `InStr(...)` |
| `Index` optional args | `Index(t, sub, startIndex: 5)`, `searchFromEnd: True`, `ignoreCase: True` 📄 (named optional args; NOT available in Aggregate filters) | positional beyond 2 args |
| `Substr(t, start, len)` | `Substr("hello world", 0, 5)` → `"hello"` ✅ — start is **zero-based** (docs) | `Substring(...)`, `Mid(...)` |
| `Trim(t)` / `TrimStart(t)` / `TrimEnd(t)` | `Trim("  x  ")` ✅ | `Strip(...)` |
| `Replace(t, old, new)` | `Replace("aaa", "a", "b")` ✅ | `StrReplace(...)` |
| `Concat(a, b)` | `Concat("a", "b")` ✅ (fixture green; official name is `Concat`, exactly 2 args) | `Concatenate(...)` 📄 suspect — not in the official Text list; prefer `Concat` or `+` |
| `ToUpper(t)` / `ToLower(t)` | `ToUpper("abc")` / `ToLower("ABC")` ✅ (fixture green, P1.4) | `Upper(...)`, `Lower(...)` |
| `NewLine()` | `"a" + NewLine() + "b"` ✅ | `"\n"`, `Chr(13)+Chr(10)` (works but prefer NewLine) |
| `Chr(n)` | `Chr(65)` → `"A"` ✅ | — |
| `EncodeHtml(t)` / `EncodeUrl(t)` / `EncodeJavaScript(t)` / `EncodeSql(t)` | `EncodeUrl("a b")` ✅ | manual `%20` hacks |

Doc-sourced gotchas (official Text reference):
- **`Replace` with empty `search`** inserts `replace` at every character position
  (including start/end): `Replace("01245", "", "abc")` → `"abc0abc1abc2abc4abc5abc"`.
  Validate user-provided `search` is non-empty before calling.
- **`Trim`/`TrimStart`/`TrimEnd` server-side only remove ASCII space** (`U+0020`)
  — not tabs/non-breaking/ideographic spaces (client-side JS impl removes all
  Unicode whitespace). Server-side non-ASCII whitespace needs the Text
  extension's `Regex_Replace`.
- **`EncodeSql` is server-side ONLY** (client logic: No). The other `Encode*`
  are available both sides.
- `Encode*` functions are literal-encoders, **not** XSS/SQLi protection on
  their own.

## 2. Conversions (all ✅ fixture shape)

| Function | Example |
|---|---|
| `TextToInteger(t)` / `IntegerToText(i)` | `TextToInteger("42")`, `IntegerToText(42)` |
| `TextToLongInteger` / `LongIntegerToText` | same pattern, 64-bit |
| `TextToDecimal` / `DecimalToText` | `DecimalToText(2.5)` ✅ green |
| `TextToDate(t)` / `DateToText(d)` | `DateToText(CurrDate())` ✅ — **1 arg ONLY** |
| `TextToTime` / `TimeToText`, `TextToDateTime` / `DateTimeToText` | `TextToDateTime("2026-01-15 10:30:00")` ✅, `DateTimeToText(CurrDateTime())` ✅ — **1 arg ONLY** |
| `TextToIdentifier(t)` / `IdentifierToText(id)` / `IdentifierToInteger` | `TextToIdentifier("7")` ✅ green (proves the shape; real use: `TextToIdentifier(UserId)`) |
| `IntegerToIdentifier` / `LongIntegerToIdentifier` | entity-Id params (Delete pattern) |

> ❌ RED (verified 2026-09-16, SS 11.55.83): `DateToText(d, fmt)` and
> `DateTimeToText(dt, fmt)` — **WrongNumberArguments**. The `*ToText` family takes
> exactly 1 arg. For formatted output use **`FormatDateTime(dt, fmt)`** ✅
> (`FormatDateTime(CurrDateTime(), "yyyy-MM-dd")` green).
> ❌ RED: `NullText()`, `NullInteger()` — **UnknownFunction**, they do not exist.

## 3. Date/Time (all ✅ fixture shape)

`CurrDate()`, `CurrTime()`, `CurrDateTime()`, `NewDate(y,m,d)`, `NewTime(h,mi,s)`,
`NewDateTime(y,mo,d,h,mi,s)`, `BuildDateTime(date, time)` ✅,
`AddDays/AddHours/AddMinutes/AddMonths/AddSeconds/AddYears(dt, n)` ✅ (AddDays, AddMonths green),
`DiffDays` ✅ / `DiffHours` ✅ / `DiffMinutes` ✅ / `DiffSeconds` ✅ (P1.4 sweep;
`DiffYears` does NOT exist in the official list),
`Year/Month/Day/Hour/Minute/Second/DayOfWeek(dt)` ✅ (all green).

```ebnf
AddDays(CurrDate(), 7) | DiffDays(CurrDate(), AddDays(CurrDate(), 7)) | BuildDateTime(#2026-01-01#, #10:00:00#)
FormatDateTime(CurrDateTime(), "yyyy-MM-dd")   -- the ONLY formatted date output
```

Doc-sourced semantics (official Date&Time reference):
- `Diff*` sign convention: **positive when dt1 < dt2**, negative when dt1 > dt2,
  0 when equal. DST ignored; Platform Server time zone.
- `DiffDays` ignores the Time component (normalizes both to 00:00:00).
- There is **no `DiffMonths`** in the official list (matches fixture RED).

> ❌ RED: `DiffMonths(a, b)` — **UnknownFunction**. Month arithmetic = `AddMonths`
> (green). There is also no `DiffYears` (use `Year(a) - Year(b)` or `DiffDays/365`).

## 4. Math / Numeric

`Abs(n)`, `Round(n, decimals)`, `Mod(a, b)`, `Power(base, exp)`, `Sqrt(n)`,
`Trunc(n)` ✅, `Sign(n)` ✅, `Max(a,b)`/`Min(a,b)` ✅ (P1.4 for Trunc/Sign; args
are nullable-Decimal in the docs). Plain `+ - * /` with parentheses. Integer
division truncates — cast via `TextToDecimal` when needed.

📄 `Round` method depends on context: **round half to even** in client/server
logic, **round half away from 0** in aggregates querying SQL Server/Oracle,
**round half up** on iDB2. Same split for `FormatDecimal`/`FormatCurrency`
(server: half up; client: half to even).

## 5. Logic / Null

`If(cond, trueVal, falseVal)` — both branches SAME type (else red).
`Not`, `And`, `Or` operators (words, not symbols).

Null discipline (verified 2026-09-16/17 — the family is ASYMMETRIC, do not generalize):

| Expression | Verdict |
|---|---|
| `NullDate()` | ✅ EXISTS |
| `NullIdentifier()` | ✅ EXISTS (the id/integer-null story) |
| `NullTextIdentifier()` | ✅ EXISTS — test with `NullTextIdentifier() = ""` |
| `NullObject()` | ✅ EXISTS — comparable: `NullObject() = NullObject()` |
| `NullBinary()` | ✅ EXISTS — comparable: `NullBinary() = NullBinary()` |
| `""` | ✅ Text null IS the empty literal |
| `NullText()` | ❌ UnknownFunction — does not exist |
| `NullInteger()` | ❌ UnknownFunction — does not exist |
| `NullTime()`, `NullDateTime()`, `NullBoolean()`, `NullDecimal()` | ❌ NOT in the official list — treat as absent |

> The official Data Conversion reference lists exactly: `NullDate`,
> `NullIdentifier`, `NullObject`, `NullBinary`, `NullTextIdentifier`. That is
> the full Null* family — anything else (`NullText`, `NullInteger`, ...) is a
> hallucination. Text null = `""`; Integer/LongInteger null = `NullIdentifier()`.

> ⚠ **`IsNull()` DOES NOT EXIST in expressions** (❌ P1.4: `IsNull("")` →
> Invalid Expression; `IsNull(NullTextIdentifier())` → Invalid Expression).
> Null tests are direct comparisons against the type default:
> `x = ""`, `x = NullIdentifier()`, `x = NullDate()`, `x = NullObject()`.
> (A green fixture widget named `ExprIsNull` predates this discovery — its
> expression does not call IsNull; do not trust the name.)

## 5.1 Conversion *Validate family (✅ P1.4 + 📄 signatures)

`TextToIntegerValidate` ✅, `TextToDateValidate` ✅, and per the official list:
`TextToLongIntegerValidate`, `TextToDecimalValidate`, `TextToDateTimeValidate`,
`TextToTimeValidate`, `DecimalToIntegerValidate`, `DecimalToLongIntegerValidate`,
`LongIntegerToIntegerValidate` → Boolean. Pattern:
`If(TextToIntegerValidate(t), TextToInteger(t), 0)`.

📄 Conversion-behavior gotchas:
- Out-of-range `TextToInteger`/`TextToLongInteger`/`TextToDecimal` in **logic**
  return the type default; in an **Aggregate** they **throw**.
- `DecimalToInteger`/`DecimalToLongInteger`: round half to even in logic,
  **truncate** in aggregate expressions.
- `TextToDate`/`TextToDateTime` accept `yyyy-MM-dd`, `/` or `.` separators by
  default; when an environment date format is configured, only THAT separator
  is accepted.
- `BooleanToText` ✅ → `"True"`/`"False"`; `IntegerToBoolean` ✅ (0=False),
  `BooleanToInteger` (True=1); `DateTimeToDate` ✅ / `DateTimeToTime`/
  `DateToDateTime` component droppers; `ToObject(exp)`.

## 6. Scope grammar (aggregates, lists, structures — the `#1 red source`)

```
<Agg>.List.Current.<Entity>.<Attr>        e.g. GetUserById.List.Current.User.Name
<DataAction>.List.Current.<Field>         e.g. FetchUserStats.List.Current.TotalLogins
<DataAction>.List.Current.<ListField>.List.Current.<Attr>   nested lists
<Form>.Valid                              form validity flag
<FormField>.Valid                         per-field flag (verify in P3)
```

Rules: `List` before EVERY `Current`; entity name between `Current.` and `.<Attr>`
for aggregates (`...Current.User.Name`, NOT `...Current.Name` when the source is the
User entity); structures from data actions skip the entity segment. Filters/sorts use
bare entity-qualified paths: `User.Id = TextToIdentifier(UserId)`,
`AddOrderBy("User.Name", True)`.

## 7. Lists — functions vs System ACTIONS (do not confuse)

There are **no inline list-transform functions** in expressions. List work = **System
actions in flows** (consume `(System)` or platform module first): `ListAppend`,
`ListInsert`, `ListRemove`, `ListRemoveAll`, `ListClear`, `ListIndexOf`, `ListSort`,
`ListFilter`, `ListDistinct`, `ListCount`? (`ListCount` verify — may be action-only).
In expressions you may only navigate (`...List.Current...`, `.List.Length`? — VERIFY:
list `.Length` vs `ListCount`; fixture P3). Until verified, count via a `Count`
aggregate (G3 pattern).

## 8. What NOT to invent

No `==`, no `&&`/`||`, no ternaries, no lambdas, no `?.`, no string interpolation,
no `Now()` (use `CurrDateTime()`), no `Today()` (use `CurrDate()`), no
`Guid.NewGuid()` (use platform `GenerateGUID` — verify), no regex (SQL/extension
only). `Format()` generic does not exist — the Format family is
`FormatDateTime`/`FormatDecimal`/`FormatCurrency`/`FormatPercent`/`FormatText`/
`FormatPhoneNumber`.

## 8.1 Format family (📄 official signatures; ✅ where swept)

| Function | Signature | Notes |
|---|---|---|
| `FormatDateTime` | `(dt, format)` → Text | ✅ fixture green. Patterns: `d,dd,ddd,dddd`, `M,MM,MMM,MMMM`, `y,yy,yyyy`, `h,hh,H,HH`, `m,mm`, `s,ss`, `t,tt` (AM/PM) |
| `FormatDecimal` | `(value, decimal_digits, decimal_separator, group_separator)` → Text | ✅ P1.4 (4-arg form green) |
| `FormatCurrency` | `(value, symbol, decimal_digits, decimal_separator, group_separator)` → Text | value type is Currency |
| `FormatPercent` | `(value, decimal_digits, decimal_separator)` → Text | ✅ P1.4 (3-arg form green); appends `%` |
| `FormatText` | `(value, min_chars, max_chars, left_padding, padding_char)` → Text | ✅ P1.4 (5-arg form green); pad/truncate to a width |
| `FormatPhoneNumber` | `(number, country_code)` → Text | — |

## 8.2 Environment / misc (📄 official — server-side)

`GetUserAgent()`, `GetCurrentLocale()` (RFC 1766 locale of the session),
`GetEntryEspaceName()`, `GetEntryEspaceId()`, `GetOwnerEspaceIdentifier()`,
`GetDatabaseProvider()` (SqlServer/Oracle), `GetApplicationServerType()`;
misc: `GeneratePassword(length, include...)`, `IsLoadingScreen()`.
URL family: `GetBookmarkableURL()`, `GetOwnerURLPath()`,
`GetPersonalAreaName()`, `GetExceptionURL()` — page/URL builders, server-side.

## 9. Verification log

- P1 (2026-09-16): fixture `ExprBible` created on `ScratchHome`; bridge-accept ✅.
- P1.2 (2026-09-16, SS 11.55.83, per-widget `get_verify_errors`): 39 widgets swept.
  GREEN (31+): Index Substr Length Trim Replace Concat NewLine Chr EncodeUrl EncodeHtml
  TextToInteger IntegerToText TextToDateTime TextToIdentifier CurrDateTime AddDays
  DiffDays BuildDateTime Year DayOfWeek NewDate Month Hour Max Min Abs Round Mod Power
  Sqrt If DateToText(1-arg) DateTimeToText(1-arg) FormatDateTime NullDate NullIdentifier
  AddMonths `""`-literal. RED (documented above, fixture holds the GREEN form):
  `DateToText(d,fmt)`, `DateTimeToText(dt,fmt)`, `NullText()`, `NullInteger()`,
  `DiffMonths()`. Rules 1–7 proven via UserProfileCard/GetUserById + live sweep.
- P1.3 (2026-09-17): doc pass against the official O11 built-in-function reference
  (`docs-product\src\ref\lang\auto\builtinfunction-*.md`, CC BY-NC-ND 4.0;
  summarized here). Fixed `Concatenate`→`Concat`, confirmed `Index` zero-based
  with -1 semantics, added `ToUpper`/`ToLower` (EXISTS — corrected an earlier
  wrong "no Upper/Lower" claim), confirmed `DiffHours/Minutes/Seconds` exist and
  `DiffYears` does not, pinned the Null* family to the official five, added the
  `*Validate` family, `Trunc`/`Sign`, the Format/Environment catalogs, and the
  Replace-empty-search + Trim-ASCII gotchas. All 📄 entries above came from this
  pass and are NOT fixture-verified yet.
- P1.4 (2026-09-17, SS 11.55.83, live sweep — 18 new widgets in `ExprBible`):
  GREEN: `ToUpper` `ToLower` `Trunc` `Sign` `DiffHours` `DiffMinutes`
  `DiffSeconds` `TextToIntegerValidate` `TextToDateValidate` `FormatDecimal(4)`
  `FormatPercent(3)` `FormatText(5)` `BooleanToText` `IntegerToBoolean`
  `DateTimeToDate` `NullTextIdentifier() = ""` `NullObject() = NullObject()`
  `NullBinary() = NullBinary()`.
  NEW RED: **`IsNull(...)` — Invalid Expression even with a Text argument**
  (`IsNull("")` red, `IsNull(NullTextIdentifier())` red). Null tests must be
  direct `=` comparisons against type defaults. The three Null* widgets above
  were red while wrapped in `IsNull(...)` and green as bare comparisons —
  the wrapper was the error, not the functions.
  Setting an existing expression's value: `set_widget_property` Value is NOT
  settable on `NRWidgets.Expression`; use bridge `set_screen_cp_parsed`
  (propName `Value`).

---

**Attribution.** Sections marked 📄 are condensed summaries of the official
OutSystems 11 built-in function reference
(`docs-product\src\ref\lang\auto\builtinfunction-text.md`, `-math.md`,
`-numeric.md`, `-date-and-time.md`, `-data-conversion.md`, `-format.md`,
`-environment.md`, `-url.md`, `-miscellaneous.md`; OutSystems docs,
CC BY-NC-ND 4.0). ✅/❌ verdicts remain fixture-verified against live SS only.
