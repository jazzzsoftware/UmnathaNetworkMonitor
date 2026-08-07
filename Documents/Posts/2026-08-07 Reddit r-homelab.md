# r/homelab post — Umnatha Network Monitor

## Status

First attempt was removed. Cause is almost certainly the **minimum subreddit karma
requirement** attached to the two software flairs introduced by the mod team's Rule 7
announcement, not the site-wide spam filter. Nothing to work around — participate in the sub
first, then repost.

## The rules this post has to satisfy (r/homelab Rule 7, effective ~June 2026)

A software project post must use a `Project Showcase: Software` flair, disclose AI use in
**both** the flair and the body, meet the karma minimum, and answer four prompts:

| Prompt | How this post answers it |
|---|---|
| Repo link with ≥1 month of commit history and screenshots | Public repo first commit 2026-07-05, 174 commits, screenshots in the README |
| The problem it solves, and why not an existing FOSS solution | The "Why not just use ..." section |
| How it is relevant to r/homelab | The "Why this belongs here" section |
| If AI was used, what role it played and how much you relied on it | The "How it was built" section |

Rule 6 was narrowed at the same time to cover only monetised and commercial promotion.
Non-commercial personal projects are explicitly permitted, and this one is MIT, free, with no
paid tier, no sponsors and no referral links.

## Posting mechanics

Post as a **text post in Markdown mode**. A Reddit image or gallery post has no body text
field, and Markdown mode cannot embed uploaded images, so the write-up and the pictures
cannot share a gallery post. Screenshots are served from GitHub instead of an image host —
the README displays them with captions and does the job an album would.

Verified 200 image/png on 2026-08-07:

```
https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/TeaserLocal.png
https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/HorizontalMiniGraphInTaskBar.png
```

These track `master`, so moving anything under `Documents/Images/` breaks the links in a live
post. Pin a commit SHA instead of `master` if a link ever needs freezing.

**Unwrap the paragraphs before pasting** — this file is hard-wrapped for readability and
Reddit may render those newlines as visible line breaks.

## Flair and tags

- **Flair: `Project Showcase: Software - Mostly AI Generated`.** Claude Code wrote the code
  at my direction, which is that flair's definition. The alternative flair would be a
  misleading disclosure, removable under Rule 7 — and trivially falsifiable, since 149 of the
  174 public commits carry a `Co-Authored-By: Claude` trailer, `CLAUDE.md` is in the repo
  root and `Documents/superpowers/` holds the specs and plans.
- **Creator Content** — tick it. Original work by the poster.
- **Brand Affiliate** — off. No commercial relationship, no money involved.
- **NSFW / Spoiler** — no.

## Formatting note

The mod announcement asks people not to use "excess emoticons and outline formatting that
LLMs like to use". On a post flagged as AI-generated, formatting that looks AI-written
invites exactly the dismissal the post is trying to avoid. The body below is deliberately
prose-heavy, with minimal bold and no emoji. Keep it that way when editing.

---

## Title

I built a network monitor to see what my Home Assistant devices were actually doing — it turned into a general network monitor

Alternates:

- A free, open-source Windows network monitor: per-process bandwidth from the kernel, device tracking by MAC, all stored locally
- NetSpeedMonitor replacement for Windows 11, plus per-device LAN traffic and device tracking

---

## Body

I'm the author, and per the flair: this was built with Claude Code. There's a section at the
end covering exactly what that means and how much of it I wrote by hand, because I'd rather
you had that up front than discovered it in the commit log.

It started because I had a pile of Home Assistant IoT devices and automations and no real
idea what any of them were doing on my network. What was actually there, what appeared and
disappeared overnight, and what was talking to what. It grew from there into a general
network monitor.

Umnatha Network Monitor is a Windows desktop app — .NET 10, WinUI 3, MIT licensed. Free, no
paid tier, no account, no cloud, no telemetry. Everything lives in a local SQLite database on
the machine it runs on. Source and screenshots:
https://github.com/jazzzsoftware/UmnathaNetworkMonitor

Why not just use an existing tool

