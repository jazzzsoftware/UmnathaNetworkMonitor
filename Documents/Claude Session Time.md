# Claude Session Time

Running record of how much active Claude Code session time has gone into NetworkMonitor.
Add a new snapshot at the top each time it's re-measured.

**How it's measured:** for each session transcript (`~/.claude/projects/<project>/*.jsonl`),
sum the time between consecutive timestamped messages, counting only gaps of 30 minutes or
less. Within-session think/idle pauses count; an app left open (a long idle stretch or an
overnight gap) does not. Summed across all sessions; the time *between* sessions never counts.
This is *active hands-on time*. Snapshots dated 2026-06-28 and earlier used an older raw
first-to-last span method (which counts all idle, including overnight), so they are not
directly comparable to later ones.

---

## 2026-07-25

- **Total active time:** 60.2 hours (3,610 minutes)
- **Sessions:** 22 over 18 active days
- **Span:** 2026-06-25 → 2026-07-25
- **Average:** ~3.3 hours per active day

Busiest days:

| Date | Active time | Focus |
|---|---|---|
| 2026-06-28 | 8.8 h | Speed Test |
| 2026-07-20 | 7.0 h | Local traffic redesign |
| 2026-07-15 | 5.7 h | Local traffic |
| 2026-07-16 | 5.2 h | Local traffic (app-centric) |
| 2026-06-30 | 4.9 h | — |

Notes:

- Method refined to *active hands-on time* (idle gaps over 30 min excluded) after two sessions left open overnight (2026-06-28 and 2026-07-20) inflated the raw first-to-last span to 108.5 h — roughly 48 h of that was an idle app.
- Transcripts before 2026-06-25 are no longer on disk, so this window starts 2026-06-25 and is **not cumulative** with the 2026-06-28 snapshot below (56.6 h, which spanned from 2026-06-07).

---

## 2026-06-28

- **Total active time:** 56.6 hours (3,395 minutes)
- **Sessions:** 26 over 22 calendar days
- **Span:** 2026-06-07 → 2026-06-28
- **Average:** ~2.6 hours per active day

Busiest days:

| Date | Active time | Focus |
|---|---|---|
| 2026-06-26 | 7.7 h | Code-review fix phase |
| 2026-06-22 | 6.7 h | Daily Digest |
| 2026-06-21 | 6.4 h | Daily Digest / Reports |
| 2026-06-19 | 4.7 h | — |
| 2026-06-15 | 4.3 h | — |
