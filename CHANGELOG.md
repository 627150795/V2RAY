# Changelog

## 0.4.6

- Added explicit table header sorting with visible direction arrows.
- Made metric columns choose useful first-click directions: reliability, speed, stability, balanced score, samples, and status sort high-to-low first; latency sorts low-to-high first.
- Preserved the active table sort after background refreshes and subscription filtering.
- Added release-page notes describing what the software does and which download normal users should choose.

## 0.4.5

- Added a dedicated ProxyMonitor application icon for the window, executable, tray, installer, and shortcuts.
- Added a default desktop shortcut task to the installer.
- Made the installer write the Windows startup command explicitly with the background launch argument.

## 0.4.4

- Changed fastest-node recommendations to require reliability eligibility and rank by rolling median speed.
- Added a 10% speed replacement threshold to prevent minor measurement noise from changing the global fastest recommendation.
- Made subscription cards recalculate all three recommendations within the selected group.
- Added a header badge showing the active recommendation scope.
- Displayed success rates with one decimal place so values just below a recommendation threshold are no longer rounded up misleadingly.

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
