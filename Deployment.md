# Deployment

Set `MagicLinkAuth__PublicBaseUrl` to the public HTTPS origin, including any application path prefix. Production has no default; invalid or missing configuration stops startup. Set `AllowedHosts` to the actual deployed host names. Do not use an incoming Host header to construct login links.

Back up legacy databases before explicitly assigning orphaned heroes to a known owner. Startup does not guess ownership.

## Security baseline (22 October 2026)

Use .NET SDK 10.0.402 from global.json. Blazor, SignalR and Entity Framework packages are pinned to 10.0.13; the browser WebAssembly runtime is explicitly pinned to 10.0.13 as well. The NuGet vulnerability check includes transitive dependencies.

The update passed 39 existing authentication, authorization and request-validation tests. The full suite has eight pre-existing dice/spell/history failures (210 passed, 8 failed), reproduced from unchanged commit eaf3af67125f946f61b7267359c921d183ba427f with the same SDK. The deployment gate runs the security integration tests; the dice-related failures require separate investigation.
