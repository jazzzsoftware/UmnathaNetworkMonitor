# Audio Science Review post — Umnatha Network Monitor

## Status

Draft, not yet posted.

## Where it goes

**Forum:** Audio Science Review → *Fun topics, videos, etc.* — the casual catch-all
subforum, chosen deliberately. It is where random personal stuff lives, so the post is
written as "made a thing", not as an announcement. A formal launch-style write-up would
read as out of place there and would invite the promotion objection that a free
non-commercial project does not otherwise attract.

Check the current forum rules before posting. Rules change and this draft was written
against a stale understanding of them.

## Posting mechanics

ASR runs **XenForo**, not Reddit. It does **not** render Markdown — pasting the body
below with `-` bullets and `**bold**` will show those characters literally.

- Paste the body as plain text, then apply bullets and bold with the editor's toolbar.
- An image URL on its own line auto-embeds in XenForo. The three links below are raw
  GitHub URLs, already proven to serve as `image/png` from the r/homelab work.
- **Unwrap the paragraphs before pasting.** This file is hard-wrapped for readability, and
  XenForo preserves newlines as line breaks — pasted as-is, every paragraph would come out
  ragged. Join each paragraph into one line first.
- Those URLs track `master`, so moving anything under `Documents/Images/` breaks the
  images in a live post. Pin a commit SHA instead of `master` if a link needs freezing.

## Tone notes

- No jargon. Nothing about ETW, ARP, mDNS, SQLite or the kernel — the whole point of this
  draft is that a non-technical reader gets it.
- The Claude Code line goes up front, not buried. ASR is an evidence-minded, sceptical
  crowd; a reader who finds it later on the repo would treat the omission as concealment,
  and that costs more than saying it plainly costs.
- No call to action, no download push, no ask. Sharing, not launching.

---

## Title

Umnatha Network Monitor: a free app that watches your home network

## Body

I've spent the last couple of months building a Windows app that watches my home network,
and it's got to the point where it's genuinely useful rather than just a toy.
I wrote it with Claude Code as an exercise to learn agentic coding while applying software engineering
principles. Judge it on whether it works for you.

It started because I have a lot of Home Assistant gadgets and no real idea what any of
them were up to. It grew from there.

What it does:

- Keeps a list of everything on your network, and logs when each thing drops off and comes
  back. Handy the morning after something misbehaved — if an endpoint vanished at 9pm,
  it's in the log rather than in your imagination.
- Shows which app on your PC is using the connection, live. It's often illuminating to
  find out what's quietly chewing through bandwidth while you're streaming music.
- Runs a speed test every hour, so you end up with a history instead of the single number
  a website gives you on the rare occasion you remember to check.
- Has a small always-on-top widget if you'd rather not keep a whole window open. It'll
  happily sit on the taskbar.

https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/Devices.png

https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/TeaserInternet.png

https://raw.githubusercontent.com/jazzzsoftware/UmnathaNetworkMonitor/master/Documents/Images/MiniGraph.png

It's free, open source, Windows only, and nothing leaves your machine — it all stays in a
file on your own PC. No account, no cloud, no telemetry, no paid version.

https://github.com/jazzzsoftware/UmnathaNetworkMonitor

I hope you find it useful.
Happy to answer anything.
