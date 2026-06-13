# Changelog

## 0.4.3

- Added auditable recommendation diagnostics with candidate eligibility, replacement margins, and challenger score gaps.
- Expanded score diagnostics with recent success rate, recent failures, and recommendation eligibility.

## 0.4.2

- Added recommendation hysteresis to prevent minor score changes from constantly replacing healthy recommendations.
- Added guarded live health checks before v2rayN switching.
- Increased formal speed recommendation requirement to three valid samples.
- Added recent reliability and latency-degradation penalties.
- Added default Windows startup option and reduced hidden-window refresh work.
- Added installer cleanup for the Windows startup entry.
