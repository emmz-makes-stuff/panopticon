# Stopped: test sign-in cookies expired against the real clock

Section 1 (`GET /api/repos`) is written and uncommitted, task 1.1 unticked. Base commit `6da0587`.

## What blocked

`make gates` gave `TEST_EXIT:2` and `FORMAT_EXIT:2`.

- **Format:** `RepoEndpointTests.Day` broke the `_` prefix rule. Renamed to `_day`, fixed.
- **Tests:** 36 server tests fail with "Creating PAN answered Unauthorized", and 7 CLI `LoginTests` fail. These aren't
  from this change. `ManualClock.StartingAt2026()` starts at 2026-11-08 09:00 UTC, and `Sessions.Lifetime` is 7 days,
  so the session cookie's `Expires` is 2026-11-15 09:00. The test client's `CookieContainer` judges expiry by the real
  clock, which passed that today, so it drops the cookie. `main` fails the same way from 2026-11-15.
- The new tests pass on their own: `RepoEndpointTests`, `VisibilityTests` and `RetiredCopyTests`, 12/12.

## Decisions so far

- Emmz confirmed (2026-11-15): five archived changes per repo, then "N more in History"; the Repos page loads once, no
  live updates. The artifacts already say so.

## Question

Fix the test clock time bomb in this change (a small test-infrastructure fix), or separately first?

## Next

Once Emmz answers: fix the cookie expiry (if agreed), rerun `make gates`, then finish section 1 (tick 1.1, section-done
note, commit). Then section 2, task 2.1 (`repoWebUrl` and `repoLink`).
