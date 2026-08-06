# Release Notes (pending)

Staging area for user-facing release notes covering work that is **committed but not yet
released**. The Release Notes dialog in Settings shows shipped versions only — every heading in
`NetworkMonitor/Views/SettingsPage.xaml` is a literal version that already has a tag and an
installer behind it.

## Why this file exists

The dialog's newest heading used to be written at runtime from `<Version>`
(`ReleaseNotesVersion.Text = $"v{AppInfo.GetVersion()}"`), on the assumption that `<Version>`
would already have been bumped to the version being written about. It hadn't: `<Version>` bumps
at installer-build time, per `CONTRIBUTING.md` → *Releasing (maintainers)*. So after the premature
0.0.11 bump was unwound in `c9eb4eb`, the heading fell back to `v0.0.10` while a literal `v0.0.10`
heading still sat below it — the dialog showed two `v0.0.10` sections, and the unreleased notes
read as though they had shipped in 0.0.10.

Keeping pending notes out of the app removes that failure mode: nothing unreleased can be
mislabelled as shipped, because nothing unreleased is in the dialog at all.

## At release time

1. Bump `<Version>` in `NetworkMonitor/NetworkMonitor.csproj`.
2. Add a new group at the top of the Release Notes dialog in
   `NetworkMonitor/Views/SettingsPage.xaml`: a `TextBlock` with the literal version
   (`FontWeight="SemiBold"`, `FontSize="16"`, `Margin="0,12,0,0"`), then one `TextBlock` per bullet
   below it, matching the existing groups.
3. Move the bullets below into that group and clear the *Next release* section here.

## Next release

- New: Horizontal mini graph. The mini graph can now be laid out as a short, wide strip instead of
  a panel — short enough to sit over the taskbar if you drag it there. Its width follows whichever
  sections you have switched on, and you set its height by dragging its top or bottom edge. Switch
  between the two layouts from the widget's right-click menu or Settings; each remembers its own
  position on screen.
- New: Show or hide the mini graph's window border, from the widget's right-click menu or Settings.
  The border is shown by default; hiding it needs Windows 11, as Windows 10 always shows it.
- The horizontal strip shortens the speed test line to the rates and ping so it does not crowd out
  the charts, and centres the speed and unknown-device readings in their cells.
- Fixed the mini graph forgetting where you left it if you had dragged it slightly past the edge of
  the screen — it would reappear in the corner of the desktop instead. This affected the floating
  panel as well as the new strip.
- Known issue: after your PC resumes from sleep, Windows can draw the taskbar over the mini graph
  and over the tray menu, even though both are correctly positioned above it. This is a Windows
  display problem rather than a fault in the app — the tray menu is affected the same way.
  Restarting Umnatha Network Monitor does not clear it; it takes a Windows restart.
