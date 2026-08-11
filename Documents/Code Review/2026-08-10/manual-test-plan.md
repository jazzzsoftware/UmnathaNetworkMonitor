# Manual Test Plan — 2026-08-10 review fix phase

Everything below needs the **real app on real hardware**. None of it is reachable from `NetworkMonitor.Tests`, which references Models and Core only — so 370 green tests say the fix phase broke nothing, and say nothing about whether these paths are now correct.

Build: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`, then run the app from Visual Studio (x64).

Tick a box when the **expected** result is what you actually see. If it differs, write what happened next to the box — a failed check is more useful than a blank one.

---

## Part 1 — Gates the review on (C2-2, C2-5)

**These two findings are the only ones still open.** The code fix landed in batch 6 and makes the outcome deterministic either way, but the DPI *transition* path has never been executed on real hardware — the v0.0.10 fix was verified on a **primary** 4K at 200%, where no transition occurs. Nothing else in the review is waiting.

Needs: a second monitor at a **different** scale factor from the primary (e.g. laptop at 125% or 150%, external at 100%; or primary at 100% and external at 200%). Set per-monitor scaling in Settings → System → Display → Scale.

Record the two scale factors here before starting: primary `______%`, secondary `______%`

### 1.1 Floating widget, saved on the differently-scaled monitor

- [ ] Open the widget (tray → Show mini graph, or the Mini graph toggle on the Traffic page).
- [ ] Drag it fully onto the **secondary** monitor. Resize it to something clearly not the minimum — roughly half the screen height.
- [ ] Note its apparent size relative to something on screen (a window edge, a taskbar icon). Screenshot if easier.
- [ ] Close the app completely (tray → Exit, not just the widget).
- [ ] Reopen. **Expected: the widget returns to the same monitor at the same apparent size.**
- [ ] Repeat the close/reopen **three more times**. **Expected: the size does not shrink a step each launch.** This is the C2-1 symptom and the clearest signal — if it shrinks toward a small fixed size over successive launches, the fix has not held.

### 1.2 Floating widget straddling the boundary

- [ ] Drag the widget so it **spans both monitors**, with its top-left corner on one and the majority of its body on the other. This is the exact configuration where restore and save used to disagree.
- [ ] Close the app completely, reopen. **Expected: same position, same apparent size.**
- [ ] Repeat twice. **Expected: still stable — no shrinking, no jump to another monitor.**

### 1.3 Dragging across the boundary live

- [ ] With the widget open, drag it slowly from the primary onto the secondary and back.
- [ ] **Expected: it stays under the cursor.** The grab point should not jump when it crosses (C2-9).
- [ ] **Expected: no flicker to a wildly wrong size** as it crosses (C2-2).

### 1.4 Horizontal strip on the differently-scaled monitor

- [ ] Switch the widget to the horizontal strip (right-click the widget → Horizontal, or Settings).
- [ ] Drag it onto the **secondary** monitor's taskbar.
- [ ] Close the app completely, reopen. **Expected: the strip returns flush with the taskbar, not floating a few pixels above it and not partly off-screen.** This is C2-5 — the frame insets are DPI-scaled and used to be measured on the wrong monitor.

**Outcome:** if 1.1–1.4 all pass, C2-2 and C2-5 can be marked `fixed` in `progress.md` and the review is fully closed.

---

## Part 2 — Widget lifetime (batch 5)

Not reachable by any test. C1-1, C1-2 and C5-4 are all timing windows, so repetition is the point.

- [ ] Open the widget, then press **Alt+F4** on it. **Expected: it closes cleanly, no error dialog.**
- [ ] Reopen it from the tray. **Expected: it opens and starts drawing.**
- [ ] Repeat that open/Alt+F4/reopen cycle **ten times, briskly.** **Expected: no fatal error dialog at any point.** Before batch 5 this could throw against a destroyed window, and the throw surfaced as a fatal MessageBox on what felt like just closing a widget.
- [ ] Open the widget, close it with the **× glyph** rather than Alt+F4, reopen. **Expected: same clean behaviour.**
- [ ] With the widget open, drag the **opacity slider** in Settings from end to end a few times. **Expected: smooth, no flicker, no error** — and the widget does not steal focus or re-activate on each step.
- [ ] Close the widget, then move the opacity slider again with it shut. **Expected: nothing happens and no error** (C1-10 — it no longer does work with nothing displaying it).
- [ ] Open the Reports page, generate/open a digest, then navigate away and back several times. **Expected: no error, no growing sluggishness** (C5-5 — `DigestReportView` now unsubscribes on unload).

---

## Part 3 — Strip geometry (batch 6)

- [ ] Switch to the horizontal strip and dock it on the taskbar.
- [ ] Drag its **top edge upward, well past where it stops growing** (the 120 DIP ceiling). **Expected: the strip's bottom edge stays on the taskbar.** It must not lift off (C2-4).
- [ ] Drag its **left edge** sideways and hold the drag for a few seconds. **Expected: the strip stays put horizontally** — it must not walk left across the screen (C2-4).
- [ ] Toggle each section on and off (Internet, Local, Speed, Unknown devices). **Expected: the strip resizes to fit; the cells are not stretched and there is no obvious empty space on the right** (C2-3/C2-6 — it used to reserve ~17% more width than it drew in).
- [ ] Turn sections off until only one remains, then try to turn that last one off. **Expected: it refuses — the widget can never be empty** (C5-3, now tested in Core, but worth seeing).
- [ ] At the **smallest** strip height, check the peak figure. **Expected: it is not shown.** The spec used to claim this threshold was unreachable; it is not (C2-7).
- [ ] Disconnect the secondary monitor while the widget is on it, then reopen the widget. **Expected: it comes back on-screen rather than stranded** (C2-10).
- [ ] If you have a touchscreen or pen: try to drag the widget with it. **Expected: it does not move, and above all does not teleport to wherever the mouse pointer was** (C2-8).

---

## Part 4 — Live chart correctness (batch 4)

- [ ] Start a large download. Watch the widget's Internet chart and the Internet tab side by side. **Expected: the two show broadly the same rate** — the widget must not sit consistently higher or lower.
- [ ] While the transfer runs, watch the **right-hand edge** of the trace. **Expected: it tracks the real rate rather than sagging to roughly half at the newest point** (C3-5).
- [ ] Navigate away from the Internet tab, wait **five minutes**, then come back. **Expected: the chart shows history and normal traffic — no uniform raised "floor" across the whole width** (C5-2, the phantom baseline).
- [ ] Do the same on the Local tab.
- [ ] Sit on the **24h** range for a couple of minutes, then switch to **5m**. **Expected: same — no phantom floor.**
- [ ] **Backward clock step.** With the widget open and some traffic running, set the system clock **back 20 seconds** (Settings → Time & language → Date & time → turn off automatic, adjust, apply). **Expected: the trace restarts cleanly from that point.** It must not freeze at the right edge or show inflated rates for the next few minutes (C3-1). Turn automatic time back on afterwards.
- [ ] Leave the machine asleep or the app closed for **several hours**, then start it with the widget open. **Expected: the first chart does not show an enormous spike** compressing hours of traffic into five minutes (C3-4).
- [ ] Turn **Settings → smooth chart scrolling OFF**. **Expected: the widget's charts stop animating smoothly** — previously only the full-page charts obeyed this (C3-2). Worth checking battery/CPU with it off if you use the widget all day.

---

## Part 5 — Speed test display (this session's change)

- [ ] Run a speed test normally. **Expected: the widget's speed line shows sensible figures.**
- [ ] If you can produce a very slow link (throttle, or check after a poor result): a rate **below 0.05 Mb/s** should read **`<0.1`**, not `0`. A link that is slow must not display the same as a link that is down.
- [ ] A genuinely idle/zero reading should still read `0`.

---

## Part 6 — Database and startup (batches 1 and 2)

**This is the highest-consequence part of the whole plan** — it decides whether existing users keep their history. `Tools/MigrationVerify` already proves the schema logic against temporary databases (37 checks), but nothing has yet proved it against a **real** database with real history in it.

- [ ] **Back up first**: copy `%LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db*` (all three files — `.db`, `-wal`, `-shm`) somewhere safe. Do not skip this.
- [ ] Run `dotnet run --project Tools/MigrationVerify`. **Expected: `ALL CHECKS PASSED`, exit code 0.** It works in `%TEMP%` and never touches the real database.
- [ ] Start the app against your **existing** database. **Expected: it starts normally.**
- [ ] Check the Devices page. **Expected: your device history is intact — the same devices, the same first-seen dates.**
- [ ] Check the Internet and Local tabs on a wide range. **Expected: traffic history is intact.**
- [ ] Confirm the database gained `__EFMigrationsHistory` (one row) and `__EFMigrationsLock`. Both are EF-internal and expected; no application data is affected.
- [ ] Restart the app a second time. **Expected: still starts normally, history still intact** — baselining must be idempotent.
- [ ] **Settings integrity.** With the widget open, drag and resize it continuously for ~30 seconds while a scan runs (Devices → Scan Network). Then exit and restart. **Expected: the app starts and your settings survived.** This is C1-3 — concurrent writes used to be able to truncate `settings.json` and leave an app that would not launch.
- [ ] Look in `%LOCALAPPDATA%\UmnathaNetworkMonitor\` for leftover `settings.json.*.tmp` files. **Expected: none** — a failed write cleans up after itself.

---

## Part 7 — Diagnostics and update (batches 3 and 8)

- [ ] Point `RetentionProbe` at the live folder: `dotnet run --project Tools/RetentionProbe -- "%LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db"`. **Expected: it refuses**, and the refusal names only the file, not your full path (C5-6, C5-7).
- [ ] Run it with a bad number: `dotnet run --project Tools/RetentionProbe -- somefile.db abc`. **Expected: `Not a number: abc` plus the usage line, not a stack trace.**
- [ ] Run it properly against a **copy** and confirm the report header shows only the file name, no username anywhere in the output.
- [ ] Turn logging on in Settings, then use **Check for updates**. **Expected: it works as before.** If it ever fails, the log should now contain a reason — previously a garbage response logged nothing at all (C5-1).
- [ ] Exit the app normally, then check the log. **Expected: no `RefreshUnapprovedCount` errors at shutdown** (C3-9), and if the WAL checkpoint is ever blocked it now says so rather than failing silently (C4-2).

---

## Recording the outcome

When Part 1 is done, update `progress.md`:

- All of 1.1–1.4 pass → mark **C2-2** and **C2-5** `fixed`, and set the status line to 50 of 50.
- Anything fails → note what happened against the finding, leave it `open`, and it becomes the first item of the next fix batch.

Parts 2–7 are regression confirmation for work already marked `fixed`. A failure there is a **new** finding, not a reopened one — record it as such so the review's history stays honest.