This is the fair question and I want to answer it properly rather than hand-wave it.

If you want network-wide flow data, the FOSS options are better than anything I could write:
ntopng, pfSense or OPNsense with Suricata, Zeek, or a SPAN port and a box to receive it. They
see the wire, which means they see traffic between devices I can never see. If that's your
requirement, go and use those — I do too, and this isn't competing with them.

What none of them can do is tell you which *process* on a Windows machine is responsible for
those bytes. That information only exists on the host, in the kernel. A router sees an IP and
a port; it cannot know that the 40 GB was Macrium rather than a browser. So there's a real
gap, and the tools that fill it on Windows are mostly closed and commercial — GlassWire and
NetLimiter are the obvious ones. Windows' own Resource Monitor shows per-process traffic live
but keeps no history, doesn't separate LAN from WAN, and knows nothing about the other
devices on your network. Fing does device discovery well but not per-application bytes, and
it's freemium.

What I couldn't find was one free, open-source Windows app that does both halves: tracks
every device on the network by MAC over time, and attributes per-process bandwidth split into
internet and local traffic, with the history kept locally and nothing phoning home. If that
exists and I missed it, I'd genuinely like to know — I'd have happily used it instead.

Why this belongs here

Most of us have at least one Windows box in the lab, and it's usually the one whose traffic
is hardest to account for. Concretely, what it gets used for here: knowing which IoT and Home
Assistant devices are actually present and when they vanish; getting told when something
unknown joins; seeing at a glance that a NAS backup is saturating the LAN and which machine
started it; and having a daily record of it all rather than a live view you have to be
watching at the right moment.

What it does

It scans the subnet on an interval using a ping sweep, ARP table parsing and reverse DNS, and
tracks every device by MAC, so a device survives IP changes and DHCP renewals. Vendors come
from the IEEE OUI registry. Every scan also runs an mDNS/DNS-SD pass to name the things
vendor and reverse-DNS lookups can't identify — mostly randomised-MAC phones and Apple,
Google and IoT gear that would otherwise show as a bare IP. Names it discovers never
overwrite one you've set yourself.

Traffic is captured from the Windows kernel via ETW and split into Internet (WAN) and Local
(LAN) views. The Local view pivots by app or by device
([screenshot](https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/TeaserLocal.png)),
so a NAS backup climbs to the top as a large upload tagged SMB. All the device-discovery
chatter — mDNS, SSDP, LLMNR, WS-Discovery — folds into one collapsible row so it doesn't
drown out real transfers.

There's an hourly Cloudflare speed test with download, upload, latency and jitter charted
over time; a daily digest report of device activity and traffic that exports to PDF or CSV;
and toast plus in-app alerts when a device appears or disappears, which can be limited to
unknown devices only.

There's also an optional always-on-top mini graph showing live Internet and Local throughput,
the last speed test and any unknown devices. It can be laid out as a short, wide strip that's
low enough to sit on the taskbar if you drag it there
([screenshot](https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/HorizontalMiniGraphInTaskBar.png)),
which is roughly what NetSpeedMonitor used to do before deskbands were removed from Windows.

What it can't do, and what to know before running it

Per-app traffic is only for the PC it runs on. ETW attributes bytes to local processes; it
cannot see traffic between two other devices on your LAN. Device presence and history
tracking is network-wide, but per-application byte attribution is not, and no host-based tool
can change that.

It runs as administrator. The ETW kernel network session is admin-only — there is no
user-mode way to attribute bytes to a process — so it relaunches itself elevated if you don't
start it that way. That elevation is used for the ETW session and the scan and nothing else:
no drivers, no system settings, nothing written outside its own data folder. The source is
all there if you want to check that before running a network scanner elevated, which is a
reasonable thing to want to do.

The hourly speed test moves real data, roughly 750 MB per accurate run, so about 18 GB a day
if you leave it on. That's fine on an unmetered line and should be switched off if you're
capped. It's a setting.

Windows only, x64, Windows 10 build 17763 or later. There won't be a Linux or macOS build —
it's built on WinUI 3 and Windows-specific kernel tracing.

