# Claude Session Time

Running record of how much active Claude Code session time has gone into NetworkMonitor.
Add a new snapshot at the top each time it's re-measured.

**How it's measured:** for each session transcript (`~/.claude/projects/<project>/*.jsonl`),
take the span from its first to last timestamped message and sum across all sessions.
This is *active session time* — within-session idle/think gaps count; the time *between*
sessions does not. It reflects "time with a live Claude session open," not pure hands-on-keyboard.

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
