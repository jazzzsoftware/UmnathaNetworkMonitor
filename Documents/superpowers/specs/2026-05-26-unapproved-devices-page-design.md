# Unapproved Devices Page — Design Spec

**Date:** 2026-05-26
**Status:** Approved

---

## Overview

Add a dedicated **Unapproved Devices** page that shows network devices discovered in the last 24 hours that have not yet been approved (i.e. `IsKnown = false`). The page mirrors the layout of `ApprovedDevicesPage` but uses its own ViewModel and is scoped to the 24-hour activity window.

---

## Architecture

| File | Change |
|---|---|
| `NetworkMonitor/ViewModels/UnapprovedDevicesViewModel.cs` | New |
| `NetworkMonitor/Views/UnapprovedDevicesPage.xaml` | New |
| `NetworkMonitor/Views/UnapprovedDevicesPage.xaml.cs` | New |
| `NetworkMonitor/MainWindow.xaml` | Add nav item |
| `NetworkMonitor/MainWindow.xaml.cs` | Add route |
| `NetworkMonitor/App.xaml.cs` | Register ViewModel as transient |

---

## UnapprovedDevicesViewModel

### Data query

Loads devices where:
```
!IsKnown && (IsOnline || LastSeen >= DateTime.Now - 24h)
```

Same 24-hour window used by `DevicesPage`. Uses `IDbContextFactory<AppDbContext>` for async DB access, consistent with existing ViewModels.

### Observable properties

| Property | Type | Purpose |
|---|---|---|
| `Devices` | `ObservableCollection<Device>` | Filtered and sorted device list |
| `SearchText` | `string` | Search input; filters Name, IP, MAC, Vendor, Type |
| `StatusText` | `string` | e.g. "3 unapproved devices" |

### Methods

| Method | Behaviour |
|---|---|
| `LoadAsync()` | Queries DB, applies search filter, populates `Devices` |
| `ApproveAsync(Device)` | Opens approve dialog (FriendlyName, Type, Notes), sets `IsKnown = true`, saves to DB, removes device from collection |
| `Sort(string property, bool ascending)` | In-memory sort on IsOnline, Type, DisplayName, IpAddress, MacAddress, Vendor |

No scan logic — scanning stays on the Devices page.

---

## UnapprovedDevicesPage

### Layout

Identical structure to `ApprovedDevicesPage`:
- Search box (top)
- `DataGrid` with columns: **Status** | **Type** | **Name** | **IP** | **MAC** | **Vendor** | **Actions**
- No "Scan Network" button

### Actions column

| Button | Behaviour |
|---|---|
| Approve | Opens the same approve dialog as `DevicesPage` — prompts for FriendlyName, DeviceType, and Notes, then sets `IsKnown = true`, saves to DB, and removes the row from the list |
| History | Navigates to `HistoryPage` with device MAC address |

The Approve button and dialog are identical in behaviour and appearance to the one on `DevicesPage`. No Edit button, no Delete button.

### Row highlighting

No orange highlight — all rows are unapproved by definition, so the highlight adds no information.

---

## Navigation

New item added to `MainWindow.xaml` NavigationView between **Devices** and **Approved Devices**:

- **Label:** Unapproved Devices
- **Tag:** `unapproved-devices`
- **Routes to:** `UnapprovedDevicesPage`

---

## Out of Scope

- Bulk approve action
- Sorting preference persistence (can be added later if needed)
- Push notifications for new unapproved devices