SMB shows up under System rather than the app that started the copy. That's a Windows fact
rather than a bug: the kernel SMB redirector owns the socket. Resource Monitor reports it the
same way. The app surfaces it honestly instead of guessing.

How it was built, and how much of it is AI

Per the flair, this is mostly AI-generated code. Being specific about that, since the rules
ask and vagueness helps nobody: Claude Code wrote effectively all of the code. I wrote very
little of it by hand. What I did was decide what to build, argue about the design, review the
output, test it on my own network, and reject the parts that were wrong.

The reason I'm comfortable posting it rather than quietly not mentioning it is the process,
which is all committed to the repo and can be read rather than taken on faith:

Every non-trivial feature starts as a design spec, written and approved before any code — the
problem, the alternatives considered, why the rejected ones were rejected, and the database
impact. The spec for the taskbar strip, for instance, opens by establishing that a genuine
in-taskbar dock is impossible on Windows 11, since deskbands were deprecated in 1809 and the
Windows 11 taskbar dropped toolbar hosting entirely, then compares the three remaining routes
and explains the trade-off behind the one chosen. That spec exists because the process forced
the question before any code was written, not after.

Each approved spec becomes a task-by-task implementation plan with a measured test-count
baseline that no later task is allowed to fall below. Two full structured code reviews are
recorded in the repo — one found 54 findings, all resolved; the second found 46, of which 44
were fixed and 2 were deliberately left with the reasoning written down. There's a separate
performance review covering data access, the ETW pipeline, scanning and UI rendering. The
test suite is at 307 and is run at every task boundary, not just before a release.

The specs, plans and review ledgers are in Documents/superpowers/ and Documents/Code Review/
if you want to check any of that rather than believe it.

I'm aware "AI wrote it" is a reason for scepticism on this sub and I don't think that's
unfair. The repo is the argument, not this paragraph.

https://github.com/jazzzsoftware/UmnathaNetworkMonitor

Happy to answer anything, about the app or the process. Genuinely interested in what's
missing for a homelab setup — the roadmap is short and mostly driven by what people ask for.

---

## Modmail (send before reposting, since the karma minimum isn't met yet)

Send via the **Message Mods** button at the top of the sub's moderator sidebar. Not to an
individual moderator, and not to the bots in that list. If the button errors, it's usually a
browser extension breaking the `reddit.com` → `mod.reddit.com` handoff; try a private window.

```
Subject: Software project post — karma requirement question

Hi mods,

I posted a software project before I'd read the new Rule 7 announcement, and it was
removed: <post permalink>

I've since read it properly. If I repost, it would be under "Project Showcase: Software -
Mostly AI Generated" — it was built with Claude Code working at my direction, and the post
says so in the first few lines. It's free, MIT, no paid tier, no sponsors and no referral
links, so I don't believe Rule 6 applies.

The repo has just over a month of history and 174 commits with screenshots in the README,
and the post covers the other three prompts (problem solved, why not an existing FOSS tool,
relevance to homelab).

My question is the subreddit karma minimum — I don't meet it yet. Should I spend time
participating in the sub first and repost later, or is there a process for a one-off
review? Happy either way, just don't want to trip the rule twice.

Thanks.
```

Send once. Don't follow up for several days.

---

## Likely comments, and answers worth having ready

- **"Why not pfSense/OPNsense/ntopng?"** — Already answered in the body, but expect it anyway.
  Complementary, not competing: they see the wire, this sees which process is responsible.
  Never argue the point; concede it and explain the gap.
- **"AI-written means unmaintained slop."** — Point at the review ledgers, the 307 tests and
  the commit history rather than at an argument. The 2026-07-27 review summary is the single
  best link.
- **"Why does it need admin?"** — ETW kernel network session; no user-mode equivalent.
- **"Does it work on Windows Server / in a VM?"** — Unverified. Say so rather than guessing.
- **"Will you sign the installer?"** — Not yet. Auto-update verifies SHA-256 against the
  checksum published with each release; Authenticode is a known gap, recorded as a deliberate
  won't-fix until code signing is in place.
