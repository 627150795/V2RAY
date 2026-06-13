# Contributing

ProxyMonitor welcomes reproducible bug reports, sanitized compatibility samples, documentation improvements, and focused pull requests.

## Before submitting

- Never include subscription URLs, node credentials, API secrets, private addresses, or personal configuration databases.
- Describe the exact Windows version, proxy client version, and capability being tested.
- Keep client integrations conservative. Reading configuration does not imply that testing or switching is safe.

## Build and test

```powershell
./setup-upstream.ps1
dotnet build ProxyMonitor.csproj -c Release
dotnet run --project ProxyMonitor.csproj -c Release -- --self-test
```

Run `ProxyMonitor.exe --diagnose` for a read-only capability report. Do not run real speed tests or switching operations in automated tests.

## Pull requests

- Keep changes scoped and explain behavioral impact.
- Add or update self-tests for scoring, compatibility, and switching safeguards.
- Update the compatibility matrix when adding or changing a client capability.

