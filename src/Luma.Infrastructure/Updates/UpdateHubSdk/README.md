# Vendored UpdateHub client SDK

These four files are a **verbatim copy** of `sdk/UpdateHub.Client/` from the
[UpdateHub](https://github.com/Elerdir/updatehub) repository. Do not edit them here —
change them upstream and re-copy, or the two will drift.

## Why vendored rather than referenced

UpdateHub's integration guide offers three ways in: a `ProjectReference` across
repositories, a NuGet package, or copying the source. The project reference only works
if both repositories sit side by side, which is not true on a CI runner, and the NuGet
package is not published yet ("until NuGet package is published"). Copying is the option
the guide names for exactly this case.

The docs state the 1.x SDK surface is stable and that the server API is
backward-compatible, so this copy does not need routine refreshing.

## Replacing it later

When `UpdateHub.Client` reaches NuGet, delete this folder and add:

```xml
<PackageReference Include="UpdateHub.Client" Version="1.*" />
```

Nothing else has to change: `UpdateHubUpdateService` is the only code that touches these
types, and the namespace stays `UpdateHub.Client`.