- **"Isn't this just GlassWire?"** — GlassWire is closed-source and freemium, and does no
  device tracking or history. Fair comparison though; don't be defensive about it.

---

## Notes and findings (2026-08-07)

Recorded so the Hacker News, Product Hunt, Dev.to and Hashnode posts don't rediscover all of
this from scratch.

### What happened

Posted, and it came back "Sorry, this post was removed by Reddit's filters." The first
diagnosis — Reddit's site-wide spam filter reacting to a new account posting several external
links — was wrong. Reading r/homelab's Rule 7 announcement afterwards gave the real cause:
both `Project Showcase: Software` flairs carry a **minimum subreddit karma requirement**
enforced by AutoMod, and the account has no karma in that sub. Nothing to work around; the
rule is doing what it was written to do.

**Lesson worth carrying to every other platform: read the sub's own rules announcement before
writing the post, not after it is removed.** The rules dictated the post's structure — the
FOSS comparison, the relevance section and the AI disclosure are all mandatory there — so
writing the post first meant rewriting it.

### Reddit mechanics learned

- **Self-text posts cannot render images inline**, in either editor mode. `![](url)` is
  stripped. `[screenshot](url)` links are the most that's possible.
- **Image and gallery posts have no body text field**, only per-image captions, and Markdown
  mode cannot embed uploads. So a picture-led post means putting the write-up in your own
  first comment. Rejected here: the caveats have to be in the post people read.
- **No image host is needed.** `raw.githubusercontent.com/<owner>/<repo>/master/<path>` serves
  the PNGs directly (verified 200 image/png), and the README acts as the album. Links track
  the branch, so moving a file breaks a live post.
- **You can edit a post's body and flair at any time, but never its title.** Editing a removed
  post does not resubmit it — only a moderator approving it brings it back.
- **Modmail:** use the **Message Mods** button in the sub's sidebar. The
  `reddit.com/message/compose?to=/r/homelab` form fails with "You can't message that user"
  because the leading slash is read as a username — encode it as `%2Fr%2Fhomelab` or type
  `r/homelab` with no slash. If the button itself errors, it is usually a browser extension or
  strict cookie blocking breaking the `reddit.com` → `mod.reddit.com` session handoff; a
  private window or a different client (the mobile app) is the quickest test.
- **Modmail is a courtesy, not a gate.** The Rule 7 announcement already permits reposting a
  removed post that meets the new requirements, so no private permission is needed.
- **Tags:** *Creator Content* means original work by the poster — tick it. *Brand Affiliate*
  is for commercial relationships — off, since this is MIT with no paid tier, sponsors or
  referral links.

### Decisions and why

- **Honest AI flair, not the softer one.** Understating it is trivially falsifiable: 149 of
  174 public commits carry a `Co-Authored-By: Claude` trailer, `CLAUDE.md` is in the repo root
  and `Documents/superpowers/` holds the specs and plans. Rule 7 makes a misleading disclosure
  removable, and being caught is far more costly than the flair.
- **Caveats stated up front rather than left for the comments.** On a sub primed to find the
  limitation, naming it yourself converts the objection into credibility. The one that matters:
  per-process attribution only covers the machine it runs on.
- **Prose, not outline formatting.** The mod announcement explicitly asks people to avoid the
  bold-and-bullet style LLMs produce. On a post flagged as AI-generated, looking AI-written
  invites the dismissal the post is trying to avoid.
- **The process evidence is the argument.** Specs, two code reviews, a performance review and
  307 tests answer "AI slop" in a way no amount of assertion does — but only because they are
  committed and readable, not claimed.

### Next steps

1. Take part in r/homelab normally until the karma minimum is met. Worth doing on its own
   terms — an account with a comment history is read very differently from one whose first act
   is promoting its own project.
2. Repost with the `Project Showcase: Software - Mostly AI Generated` flair, the rewritten
   body, and Creator Content ticked.
3. Leave the removed post in place meanwhile; it does no harm and the permalink is still
   needed if modmail starts working.
