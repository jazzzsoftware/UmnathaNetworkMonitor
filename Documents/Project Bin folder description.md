# Project Bin folder description

This document catalogues the contents of the compiled output ("bin") folder for the Network Monitor app and explains what each file is.

Both `SelfContained=true` and `WindowsAppSDKSelfContained=true` are set in the csproj, so the output folder is **fully self-contained** — the .NET runtime, the Windows App SDK / WinUI 3 runtime, and all native dependencies are copied in. Nothing needs to be pre-installed to run it; copy the folder and launch `NetworkMonitor.exe` (see [Portable copy](#portable-copy-side-loading-for-testing) below).

Output path (x64 Debug): `NetworkMonitor\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\`. It contains **~414 files at the root plus ~103 sub-folders** (mostly per-culture localization). The tables below list the app's own files individually and group the large, repetitive runtime families (the ~230 .NET base-class-library assemblies, the WinRT `*.winmd` / `*.Projection.dll` pairs, and the 100+ localization folders) by family with a count — enumerating each identical-purpose file line-by-line would add no information.

## The application itself

| File | What it is |
|---|---|
| `NetworkMonitor.exe` | Native **apphost** launcher — the entry-point double-clicked by the user; loads the CoreCLR and runs `NetworkMonitor.dll`. |
| `NetworkMonitor.dll` | The app's compiled managed assembly (all C# in the project). |
| `NetworkMonitor.pdb` | Debug symbols for the app assembly (Debug builds). |
| `NetworkMonitor.deps.json` | Dependency manifest listing every assembly/native lib and its runtime target. |
| `NetworkMonitor.runtimeconfig.json` | Runtime configuration (target framework version, GC/threading knobs). |
| `NetworkMonitor.pri` | Packaged Resource Index — compiled WinUI resources (themes, strings) for the app. |
| `App.xbf`, `MainWindow.xbf`, `SplashWindow.xbf` | Compiled binary XAML for the root XAML files. |
| `Views\*.xbf`, `Views\Controls\*.xbf` | Compiled binary XAML for every page and custom control. |
| `appsettings.json` | First-run default settings seed (`Scanner` section) — copied via `PreserveNewest`. |
| `Assets\oui.txt` | IEEE OUI database (MAC prefix → vendor). |
| `Assets\app.ico`, `Assets\splash-logo.png` | App icon and splash image. |
| `LatoFont\*.ttf` (+ `OFL.txt`) | Bundled Lato font family used for PDF/report text; `OFL.txt` is its Open Font License. |

## .NET 10 runtime (self-contained CoreCLR)

| File(s) | What it is |
|---|---|
| `coreclr.dll` | The CoreCLR execution engine. |
| `clrjit.dll` | The JIT compiler. |
| `clrgc.dll`, `clrgcexp.dll` | Standard and experimental garbage collectors. |
| `System.Private.CoreLib.dll` | The core BCL assembly (deepest layer, paired with CoreCLR). |
| `hostfxr.dll`, `hostpolicy.dll` | .NET host resolver + policy that pick the runtime and dependencies. |
| `clretwrc.dll`, `mscorrc.dll` | CLR ETW / error-message resource DLLs. |
| `mscordaccore.dll`, `mscordaccore_amd64_amd64_*.dll`, `mscordbi.dll` | Data-access component + debugger interface (diagnostics / crash dumps). |
| `createdump.exe` | Crash-dump collection tool shipped with the runtime. |
| `mscorlib.dll`, `netstandard.dll`, `System.dll`, `System.Core.dll` | Compatibility/facade assemblies for older references. |
| `clrgcexp.dll`, `System.IO.Compression.Native.dll`, `System.Runtime.*` natives | Native helpers for compression, interop, etc. |
| ~230 × `System.*.dll` + `Microsoft.CSharp.dll`, `Microsoft.VisualBasic*.dll`, `Microsoft.Win32.*.dll` | The **.NET Base Class Library** — one assembly per API area (`System.Net.*`, `System.Text.Json`, `System.Linq`, `System.Security.*`, `System.Xml.*`, …). Bundled wholesale because the build is self-contained. |

## Windows App SDK / WinUI 3 runtime (self-contained)

| File(s) | What it is |
|---|---|
| `Microsoft.WindowsAppRuntime.dll`, `.Bootstrap.dll`, `.Bootstrap.Net.dll`, `.pri` | The Windows App SDK runtime and the bootstrapper that initialises it for an unpackaged app. |
| `Microsoft.WinUI.dll` | Managed WinUI 3 assembly (the `Microsoft.UI.Xaml` API surface used by the app). |
| `Microsoft.ui.xaml.dll`, `Microsoft.UI.Xaml.Controls.dll`/`.pri`, `Microsoft.UI.Xaml.Internal.dll`, `Microsoft.UI.Xaml.Phone.dll`, `Microsoft.ui.xaml.resources.19h1.dll`, `.resources.common.dll` | Native WinUI XAML framework + its built-in control resources/styles. |
| `Microsoft.UI.Composition.OSSupport.dll`, `Microsoft.UI.Input.dll`, `Microsoft.UI.Windowing*.dll`, `CoreMessagingXP.dll`, `DWriteCore.dll`, `dcompi.dll`, `dwmcorei.dll`, `DwmSceneI.dll`, `wuceffectsi.dll`, `Microsoft.DirectManipulation.dll`, `WinUIEdit.dll`, `MRM.dll`, `Microsoft.Internal.FrameworkUdk.dll`, `Microsoft.InputStateManager.dll` | Native composition, text (DWrite), windowing, DWM, resource-manager and input support that WinUI sits on. |
| `Microsoft.Windows.SDK.NET.dll` | The C#/WinRT projection of the Windows metadata (the `Windows.*` API surface). |
| `WinRT.Runtime.dll` | C#/WinRT runtime that marshals between .NET and WinRT/COM. |
| `marshal.dll`, `SessionHandleIPCProxyStub.dll`, `PushNotificationsLongRunningTask.ProxyStub.dll` | Native WinRT/COM marshalling and proxy-stub DLLs. |
| ~150 × `Microsoft.Windows.*.winmd` + matching `*.Projection.dll` | WinRT **metadata + projections** for App SDK feature areas (AppNotifications, AppLifecycle, Storage/Pickers, Management.Deployment, PushNotifications, Security, System.Power, Widgets, …). Only a few are used (notifications, storage pickers); the rest ship with the SDK. |
| `Microsoft.UI.Xaml\Assets\…` | WinUI framework theme assets (control glyphs/images). |
| 100+ culture folders (`af-ZA\`, `de-DE\`, `fr-FR\`, `zh-CN\`, …) each with `Microsoft.ui.xaml.dll.mui` + `Microsoft.UI.Xaml.Phone.dll.mui` | Localized WinUI resource (MUI) satellites — one folder per language the App SDK ships. |
| `RestartAgent.exe` | Windows App SDK restart helper (used by the SDK's crash/restart recovery). |

## EF Core + SQLite

| File(s) | What it is |
|---|---|
| `Microsoft.EntityFrameworkCore.dll`, `.Abstractions.dll`, `.Relational.dll`, `.Sqlite.dll` | EF Core 10 and its SQLite provider. |
| `Microsoft.EntityFrameworkCore.Design.dll` | EF **design-time** assembly (referenced for tooling; drags in Roslyn/build libs below). |
| `Microsoft.Data.Sqlite.dll`, `SQLitePCLRaw.batteries_v2.dll`, `.core.dll`, `.provider.e_sqlite3.dll` | ADO.NET SQLite layer + SQLitePCLRaw bindings. |
| `e_sqlite3.dll` | The **native SQLite engine**. |

## Per-process traffic capture (ETW)

| File(s) | What it is |
|---|---|
| `Microsoft.Diagnostics.Tracing.TraceEvent.dll` | TraceEvent library driving the ETW kernel network session. |
| `Microsoft.Diagnostics.FastSerialization.dll`, `Microsoft.Diagnostics.NETCore.Client.dll`, `TraceReloggerLib.dll` | TraceEvent support (serialization, diagnostics client, ETW relogger). |
| `Dia2Lib.dll`, `Microsoft.DiaSymReader.Native.amd64.dll` | Symbol-reading interop used by TraceEvent. |
| `amd64\KernelTraceControl.dll`, `amd64\msdia140.dll` | Native x64 kernel-trace-control and DIA symbol engine that TraceEvent P/Invokes. |

## Reports: charts & PDF

| File(s) | What it is |
|---|---|
| `Microsoft.Graphics.Canvas.dll`, `.Interop.dll` | Win2D — used to rasterise digest charts to PNG. |
| `QuestPDF.dll` | QuestPDF managed library (PDF document builder). |
| `QuestPdfSkia.dll`, `qpdf.dll` | Native Skia renderer / PDF backend behind QuestPDF. |

## Design-time / transitive dependencies (present but not used at runtime)

| File(s) | What it is |
|---|---|
| `Microsoft.CodeAnalysis*.dll`, `Microsoft.Build.Framework.dll`, `Microsoft.VisualStudio.SolutionPersistence.dll`, `Mono.TextTemplating.dll`, `System.Composition.*.dll`, `Humanizer.dll` | Roslyn + MSBuild + templating libraries pulled in by `EntityFrameworkCore.Design` (migration/scaffolding tooling). Not exercised by the running app. |
| `Newtonsoft.Json.dll`, `Microsoft.Extensions.DependencyModel.dll` | Transitive JSON / dependency-model libraries. |
| ~40 × `Microsoft.Extensions.*.dll` | Hosting, DI, configuration, logging and options libraries (some hosting sub-features unused). |

## Windows App SDK 2.2 AI/ML stack (SDK-bundled, unused by this app)

| File(s) | What it is |
|---|---|
| `Microsoft.ML.OnnxRuntime.dll`, `onnxruntime.dll` | ONNX Runtime (managed + native). |
| `DirectML.dll` | DirectML GPU inference. |
| `Microsoft.Windows.AI.*.dll` / `.winmd`, `Microsoft.Graphics.Imaging*.dll`, `NPUDetect.dll`, `PerceptiveStreaming.dll` | Windows AI Foundation / Windows ML + imaging components. |
| `Microsoft.Windows.Workloads.*`, `workloads*.json` (`stx`, `qnn`, `lnl`, `j32`, `365`, …) | Workload resolver + manifests describing AI-accelerator (NPU) workload variants. |
| `Microsoft.Web.WebView2.Core.dll`, `.Core.Projection.dll`, `WebView2Loader.dll` | WebView2 loader/interop shipped by the SDK. |
| `msquic.dll` (+ `System.Net.Quic.dll`) | Native MsQuic (HTTP/3) transport. |

These AI/ML, WebView2 and QUIC pieces are bundled by Windows App SDK 2.2's self-contained runtime; the app does not call them, but a self-contained copy includes them.

## Portable copy (side-loading for testing)

Because the output is self-contained, the whole `win-x64` output folder can be copied to another machine and run directly — no runtime install required. Caveats:

- **Architecture must match** the target machine (this is an **x64** build; use `-p:Platform=ARM64` / `-r win-arm64` for an ARM64 laptop).
- The database and `settings.json` live in `%LOCALAPPDATA%\UmnathaNetworkMonitor\`, **not** in the app folder — a portable copy starts from (or shares) whatever data already exists there, and does **not** carry the dev machine's data across.
- For a leaner folder than the raw `bin`, publish instead: `dotnet publish NetworkMonitor\NetworkMonitor.csproj -c Release -r win-x64` and copy the `publish\` output.
