# ScreenTime Guardian — Development Plan

Created: 2026-11-13 · Status: **awaiting acceptance — nothing executed yet**

This plan was produced from a full read of the source (Service, LockScreen,
Config, Shared, installer, README) plus live inspection of the installed
state on this machine. Every "verified" claim below was checked with a tool
run, not assumed.

---

## 1. Current state (verified today)

| Fact | Evidence |
|---|---|
| **Build is RED** — 4 errors, all in `LockScreen/App.xaml.cs` | `dotnet build`: `GetDisplayDpi` undefined (L51), `scaleY` undefined (L62), `Screen.DpiX/DpiY` do not exist (L73–74). WinForms `Screen` has no DPI members — the file is a half-finished refactor. |
| **No git repository** | `git rev-parse` → `fatal: not a git repository`. One bad edit from unrecoverable. |
| **ACL lockdown is NOT in effect on this machine** | `icacls %ProgramData%\ScreenTimeGuardian` shows inherited ACEs incl. `BUILTIN\Users:(CI)(WD,AD,WEA,WA)` on the folder and `Users:(RX)` on `config.json`. install.bat's `/inheritance:r` lockdown (SYSTEM+Administrators only) did not apply here → **a standard user can read the PIN and can write files into `ipc/` — i.e. queue their own `STG_UNLOCK` and self-unlock.** |
| Dev-test config still live | `config.json` has `UsageDurationMinutes: 2` (must return to 30 when testing ends). |
| Dead / unfinished code | `ActivityMonitor.UpdateActivity()` never called; `GetSystemIdleTime()` never used; `IpcMessages.STATUS_REQUEST/RESPONSE` defined but never dispatched; `Win32.BlockInput` P/Invoke unused; mouse-hook callback reads the struct and does nothing. |
| README over-claims | "Blocks Ctrl+Alt+Del" — impossible: the SAS is handled by winlogon on a secure desktop; no user-mode hook ever sees it. (Task Manager *is* covered, but by the HKLM policy, not the hook.) |

---

## 2. Challenge analysis — current choices vs. problems

### A. Architecture

