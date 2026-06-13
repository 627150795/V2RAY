# ProxyMonitor

ProxyMonitor is a privacy-first Windows companion for monitoring proxy-node reliability across v2rayN, Mihomo/Clash, and sing-box based clients.

It performs low-bandwidth isolated health checks, keeps local history, and recommends the most stable, fastest, and best-balanced nodes without uploading subscriptions or automatically changing the active node.

## Why it exists

Proxy clients usually show only a momentary latency result. A node that looks fast once may still fail frequently, fluctuate heavily, or degrade shortly after switching. ProxyMonitor tracks behavior over time and treats recent failures as a first-class signal.

## Highlights

- Monitors enabled v2rayN subscriptions using copied configuration snapshots.
- Discovers common Mihomo/Clash and sing-box desktop clients conservatively.
- Runs delay checks every 30 minutes by default.
- Runs a serialized, capped lightweight speed test once per day.
- Stores rolling history locally in SQLite.
- Recommends stable, fast, and balanced nodes with recommendation hysteresis.
- Requires confirmation and a live pre-switch health check before v2rayN switching.
- Runs in the tray, starts with Windows by default, and remains idle at near-zero CPU.

## Safety and privacy

- No subscription URLs, credentials, nodes, or history are uploaded.
- v2rayN configuration is copied before testing; its live database is not modified.
- Speed tests are serialized and capped to reduce impact on normal browsing.
- Unsupported integrations remain read-only.
- ProxyMonitor never switches nodes automatically.

## Current integrations

| Client family | Discover/read | Delay | Speed | Switch |
| --- | --- | --- | --- | --- |
| v2rayN 7.x | Yes | Yes | Yes | Confirmed and guarded |
| v2rayN 6.x | Yes | No | No | Configuration compatibility |
| Mihomo / Clash desktop clients | Yes | When control API is available | No | No |
| sing-box desktop clients | Read-only JSON discovery | No | No | No |

See [CLIENT_COMPATIBILITY.md](CLIENT_COMPATIBILITY.md) and [COMPATIBILITY.md](COMPATIBILITY.md) for evidence and limitations.

## Scoring model

- Formal stable recommendations require at least 24 delay samples.
- Formal speed and balanced recommendations require at least 3 valid speed samples.
- Long-term and recent success rates must both reach 90%.
- Stability combines availability, recent availability, jitter, and recent P95 latency.
- Balanced score defaults to stability 45%, speed 30%, and latency 25%.
- Recent degradation and consecutive failures receive strong penalties.
- Recommendation hysteresis prevents minor score changes from constantly replacing a healthy recommendation.

Lightweight speed tests provide relative ranking, not a maximum-bandwidth benchmark.

## Build

Requirements:

- Windows 10/11 x64
- .NET 10 SDK
- Git
- Inno Setup 6, optional for producing the installer

```powershell
./setup-upstream.ps1
dotnet build ProxyMonitor.csproj -c Release
dotnet run --project ProxyMonitor.csproj -c Release -- --self-test
./build.ps1
```

`setup-upstream.ps1` clones a pinned v2rayN revision beside this repository and applies the small isolation patch stored in `patches/`.

## Diagnostics

```powershell
ProxyMonitor.exe --diagnose
ProxyMonitor.exe --recommendations
ProxyMonitor.exe --self-test
ProxyMonitor.exe --render-preview
```

`--recommendations` explains each selected recommendation, the strongest challenger,
its score gap, the replacement margin, and whether the remembered recommendation is
still eligible. Diagnostics are read-only. Sanitize output before sharing it publicly.

## Contributing

Compatibility reports and focused pull requests are welcome. Never submit real subscriptions, credentials, private node addresses, or configuration databases. See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and [ROADMAP.md](ROADMAP.md).

## Upstream and license

ProxyMonitor integrates with and adapts code from [v2rayN](https://github.com/2dust/v2rayN), which is licensed under GPL-3.0. ProxyMonitor is therefore distributed under the [GNU General Public License v3.0](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
