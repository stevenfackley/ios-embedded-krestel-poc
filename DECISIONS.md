# Decisions

## 2026-08-19 — Dependabot sweep: test-toolchain majors

**Status:** accepted (awareness-only stub per saved sweep policy)
**Decision:** merged #15 and #14 on green CI.
- **xunit.runner.visualstudio 2.8.2 → 4.0.0** (#15): double-major jump. Zero-discovery is the failure mode — confirm test counts in the next CI run's logs.
- **Microsoft.NET.Test.Sdk 17.14.1 → 18.9.0** (#14): toolchain-only; POC repo, build green is the bar.

**Why no review:** sweep policy — CI gates, revert cheap; POC repo with no deploy surface.
