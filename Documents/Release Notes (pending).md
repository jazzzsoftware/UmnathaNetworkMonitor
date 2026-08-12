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

- New: Chart colour schemes. Settings has a new Theme tab, between Devices and Other, with five
  colour schemes to choose from — Classic, Horizon, Aurora, Ember and Ocean — plus a Custom option
  where you pick your own colours by selecting a swatch. The choice applies straight away, across the Internet, Local and
  Speed test charts, the mini graph widget, the coloured Download/Upload figures in the grids, and
  the chart legends. Each colour is automatically adjusted to stay readable on both dark and light
  cards, so a scheme looks right whichever mode you are in. The daily digest report keeps its own
  fixed colours and is not affected. If you have not changed anything, you may notice the default
  chart colours are very slightly lighter than before — that is this same readability adjustment
  applying to the default scheme too.