**A1. GUI launched from a Windows service (session 0).**
*Current:* `LockController` `Process.Start`s the WPF overlay from the service.
*Problem:* session 0 has no interactive desktop — the window almost certainly
never appears (README's own field checklist anticipates "exited immediately
with code N"). The same applies to the PowerShell/NotifyIcon **notifications**:
a balloon raised in session 0 is invisible to the signed-in child, so the
5-min/1-min warnings are also presumed dead when running as a service.
*Challenge:* this is not a bug to patch around — it invalidates the core
delivery mechanism. Two viable shapes:
1. **CreateProcessAsUser** (recommended first spike): LocalSystem holds
   `SeTcbPrivilege`, so it can call `WTSQueryUserToken` for the active
   session and spawn the overlay (and a toast helper) directly into the
   child's session. No extra installed component, IPC model unchanged.
2. **Logon agent**: a small process started at user logon (scheduled task
   `/it` or Run key) that the service signals. More moving parts, but it also
   hosts notifications and survives being the natural place for future UI.
*Spike decides; both are cheap to test with the existing `dev-run.cmd` A/B.*

**A2. Two independent clocks for one break.**
*Current:* service `TimerManager` counts the break AND `LockMainWindow` runs
its own `DispatcherTimer` countdown that sends `STG_UNLOCK` at zero.
*Problem:* after a service restart mid-break the two disagree; states like
"service thinks usage, overlay thinks break" are reachable. They mostly
cancel out today only by luck (each side tolerates the other's unlock).
*Recommendation:* the service is the single source of truth; the overlay
renders remaining time it is *told* (the unused `STATUS_*` IPC is the natural
channel). Keep the overlay's own countdown only as a dead-man fallback if the
service goes silent.

**A3. `DateTime.UtcNow` as the timer.**
*Problem:* subject to clock steps (NTP, manual changes); elapsed can jump or
go negative. *Recommendation:* monotonic (`Stopwatch`/`Environment.TickCount64`)
for in-run elapsed; wall clock only when reading/writing persisted state.

**A4. No state persistence — reboot/restart is a full bypass.**
*Current:* `Start()` sets `_usageStartTime = UtcNow`; startup runs
`ClearStaleProtection()` and drops everything.
*Problem:* (1) child reboots during a break → break gone; (2) child reboots
after 25 min of usage → fresh 30 min. A watchdog that restarts the service
also silently resets the budget.
*Recommendation:* persist `{usageAccumulated, breakEndUtc}` to an ACL'd state
file; on startup restore it — resume an **active** break (re-lock), keep
clearing only *stale/expired* protection. Trade-off (corrupt state must never
trap the machine) is handled by: state file is validated, any parse failure ⇒
treat as no-state and log loudly.

### B. Enforcement / security

**B1. PIN passed on the command line** (`--pin`).
*Problem:* visible to any process via WMI `Win32_Process.CommandLine` —
readable by a standard user even for SYSTEM processes. The overlay could read
`config.json` itself (it runs as SYSTEM and the file is SYSTEM-readable).
*Recommendation:* drop `--pin`; overlay loads `AppConfig.Load()`.

**B2. No rate limiting on PIN attempts.**
*Problem:* 6 digits, unlimited tries, instant feedback. A pre-running
auto-typer could brute-force during a break.
*Recommendation:* 5 wrong → 30 s lockout + `GuardLog` entry.

**B3. Config app has no elevation requirement.**
*Problem:* once B-fix lands and the folder is genuinely locked to
SYSTEM+Administrators, a non-admin parent can no longer save settings — and
today's *unlocked* ACL is what makes the config app work at all. The two must
change together or the app breaks in one direction or the other.
*Recommendation:* `requireAdministrator` manifest on Config.exe, README step
updated, and install.bat must **verify** the lockdown (echo `icacls`, fail
loudly) using SID grants (`*S-1-5-18`, `*S-1-5-32-544`) so non-English
Windows can't silently skip it — the script already warns this can happen;
on this machine it evidently did.

**B4. Config file I/O is neither guarded nor atomic.**
*Problem:* `AppConfig.Load()` has no try/catch — a half-written
`config.json` (write is a bare `File.WriteAllText`) throws at service
startup → crash loop with the watchdog dutifully restarting it every minute.
This violates the project's own "GuardLog never throws / logging must never
break lock/unlock" invariant, extended to config.
*Recommendation:* load → try/catch → defaults + loud log; save → temp file +
`File.Replace`.

**B5. `ProcessGuardian.StopProtection` blindly deletes `DisableTaskMgr`.**
*Problem:* if the machine had that policy set for another reason (domain /
other software), a completed break destroys it. Also a tiny race: a
`GuardianTick` already past its `_isProtecting` check can re-set the policy
*after* `StopProtection` cleared it, leaving Task Manager disabled with no
timer left to fix it.
*Recommendation:* remember whether *we* set the value (or its prior value)
and restore that; guard tick/stop with a lock or re-check inside the tick.

### C. Usage accounting

**C1. Idle detection is dead code — and its design can't work anyway.**
*Current:* `ActivityMonitor` is event-driven (`UpdateActivity()` must be
called on input) — but a service in session 0 never receives input events,
so `_lastActivityTime` freezes at construction and after 5 minutes the monitor
permanently reports "idle" while doing nothing useful. The *stateless*
`GetSystemIdleTime()` (`GetLastInputInfo`) exists but is never called.
*Challenge:* before wiring anything, **spike whether `GetLastInputInfo` from
session 0 reflects the user's session at all.** If it doesn't, idle detection
needs a user-session helper anyway (see A1 — the same agent solves it).

**C2. What should count as "usage"? — the semantic decision behind C1.**
*Current:* every wall-clock second counts (README documents this as
deliberate).
*Challenge to the obvious fix:* naively subtracting input-idle time means a
child watching a 2-hour movie or using a **gamepad** (XInput doesn't refresh
`GetLastInputInfo`) burns zero usage — unlimited video/gaming through the
"smart" version of this feature. Per-second input sampling is the wrong
metric for screen time.
*Recommendation (default unless you choose otherwise):* **long-absence
exclusion** — count every second, except stretches where the system has been
idle longer than a threshold (e.g. >5 min, configurable). That kills the
"reboot/AFK to farm time" problem without turning passive media into free
time. The alternative (subtract all idle) is available but should be an
explicit parent-facing setting, not the silent default.

**C3. Timer thread-safety.**
*Problem:* `TimerManager` state is touched from timer callbacks and the IPC
loop with no synchronization; `ResetTimers()` re-subscribes
`ActivityMonitor.OnBecameActive` each call (handler accumulates).
*Recommendation:* one lock around transitions; subscribe once in the
constructor.

### D. Product completeness

- **Settings don't apply until service restart** — `Worker` loads config once
  and closures capture it; `TimerManager._config` is set only in `Start()`.
  Add `STG_CONFIG_CHANGED` (or a `FileSystemWatcher`) + reload.
- **No status surface** — `STATUS_REQUEST/RESPONSE` are defined, never used.
  Config app could show service-alive / usage remaining / break remaining.
- **Overlay death mid-break = break unenforced** — if the lock process exits
  while on break, nothing relaunches it; policy stays on but the child has a
  usable desktop. Add supervision: on unexpected exit while `_isOnBreak`,
  relaunch with backoff and log.
- **Notifications** fall out of A1; simplest fallback is rendering the
  5-min/1-min warnings *inside the overlay* is impossible (it doesn't exist
  yet at warning time) — so they move to whatever user-session channel A1
  selects.
- **Dead code**: `BlockInput` (considered — rejected: a stuck-block on
  process death locks the kid out with no recovery path), no-op mouse hook,
  unused STATUS constants (implement in D or delete).

---

## 3. Staged plan

Each stage has an acceptance gate; no stage starts until the previous gate is
met. (Git init in Stage 0 makes every later stage recoverable.)

### Stage 0 — Safety net + green build  *(small, first)*
1. `git init`, `.gitignore` (`bin/`, `obj/`, `publish/`), initial commit.
2. Fix `App.xaml.cs` **in one edit batch** (define `GetDisplayDpi` via
   `GetDpiForMonitor`/shcore, fix `scaleY`, rewrite `BoundsInWindowUnits`
   without `Screen.DpiX/DpiY`), rebuild.
3. Reset `config.json` `UsageDurationMinutes` 2 → 30.
- **Gate:** `dotnet build -c Debug` green; repo committed.

### Stage 1 — Session-0 viability spikes  *(highest risk — do next)*
1. Spike A: spawn the overlay into the active user session
   (`WTSQueryUserToken` + `CreateProcessAsUserW`); A/B against current
   behavior using `installer/dev-run.cmd` per the README checklist.
2. Spike B: from the service context, read `GetLastInputInfo` while moving
   the real mouse — does it track the user session?
3. Decide A1 shape (in-process token spawn vs. logon agent) from evidence.
- **Gate:** overlay window observed on the child desktop from the installed
  service; written verdict on idle-readout viability.

### Stage 2 — Security hardening  *(code-level, parallel-safe with Stage 1)*
1. PIN off the command line (overlay reads config itself).
2. PIN attempt rate limiting (5 → 30 s lockout).
3. install.bat: SID-based ACL grants, `icacls` verification that fails the
   install loudly; Config.exe `requireAdministrator` manifest — shipped
   together (B3 dependency).
4. `AppConfig` load-guard + atomic save.
5. `ProcessGuardian` prior-value save/restore + tick/stop race fix.
- **Gate:** standard-user probe cannot read `config.json` nor create a file
  in `ipc/` (run `icacls` + an actual write attempt); corrupt-config startup
  test lands on defaults instead of crash-looping.

### Stage 3 — Timer & anti-bypass core
1. Monotonic clock for elapsed.
2. Persist/restore usage + break state; resume an active break after
   restart/reboot; only stale protection is cleared.
3. C2 decision implemented (default: long-absence exclusion), README kept
   honest.
4. `TimerManager` locking + one-time event subscription.
5. Overlay supervision: relaunch on unexpected exit mid-break.
- **Gate:** scripted tests: reboot mid-break re-locks; reboot mid-usage
   keeps accumulated usage; overlay kill → relaunch ≤ a few seconds.

### Stage 4 — Product completeness
1. Live config reload (IPC or file watch).
2. Status view in Config app via `STATUS_*`.
3. Notifications on the Stage-1 channel (or drop to overlay-independent
   toast helper).
4. Dead-code sweep.
- **Gate:** change a setting while running → next tick uses it; status panel
   shows live remaining time.

### Stage 5 — Tests, docs, field test
1. Test project: `TimerManager` state machine (injected fake clock),
   `IpcMessages` ordering invariants (the `LastIndexOf('_')` rule — subtle,
   must not regress), `AppConfig` validation + corrupt-file fallback.
2. README corrections: Ctrl+Alt+Del claim, "Toast" → actual mechanism, idle
   semantics, PIN default warning.
3. Field test on the child's machine (README checklist), **not** the dev
   machine.
- **Gate:** CI-able `dotnet test` green; checklist executed with evidence
   lines from `guardian.log`.

---

## 4. Decisions I made with recommended defaults (override any)

1. **Idle semantics:** long-absence exclusion (>5 min idle doesn't count),
   NOT per-second input subtraction. → C2
2. **Break after reboot:** resume the break if `breakEndUtc` is still in the
   future; never resume if state is unreadable. → A4
3. **Session-0 fix:** try `CreateProcessAsUser` first; logon agent only if
   the spike fails. → A1
4. **Config elevation:** Config.exe becomes admin-only in the same change
   that tightens ACLs. → B3

## 5. Explicitly out of scope (for now)

- Multi-session / RDP enforcement (single console session assumption).
- Kernel-level anti-bypass (README already states the honest limits).
- Daily budgets / schedules / per-app limits — new features, after the core
  is trustworthy.
