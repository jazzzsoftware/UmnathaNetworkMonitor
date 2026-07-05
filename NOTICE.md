# Third-Party Notices

Umnatha Network Monitor is licensed under the [MIT License](LICENSE). It depends on the following third-party software.

## QuestPDF

Used to generate the daily digest PDF report (`NetworkMonitor/App.xaml.cs`, configured with `QuestPDF.Settings.License = LicenseType.Community`).

Licensed under the QuestPDF Community License — free for organisations and projects under the revenue threshold in the license terms. See https://www.questpdf.com/license/.

## Lato font

Used by QuestPDF as the default report font. The `LatoFont/*.ttf` files (and their `OFL.txt` license) are bundled and copied to the build output automatically by the QuestPDF NuGet package — they are not vendored in this repository's source tree.

Licensed under the SIL Open Font License, Version 1.1. Copyright (c) 2010-2011 by tyPoland Łukasz Dziedzic, with Reserved Font Name "Lato". Full license text: https://scripts.sil.org/OFL.

## IEEE OUI registry

`NetworkMonitor/Assets/oui.txt` is used to identify device vendors from the first three octets of a MAC address.

Source: IEEE Media Access Control (MAC) Address Registration Authority, https://standards-oui.ieee.org/oui/oui.txt. Redistributed unmodified as public registration data.

## NuGet packages (MIT License)

- Microsoft.WindowsAppSDK
- CommunityToolkit.Mvvm
- CommunityToolkit.WinUI.UI.Controls.DataGrid
- Microsoft.EntityFrameworkCore.Sqlite / Microsoft.EntityFrameworkCore.Design
- SQLitePCLRaw.bundle_e_sqlite3
- Microsoft.Extensions.Hosting
- Microsoft.Diagnostics.Tracing.TraceEvent
- Microsoft.Graphics.Win2D
