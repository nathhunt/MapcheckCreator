# ESAPI compile-check stubs (never deployed)

Minimal stand-ins for `VMS.TPS.Common.Model.API.dll` and `VMS.TPS.Common.Model.Types.dll`, so the ESAPI code compiles on machines without Eclipse (CI, Linux). They contain **only** members used by the app (`src/SrsMcPsqa.App/Esapi`). Signatures come from the v18 API probe (`api-probe-*.txt`, ESAPI 18.1.0.472). Every body throws.

```bash
dotnet build tools/EsapiStubs/API/VMS.TPS.Common.Model.API.csproj -c Release
```

```bash
dotnet build SrsMcPsqa.sln -p:EsapiPath=$PWD/tools/EsapiStubs/out
```

Never copy these DLLs to an Eclipse machine. The real ones are in the GAC there.
