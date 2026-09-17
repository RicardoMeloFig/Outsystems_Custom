# ODCTenantPoolSize

OutSystems Integration Studio extension that exposes two tenant‑pool forecasting
actions for ODC Personal Editions:

- `MssPersonalEditionPoolSizePredictionSSA_Get` — per‑weekday Singular Spectrum
  Analysis (ML.NET).
- `MssPersonalEditionPoolSizePredictionSARIMA_Get` — Box‑Jenkins
  SARIMA(p, d, q) × (P, D, Q, s), with an optional SARIMAX exogenous regressor
  derived from a z‑score test, built on top of `MathNet.Numerics`.

Both actions consume an `RLPersonalEditionRequestRecordList` of historical daily
counts per region and return a forecast list with one row per
(prediction date, region).

## Prerequisites

- Visual Studio 2022 (or the .NET Framework 4.8 build tools) with MSBuild.
- OutSystems Integration Studio, for repackaging the extension into an `.xif` for
  publication. The platform‑side DLLs in `Source/NET/Bin/` are reproduced by
  Integration Studio when you open the project; they are not tracked.

## Building

```powershell
cd Source/NET
nuget restore .\ODCTenantPoolSize.sln          # or `dotnet restore`
msbuild .\ODCTenantPoolSize.sln /p:Configuration=Release
```

The build emits `OutSystems.NssODCTenantPoolSize.dll` in `Source/NET/Bin/` and
copies the ML.NET win‑x64 native runtimes (`MklImports.dll`, `CpuMathNative.dll`,
…) alongside it via the `CopyMlNativeRuntimes` MSBuild target defined in the
`.csproj`.

## .csproj regeneration warning

Integration Studio rewrites `Source/NET/ODCTenantPoolSize.csproj` on every
Source‑code‑edit cycle. After regeneration, the `FORECAST_DEPENDENCIES` block
(NuGet `PackageReference`s for `Microsoft.ML`, `Microsoft.ML.TimeSeries` and
`MathNet.Numerics`, plus the `CopyMlNativeRuntimes` and
`ExcludeUnusedMlAssemblies` targets) and the additional `<Compile Include>`
entries for the forecasting source files must be reapplied. The block is
preserved verbatim in the committed `.csproj` for that purpose.
