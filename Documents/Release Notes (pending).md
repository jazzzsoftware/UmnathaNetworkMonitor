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
4. Strike through any `README.md` **Roadmap** entry the release ships, matching the existing
   `~~**Name** — description.~~ Shipped in vX.Y.Z.` form. The roadmap is only marked shipped at
   release, never when the work merges, so it stays accurate for whoever reads it between releases.

## Next release

- New: Chart colour schemes. Settings has a new **Theme** tab, between Devices and Other, with five
  schemes to choose from — Classic, Horizon, Aurora, Ember and Ocean — plus **Custom**, where you
  set each colour yourself by clicking its swatch. The choice applies straight away, across the
  Internet, Local and Speed test charts, the mini graph widget, the coloured Download and Upload
  figures in the grids, and the chart legends. Each colour is adjusted automatically to stay
  readable on both the dark and light card backgrounds, so a scheme looks right whichever Windows
  mode you are in.
- New: a preview on the Theme tab draws sample charts in the scheme you have picked, so you can see
  every colour in context — including the hover line, which on a real chart only appears while you
  are pointing at it.
- Changed: the chart hover line now uses your scheme's colour. Previously it was grey while you
  hovered and only picked up the scheme colour once you clicked a point.
- Note: the daily digest report keeps its own fixed colours and is not affected by any of this.
- Note: if you have not changed anything, the default chart colours are very slightly lighter than
  before. That is the same readability adjustment being applied to the default scheme.
- New: clicking a Windows notification now opens the app at the part it is about. An unrecognised
  device takes you to the Unapproved list, a device you already know takes you to its history, a
  finished scan or a network change to the device list, a speed test to the Speed test tab, and a
  daily digest to Reports. The window is restored and brought to the front first, so it works from
  the tray or minimised.
- Changed: the app does noticeably less work while you are watching it. The mini graph widget no
  longer redraws detail finer than the screen can show, and stops redrawing altogether while nothing
  is moving; the Internet app list updates its figures in place instead of rebuilding every row each
  time; device history loads without building a duplicate copy of a device for every event; and the
  live charts now reuse their working memory between updates rather than allocating it afresh
  several times a second. Nothing looks or behaves differently — it simply costs less to leave
  running all day.
- Changed: the mini graph widget's shape is now set by an **Orientation** setting offering
  **Vertical** and **Horizontal**, in place of the old "Horizontal strip" on/off switch. The two
  shapes are equal choices rather than one being the absence of the other, the wording matches the
  widget's own right-click menu, and the description underneath now describes whichever one you
  have picked.
