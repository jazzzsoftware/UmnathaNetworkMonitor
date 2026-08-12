# Chart Colour Schemes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user pick the chart palette from five presets or a custom five-colour set, applied immediately to every in-app chart, grid and widget, on both the dark and light card surfaces.

**Architecture:** Pure colour types and an OKLCH derivation rule live in `NetworkMonitor.Core/Charting/`. A `ChartPaletteService` singleton in Services caches the derived `Windows.UI.Color` per role and raises `PaletteChanged`. The App owns five `SolidColorBrush` resources whose `.Color` is mutated on that event, so every XAML surface repaints for free; only the Win2D chart and the speed-test ViewModel need explicit handling.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), Win2D (`Microsoft.Graphics.Canvas`), CommunityToolkit.Mvvm, xunit.

**Spec:** `Documents/superpowers/specs/2026-08-12-chart-colour-schemes-design.md`

## Global Constraints

- **No `var`.** Always explicit types.
- **No single-character variable names**, including pattern-match variables and lambda parameters.
- **Always curly braces** on `if`, `else`, `for`, `foreach`, `while`, `using` — even single-line bodies.
- **Blank lines around all blocks**, at every nesting level, including immediately after a method's opening `{` when the first statement is a block and immediately before its closing `}` when the last statement ends with `}`.
- **Single exit point** — exactly one `return` per method, at the end. `break` and `continue` are unaffected.
- **Returns stand alone** — assign to a local first, then `return` that local. Blank line above the `return`.
- **One type per file**, named exactly after the type.
- **Class member order** — Fields → Constructor → Properties → Public methods → Override methods → Private methods. A property's backing field goes immediately above that property in the Properties section, not with the other fields.
- **Hand-write observable properties** with `SetProperty(ref _field, value)`. Never `[ObservableProperty]`.
- **Property braces** — `{`, `get;`, `set;` each on their own line. Expression-bodied (`=>`) properties are exempt.
- **`string.Empty`**, not `""`.
- **No underscores in identifiers** except the leading underscore on private fields.
- **No comments unless the WHY is non-obvious.** No trailing summary comments after methods.
- **XAML formatting** — element name on its own line; every attribute on its own line indented 4 spaces from the opening `<`; blank line above and below every element; attribute order is simple assignments, then event handlers and `Command` bindings, then value-assignment bindings. `AllDevicesPage.xaml` is the canonical reference.
- **Platform x64.** WinUI 3 does not support Any CPU.
- **DB impact: none.** No entity, `DbSet`, column or index changes anywhere in this plan, and therefore no EF migration. Every persisted value added here goes to `settings.json`.
- **Test project references** Models and Core only, via `ProjectReference`. Do not add a reference to Services or the App — Services code in this plan is verified by build and manual test, not unit test.
- Build with `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`; test with `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`.

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `NetworkMonitor.Core/Charting/ChartRole.cs` | The five palette roles |
| `NetworkMonitor.Core/Charting/ChartSurface.cs` | Dark / Light target surface |
| `NetworkMonitor.Core/Charting/Oklch.cs` | An L/C/H triple |
| `NetworkMonitor.Core/Charting/OklchColour.cs` | sRGB ↔ OKLab ↔ OKLCH conversion, gamut reduction, WCAG contrast |
| `NetworkMonitor.Core/Charting/PaletteVariant.cs` | Base hex + surface → display hex |
| `NetworkMonitor.Core/Charting/ChartPalette.cs` | One base hex per role |
| `NetworkMonitor.Core/Charting/ChartSchemePreset.cs` | Id + display name + palette |
| `NetworkMonitor.Core/Charting/ChartSchemeCatalog.cs` | The five presets, lookup, fallback |
| `NetworkMonitor.Services/Charting/ChartPaletteService.cs` | Resolved colours + `PaletteChanged` |
| `NetworkMonitor/Charting/ChartBrushes.cs` | Mutates the App's five brush resources |
| `NetworkMonitor.Tests/OklchColourTests.cs` | Conversion and contrast |
| `NetworkMonitor.Tests/PaletteVariantTests.cs` | Derivation rule |
| `NetworkMonitor.Tests/ChartSchemeCatalogTests.cs` | Presets pass their gates; fallback |

**Modified**

| File | Change |
|---|---|
| `NetworkMonitor.Services/Data/Settings.cs` | Six new properties |
| `NetworkMonitor/App.xaml` | Five `SolidColorBrush` resources |
| `NetworkMonitor/App.xaml.cs` | DI registration, brush attach, theme hook |
| `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs` | Instance colours from the service, rebuild on change |
| `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml` | 2 literals → resources |
| `NetworkMonitor/Views/InternetPage.xaml` | 4 literals → resources |
| `NetworkMonitor/Views/LocalPage.xaml` | 6 literals → resources |
| `NetworkMonitor/Views/SpeedTestPage.xaml` | 8 literals → resources |
| `NetworkMonitor/ViewModels/SpeedTestViewModel.cs` | Hexes from the service, rebuild on change |
| `NetworkMonitor/Views/SettingsPage.xaml` | Theme tab + Chart colours card |
| `NetworkMonitor/Views/SettingsPage.xaml.cs` | Theme panel visibility |
| `NetworkMonitor/ViewModels/SettingsViewModel.cs` | Scheme index, custom colours, reset |
| `NetworkMonitor.slnx` | Register this plan |

**Two corrections already applied to the spec:** it originally said "twelve XAML literals" — the real count is **20** (`InternetPage` 4, `LocalPage` 6, `SpeedTestPage` 8, `TrafficAreaChart` 2), and Task 6 lists every line. It also listed `OklchColour` alone; that type needs a companion `Oklch` record to carry L/C/H, so Core gains eight types rather than seven. Both were fixed in the spec when this plan was written; Task 10 Step 1 just confirms them.

---

### Task 1: OKLCH colour maths

The derivation rule needs a perceptual colour space. OKLab is one where equal numeric distance means roughly equal perceived difference; OKLCH is the same space in polar form (Lightness, Chroma, Hue), which is what lets us move lightness while holding the hue recognisable. Nothing in the codebase does this yet.

**Files:**
- Create: `NetworkMonitor.Core/Charting/Oklch.cs`
- Create: `NetworkMonitor.Core/Charting/OklchColour.cs`
- Test: `NetworkMonitor.Tests/OklchColourTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Oklch(double Lightness, double Chroma, double Hue)`; `OklchColour.ToOklch(string hex) → Oklch`; `OklchColour.ToHex(Oklch value) → string` (uppercase `#RRGGBB`, chroma-reduced into sRGB gamut); `OklchColour.Contrast(string oneHex, string otherHex) → double` (WCAG ratio, 1.0–21.0).

- [ ] **Step 1: Write the failing tests**

Create `NetworkMonitor.Tests/OklchColourTests.cs`:

```csharp
using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class OklchColourTests
    {
        [Theory]
        [InlineData("#1976D2")]
        [InlineData("#AB47BC")]
        [InlineData("#F57C00")]
        [InlineData("#2E7D32")]
        [InlineData("#FFFFFF")]
        [InlineData("#000000")]
        [InlineData("#808080")]
        public void HexRoundTripsThroughOklch(string hex)
        {
            Oklch value = OklchColour.ToOklch(hex);
            string result = OklchColour.ToHex(value);

            Assert.Equal(hex.ToUpperInvariant(), result);
        }

        [Fact]
        public void ContrastOfBlackOnWhiteIsTwentyOne()
        {
            double result = OklchColour.Contrast("#000000", "#FFFFFF");

            Assert.Equal(21.0, result, 1);
        }

        [Fact]
        public void ContrastIsSymmetric()
        {
            double forward = OklchColour.Contrast("#1976D2", "#2D2D2D");
            double backward = OklchColour.Contrast("#2D2D2D", "#1976D2");

            Assert.Equal(forward, backward, 6);
        }

        [Fact]
        public void ContrastOfAColourWithItselfIsOne()
        {
            double result = OklchColour.Contrast("#EDA100", "#EDA100");

            Assert.Equal(1.0, result, 6);
        }

        [Fact]
        public void LightnessIsOrderedFromBlackToWhite()
        {
            double black = OklchColour.ToOklch("#000000").Lightness;
            double mid = OklchColour.ToOklch("#808080").Lightness;
            double white = OklchColour.ToOklch("#FFFFFF").Lightness;

            Assert.True(black < mid);
            Assert.True(mid < white);
        }

        [Fact]
        public void GreyHasEssentiallyNoChroma()
        {
            double chroma = OklchColour.ToOklch("#808080").Chroma;

            Assert.True(chroma < 0.01);
        }

        [Fact]
        public void AnOutOfGamutChromaIsReducedToARenderableColour()
        {
            Oklch source = OklchColour.ToOklch("#1976D2");
            Oklch exaggerated = new Oklch(source.Lightness, 0.9, source.Hue);

            string result = OklchColour.ToHex(exaggerated);

            Assert.Equal(7, result.Length);
            Assert.StartsWith("#", result);
        }

        [Fact]
        public void ParsingAcceptsAHexWithoutTheHash()
        {
            Oklch withHash = OklchColour.ToOklch("#1976D2");
            Oklch withoutHash = OklchColour.ToOklch("1976D2");

            Assert.Equal(withHash.Lightness, withoutHash.Lightness, 9);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter OklchColourTests`
Expected: FAIL — compile error, `Oklch` and `OklchColour` do not exist.

- [ ] **Step 3: Write the record**

Create `NetworkMonitor.Core/Charting/Oklch.cs`:

```csharp
namespace NetworkMonitor.Core.Charting
{
    public record Oklch(double Lightness, double Chroma, double Hue);
}
```

- [ ] **Step 4: Write the converter**

Create `NetworkMonitor.Core/Charting/OklchColour.cs`:

```csharp
using System;
using System.Globalization;

namespace NetworkMonitor.Core.Charting
{
    public static class OklchColour
    {
        private const double ChromaReductionStep = 0.005;
        private const double GamutTolerance = 0.0005;

        public static Oklch ToOklch(string hex)
        {
            (double red, double green, double blue) = ToLinearRgb(hex);

            double longCone = Math.Cbrt(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue);
            double mediumCone = Math.Cbrt(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue);
            double shortCone = Math.Cbrt(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue);

            double lightness = 0.2104542553 * longCone + 0.7936177850 * mediumCone - 0.0040720468 * shortCone;
            double aAxis = 1.9779984951 * longCone - 2.4285922050 * mediumCone + 0.4505937099 * shortCone;
            double bAxis = 0.0259040371 * longCone + 0.7827717662 * mediumCone - 0.8086757660 * shortCone;

            Oklch result = new Oklch(lightness, Math.Sqrt(aAxis * aAxis + bAxis * bAxis), Math.Atan2(bAxis, aAxis));

            return result;
        }

        public static string ToHex(Oklch value)
        {
            double chroma = Math.Max(0.0, value.Chroma);

            while (chroma > 0.0 && !IsInGamut(value.Lightness, chroma, value.Hue))
            {
                chroma = Math.Max(0.0, chroma - ChromaReductionStep);
            }

            (double red, double green, double blue) = ToLinearRgb(value.Lightness, chroma, value.Hue);
            string result = FromLinearRgb(red, green, blue);

            return result;
        }

        public static double Contrast(string oneHex, string otherHex)
        {
            double first = RelativeLuminance(oneHex);
            double second = RelativeLuminance(otherHex);
            double lighter = Math.Max(first, second);
            double darker = Math.Min(first, second);
            double result = (lighter + 0.05) / (darker + 0.05);

            return result;
        }

        private static (double Red, double Green, double Blue) ToLinearRgb(string hex)
        {
            string clean = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;

            double red = ToLinearChannel(byte.Parse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            double green = ToLinearChannel(byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            double blue = ToLinearChannel(byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);

            return (red, green, blue);
        }

        private static (double Red, double Green, double Blue) ToLinearRgb(double lightness, double chroma, double hue)
        {
            double aAxis = Math.Cos(hue) * chroma;
            double bAxis = Math.Sin(hue) * chroma;

            double longCone = Math.Pow(lightness + 0.3963377774 * aAxis + 0.2158037573 * bAxis, 3.0);
            double mediumCone = Math.Pow(lightness - 0.1055613458 * aAxis - 0.0638541728 * bAxis, 3.0);
            double shortCone = Math.Pow(lightness - 0.0894841775 * aAxis - 1.2914855480 * bAxis, 3.0);

            double red = 4.0767416621 * longCone - 3.3077115913 * mediumCone + 0.2309699292 * shortCone;
            double green = -1.2684380046 * longCone + 2.6097574011 * mediumCone - 0.3413193965 * shortCone;
            double blue = -0.0041960863 * longCone - 0.7034186147 * mediumCone + 1.7076147010 * shortCone;

            return (red, green, blue);
        }

        private static bool IsInGamut(double lightness, double chroma, double hue)
        {
            (double red, double green, double blue) = ToLinearRgb(lightness, chroma, hue);
            bool result = IsChannelInGamut(red) && IsChannelInGamut(green) && IsChannelInGamut(blue);

            return result;
        }

        private static bool IsChannelInGamut(double channel)
        {
            bool result = channel >= -GamutTolerance && channel <= 1.0 + GamutTolerance;

            return result;
        }

        private static string FromLinearRgb(double red, double green, double blue)
        {
            int redByte = ToByte(red);
            int greenByte = ToByte(green);
            int blueByte = ToByte(blue);
            string result = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", redByte, greenByte, blueByte);

            return result;
        }

        private static int ToByte(double linearChannel)
        {
            double gamma = ToGammaChannel(linearChannel);
            int result = (int)Math.Round(Math.Clamp(gamma, 0.0, 1.0) * 255.0);

            return result;
        }

        private static double ToLinearChannel(double channel)
        {
            double result = channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

            return result;
        }

        private static double ToGammaChannel(double channel)
        {
            double clamped = Math.Clamp(channel, 0.0, 1.0);
            double result = clamped <= 0.0031308 ? clamped * 12.92 : 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055;

            return result;
        }

        private static double RelativeLuminance(string hex)
        {
            (double red, double green, double blue) = ToLinearRgb(hex);
            double result = 0.2126 * red + 0.7152 * green + 0.0722 * blue;

            return result;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter OklchColourTests`
Expected: PASS, 14 tests (the round-trip theory contributes 7 of them).

If `HexRoundTripsThroughOklch` fails by one byte on a channel, the cause is rounding in `ToByte`, not the matrices — check that `Math.Round` is applied after clamping, not before.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor.Core/Charting/Oklch.cs NetworkMonitor.Core/Charting/OklchColour.cs NetworkMonitor.Tests/OklchColourTests.cs
git commit -m "Add OKLCH colour conversion for the chart palette."
```

---

### Task 2: The derivation rule

One authored base colour per role has to work on two very different card surfaces. It cannot be used raw: `#EDA100` amber sits outside the dark-mode lightness band at L 0.764, and on the light card it falls to 2.09:1 contrast. This task encodes the fix.

**Files:**
- Create: `NetworkMonitor.Core/Charting/ChartRole.cs`
- Create: `NetworkMonitor.Core/Charting/ChartSurface.cs`
- Create: `NetworkMonitor.Core/Charting/PaletteVariant.cs`
- Test: `NetworkMonitor.Tests/PaletteVariantTests.cs`

**Interfaces:**
- Consumes: `Oklch`, `OklchColour` from Task 1.
- Produces: `enum ChartRole { Download, Upload, Latency, Jitter, Selection }`; `enum ChartSurface { Dark, Light }`; `PaletteVariant.Derive(string baseHex, ChartSurface surface) → string`; the constants `PaletteVariant.DarkSurfaceHex` (`"#2D2D2D"`), `PaletteVariant.LightSurfaceHex` (`"#FBFBFB"`), `PaletteVariant.MinimumContrast` (`3.0`), and `PaletteVariant.SurfaceHex(ChartSurface surface) → string`.

- [ ] **Step 1: Write the failing tests**

Create `NetworkMonitor.Tests/PaletteVariantTests.cs`:

```csharp
using System;
using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class PaletteVariantTests
    {
        [Theory]
        [InlineData("#1976D2", ChartSurface.Dark)]
        [InlineData("#1976D2", ChartSurface.Light)]
        [InlineData("#EDA100", ChartSurface.Dark)]
        [InlineData("#EDA100", ChartSurface.Light)]
        [InlineData("#1C5FA8", ChartSurface.Dark)]
        [InlineData("#000000", ChartSurface.Dark)]
        [InlineData("#FFFFFF", ChartSurface.Light)]
        public void DerivedColourClearsMinimumContrastAgainstItsSurface(string baseHex, ChartSurface surface)
        {
            string derived = PaletteVariant.Derive(baseHex, surface);
            double contrast = OklchColour.Contrast(derived, PaletteVariant.SurfaceHex(surface));

            Assert.True(
                contrast >= PaletteVariant.MinimumContrast,
                $"{baseHex} on {surface} derived to {derived} at {contrast:F2}:1");
        }

        [Theory]
        [InlineData("#1976D2", ChartSurface.Dark, 0.48, 0.67)]
        [InlineData("#1976D2", ChartSurface.Light, 0.43, 0.77)]
        [InlineData("#EDA100", ChartSurface.Dark, 0.48, 0.67)]
        [InlineData("#EDA100", ChartSurface.Light, 0.43, 0.77)]
        public void DerivedLightnessLandsInsideTheSurfaceBand(string baseHex, ChartSurface surface, double minimum, double maximum)
        {
            string derived = PaletteVariant.Derive(baseHex, surface);
            double lightness = OklchColour.ToOklch(derived).Lightness;

            Assert.True(lightness >= minimum - 0.01, $"{derived} L={lightness:F3} below {minimum}");
            Assert.True(lightness <= maximum + 0.01, $"{derived} L={lightness:F3} above {maximum}");
        }

        [Theory]
        [InlineData("#1976D2")]
        [InlineData("#AB47BC")]
        [InlineData("#EB6834")]
        [InlineData("#1BAF7A")]
        public void DerivationHoldsTheHue(string baseHex)
        {
            double sourceHue = OklchColour.ToOklch(baseHex).Hue;
            double darkHue = OklchColour.ToOklch(PaletteVariant.Derive(baseHex, ChartSurface.Dark)).Hue;
            double lightHue = OklchColour.ToOklch(PaletteVariant.Derive(baseHex, ChartSurface.Light)).Hue;

            Assert.True(Math.Abs(sourceHue - darkHue) < 0.05, $"dark hue drifted from {sourceHue:F3} to {darkHue:F3}");
            Assert.True(Math.Abs(sourceHue - lightHue) < 0.05, $"light hue drifted from {sourceHue:F3} to {lightHue:F3}");
        }

        [Fact]
        public void AmberIsDarkenedForBothSurfaces()
        {
            double source = OklchColour.ToOklch("#EDA100").Lightness;
            double onLight = OklchColour.ToOklch(PaletteVariant.Derive("#EDA100", ChartSurface.Light)).Lightness;
            double onDark = OklchColour.ToOklch(PaletteVariant.Derive("#EDA100", ChartSurface.Dark)).Lightness;

            Assert.True(onLight < source);
            Assert.True(onDark < source);
        }

        [Fact]
        public void DerivationIsDeterministic()
        {
            string first = PaletteVariant.Derive("#7C5CDB", ChartSurface.Dark);
            string second = PaletteVariant.Derive("#7C5CDB", ChartSurface.Dark);

            Assert.Equal(first, second);
        }

        [Fact]
        public void DerivedValueIsAlwaysAParseableSixDigitHex()
        {
            string derived = PaletteVariant.Derive("#E34948", ChartSurface.Light);

            Assert.Equal(7, derived.Length);
            Assert.StartsWith("#", derived);
            Assert.Equal(derived, OklchColour.ToHex(OklchColour.ToOklch(derived)));
        }

        [Fact]
        public void SurfaceHexMatchesTheDocumentedCardColours()
        {
            Assert.Equal("#2D2D2D", PaletteVariant.SurfaceHex(ChartSurface.Dark));
            Assert.Equal("#FBFBFB", PaletteVariant.SurfaceHex(ChartSurface.Light));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter PaletteVariantTests`
Expected: FAIL — compile error, `ChartSurface` and `PaletteVariant` do not exist.

- [ ] **Step 3: Write the enums**

Create `NetworkMonitor.Core/Charting/ChartRole.cs`:

```csharp
namespace NetworkMonitor.Core.Charting
{
    public enum ChartRole
    {
        Download,
        Upload,
        Latency,
        Jitter,
        Selection
    }
}
```

Create `NetworkMonitor.Core/Charting/ChartSurface.cs`:

```csharp
namespace NetworkMonitor.Core.Charting
{
    public enum ChartSurface
    {
        Dark,
        Light
    }
}
```

- [ ] **Step 4: Write the derivation**

Create `NetworkMonitor.Core/Charting/PaletteVariant.cs`:

```csharp
using System;

namespace NetworkMonitor.Core.Charting
{
    public static class PaletteVariant
    {
        public const string DarkSurfaceHex = "#2D2D2D";
        public const string LightSurfaceHex = "#FBFBFB";
        public const double MinimumContrast = 3.0;

        private const double DarkBandMinimum = 0.48;
        private const double DarkBandMaximum = 0.67;
        private const double LightBandMinimum = 0.43;
        private const double LightBandMaximum = 0.77;
        private const double LightnessStep = 0.02;

        public static string SurfaceHex(ChartSurface surface)
        {
            string result = surface == ChartSurface.Dark ? DarkSurfaceHex : LightSurfaceHex;

            return result;
        }

        public static string Derive(string baseHex, ChartSurface surface)
        {
            Oklch source = OklchColour.ToOklch(baseHex);
            double minimum = surface == ChartSurface.Dark ? DarkBandMinimum : LightBandMinimum;
            double maximum = surface == ChartSurface.Dark ? DarkBandMaximum : LightBandMaximum;
            double step = surface == ChartSurface.Dark ? LightnessStep : -LightnessStep;
            string surfaceHex = SurfaceHex(surface);

            double lightness = Math.Clamp(source.Lightness, minimum, maximum);
            string candidate = OklchColour.ToHex(new Oklch(lightness, source.Chroma, source.Hue));

            while (OklchColour.Contrast(candidate, surfaceHex) < MinimumContrast)
            {
                double next = lightness + step;

                if (next < minimum || next > maximum)
                {
                    break;
                }

                lightness = next;
                candidate = OklchColour.ToHex(new Oklch(lightness, source.Chroma, source.Hue));
            }

            return candidate;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter PaletteVariantTests`
Expected: PASS, 19 tests (15 theory cases plus 4 facts).

If `DerivedColourClearsMinimumContrastAgainstItsSurface` fails for `#1976D2` on Dark, that is real and expected to be handled by the loop — the raw colour is 2.99:1. A failure means the loop direction is wrong: on a dark surface lightness must **increase**, on light it must **decrease**.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor.Core/Charting/ChartRole.cs NetworkMonitor.Core/Charting/ChartSurface.cs NetworkMonitor.Core/Charting/PaletteVariant.cs NetworkMonitor.Tests/PaletteVariantTests.cs
git commit -m "Derive a chart colour per surface from one base hex."
```

---

### Task 3: The preset catalogue

**Files:**
- Create: `NetworkMonitor.Core/Charting/ChartPalette.cs`
- Create: `NetworkMonitor.Core/Charting/ChartSchemePreset.cs`
- Create: `NetworkMonitor.Core/Charting/ChartSchemeCatalog.cs`
- Test: `NetworkMonitor.Tests/ChartSchemeCatalogTests.cs`

**Interfaces:**
- Consumes: `ChartRole`, `ChartSurface`, `PaletteVariant`, `OklchColour`.
- Produces: `ChartPalette(string Download, string Upload, string Latency, string Jitter, string Selection)` with `ForRole(ChartRole role) → string` and `WithRole(ChartRole role, string hex) → ChartPalette`; `ChartSchemePreset(string Id, string DisplayName, ChartPalette Palette)`; `ChartSchemeCatalog.Presets → IReadOnlyList<ChartSchemePreset>`, `ChartSchemeCatalog.Resolve(string? schemeId) → ChartSchemePreset`, `ChartSchemeCatalog.DefaultSchemeId` (`"classic"`), `ChartSchemeCatalog.CustomSchemeId` (`"custom"`).

- [ ] **Step 1: Write the failing tests**

The contrast sweep is the point of this task: it is what stops a preset shipping unreadable on one of the two surfaces.

Create `NetworkMonitor.Tests/ChartSchemeCatalogTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class ChartSchemeCatalogTests
    {
        public static IEnumerable<object[]> EveryPresetRoleAndSurface()
        {

            foreach (ChartSchemePreset preset in ChartSchemeCatalog.Presets)
            {

                foreach (ChartRole role in Enum.GetValues<ChartRole>())
                {
                    yield return new object[] { preset.Id, role, ChartSurface.Dark };
                    yield return new object[] { preset.Id, role, ChartSurface.Light };
                }

            }

        }

        [Theory]
        [MemberData(nameof(EveryPresetRoleAndSurface))]
        public void EveryPresetColourIsReadableOnBothSurfaces(string presetId, ChartRole role, ChartSurface surface)
        {
            ChartSchemePreset preset = ChartSchemeCatalog.Resolve(presetId);
            string derived = PaletteVariant.Derive(preset.Palette.ForRole(role), surface);
            double contrast = OklchColour.Contrast(derived, PaletteVariant.SurfaceHex(surface));

            Assert.True(
                contrast >= PaletteVariant.MinimumContrast,
                $"{presetId}/{role} on {surface} derived to {derived} at only {contrast:F2}:1");
        }

        [Fact]
        public void ThereAreFivePresetsInTheDocumentedOrder()
        {
            string[] result = ChartSchemeCatalog.Presets.Select(preset => preset.Id).ToArray();

            Assert.Equal(new[] { "classic", "contrast", "aurora", "ember", "ocean" }, result);
        }

        [Fact]
        public void ClassicShipsTodaysPaletteUnchanged()
        {
            ChartPalette result = ChartSchemeCatalog.Resolve("classic").Palette;

            Assert.Equal("#1976D2", result.Download);
            Assert.Equal("#AB47BC", result.Upload);
            Assert.Equal("#F57C00", result.Latency);
            Assert.Equal("#2E7D32", result.Jitter);
            Assert.Equal("#F57C00", result.Selection);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nonsense")]
        [InlineData("custom")]
        public void AnUnknownSchemeIdFallsBackToClassic(string? schemeId)
        {
            ChartSchemePreset result = ChartSchemeCatalog.Resolve(schemeId);

            Assert.Equal("classic", result.Id);
        }

        [Fact]
        public void SchemeIdLookupIsCaseInsensitive()
        {
            ChartSchemePreset result = ChartSchemeCatalog.Resolve("AURORA");

            Assert.Equal("aurora", result.Id);
        }

        [Fact]
        public void SelectionNeverReusesTheDownloadOrUploadHueInAnyPreset()
        {

            foreach (ChartSchemePreset preset in ChartSchemeCatalog.Presets)
            {
                Assert.NotEqual(preset.Palette.Download, preset.Palette.Selection);
                Assert.NotEqual(preset.Palette.Upload, preset.Palette.Selection);
            }

        }

        [Fact]
        public void ForRoleReturnsTheMatchingSlot()
        {
            ChartPalette palette = new ChartPalette("#111111", "#222222", "#333333", "#444444", "#555555");

            Assert.Equal("#111111", palette.ForRole(ChartRole.Download));
            Assert.Equal("#222222", palette.ForRole(ChartRole.Upload));
            Assert.Equal("#333333", palette.ForRole(ChartRole.Latency));
            Assert.Equal("#444444", palette.ForRole(ChartRole.Jitter));
            Assert.Equal("#555555", palette.ForRole(ChartRole.Selection));
        }

        [Fact]
        public void WithRoleReplacesOnlyTheNamedSlot()
        {
            ChartPalette palette = new ChartPalette("#111111", "#222222", "#333333", "#444444", "#555555");

            ChartPalette result = palette.WithRole(ChartRole.Latency, "#ABCDEF");

            Assert.Equal("#ABCDEF", result.Latency);
            Assert.Equal("#111111", result.Download);
            Assert.Equal("#222222", result.Upload);
            Assert.Equal("#444444", result.Jitter);
            Assert.Equal("#555555", result.Selection);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChartSchemeCatalogTests`
Expected: FAIL — compile error, `ChartPalette` and `ChartSchemeCatalog` do not exist.

- [ ] **Step 3: Write the palette record**

Create `NetworkMonitor.Core/Charting/ChartPalette.cs`:

```csharp
using System;

namespace NetworkMonitor.Core.Charting
{
    public record ChartPalette(
        string Download,
        string Upload,
        string Latency,
        string Jitter,
        string Selection)
    {
        public string ForRole(ChartRole role)
        {
            string result = role switch
            {
                ChartRole.Download => Download,
                ChartRole.Upload => Upload,
                ChartRole.Latency => Latency,
                ChartRole.Jitter => Jitter,
                ChartRole.Selection => Selection,
                _ => Download
            };

            return result;
        }

        public ChartPalette WithRole(ChartRole role, string hex)
        {
            ChartPalette result = role switch
            {
                ChartRole.Download => this with { Download = hex },
                ChartRole.Upload => this with { Upload = hex },
                ChartRole.Latency => this with { Latency = hex },
                ChartRole.Jitter => this with { Jitter = hex },
                ChartRole.Selection => this with { Selection = hex },
                _ => this
            };

            return result;
        }
    }
}
```

- [ ] **Step 4: Write the preset record and catalogue**

Create `NetworkMonitor.Core/Charting/ChartSchemePreset.cs`:

```csharp
namespace NetworkMonitor.Core.Charting
{
    public record ChartSchemePreset(string Id, string DisplayName, ChartPalette Palette);
}
```

Create `NetworkMonitor.Core/Charting/ChartSchemeCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkMonitor.Core.Charting
{
    public static class ChartSchemeCatalog
    {
        public const string DefaultSchemeId = "classic";
        public const string CustomSchemeId = "custom";

        private static readonly IReadOnlyList<ChartSchemePreset> _presets = new List<ChartSchemePreset>
        {
            new ChartSchemePreset(
                "classic",
                "Classic",
                new ChartPalette("#1976D2", "#AB47BC", "#F57C00", "#2E7D32", "#F57C00")),
            new ChartSchemePreset(
                "contrast",
                "Contrast",
                new ChartPalette("#2A78D6", "#EB6834", "#EDA100", "#1BAF7A", "#E87BA4")),
            new ChartSchemePreset(
                "aurora",
                "Aurora",
                new ChartPalette("#1BAF7A", "#7C5CDB", "#EDA100", "#2A78D6", "#EB6834")),
            new ChartSchemePreset(
                "ember",
                "Ember",
                new ChartPalette("#E34948", "#EDA100", "#7C5CDB", "#1BAF7A", "#2A78D6")),
            new ChartSchemePreset(
                "ocean",
                "Ocean",
                new ChartPalette("#6EA8E8", "#1C5FA8", "#EDA100", "#1BAF7A", "#EB6834"))
        };

        public static IReadOnlyList<ChartSchemePreset> Presets => _presets;

        public static ChartSchemePreset Resolve(string? schemeId)
        {
            ChartSchemePreset? match = null;

            if (!string.IsNullOrWhiteSpace(schemeId))
            {
                match = _presets.FirstOrDefault(preset =>
                    string.Equals(preset.Id, schemeId.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            ChartSchemePreset result = match ?? _presets[0];

            return result;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChartSchemeCatalogTests`
Expected: PASS, 61 tests (the sweep contributes 50: 5 presets × 5 roles × 2 surfaces).

If a specific `preset/role/surface` case fails the sweep, the base hex is unusable on that surface even after derivation — change that base hex in the catalogue rather than weakening the assertion. That is exactly what this test exists to catch.

- [ ] **Step 6: Run the whole suite and commit**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`
Expected: PASS, no regressions in the existing tests.

```bash
git add NetworkMonitor.Core/Charting/ChartPalette.cs NetworkMonitor.Core/Charting/ChartSchemePreset.cs NetworkMonitor.Core/Charting/ChartSchemeCatalog.cs NetworkMonitor.Tests/ChartSchemeCatalogTests.cs
git commit -m "Add the five chart colour presets with a readability sweep."
```

---

### Task 4: Settings and the palette service

**Files:**
- Modify: `NetworkMonitor.Services/Data/Settings.cs` (add after `ChartSmoothScrolling`, which ends at line 141)
- Create: `NetworkMonitor.Services/Charting/ChartPaletteService.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3; `Settings` and its existing `Save()` method.
- Produces: `Settings.ChartSchemeId`, `Settings.ChartCustomDownload`, `ChartCustomUpload`, `ChartCustomLatency`, `ChartCustomJitter`, `ChartCustomSelection` (all `string`); `ChartPaletteService` with `Resolve(ChartRole) → Color`, `ResolveHex(ChartRole) → string`, `SchemeId → string`, `IsCustom → bool`, `CurrentBasePalette() → ChartPalette`, `SetSurface(ChartSurface)`, `ApplyScheme(string schemeId)`, `ApplyCustomColour(ChartRole role, string baseHex)`, `ResetToDefault()`, and `event EventHandler? PaletteChanged`.

- [ ] **Step 1: Add the Settings properties**

In `NetworkMonitor.Services/Data/Settings.cs`, insert after the `ChartSmoothScrolling` property (which closes at line 141) and before `public int WindowX`:

```csharp
        public string ChartSchemeId
        {
            get;
            set;
        } = ChartSchemeCatalog.DefaultSchemeId;

        public string ChartCustomDownload
        {
            get;
            set;
        } = "#1976D2";

        public string ChartCustomUpload
        {
            get;
            set;
        } = "#AB47BC";

        public string ChartCustomLatency
        {
            get;
            set;
        } = "#F57C00";

        public string ChartCustomJitter
        {
            get;
            set;
        } = "#2E7D32";

        public string ChartCustomSelection
        {
            get;
            set;
        } = "#F57C00";
```

Add `using NetworkMonitor.Core.Charting;` to the file's using block (it already has `using NetworkMonitor.Core.Traffic;` at line 9, so Core is referenced).

The custom defaults are Classic's values, so a user who selects Custom without touching a picker sees no change.

- [ ] **Step 2: Write the service**

Create `NetworkMonitor.Services/Charting/ChartPaletteService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Data;
using Windows.UI;

namespace NetworkMonitor.Services.Charting
{
    public class ChartPaletteService
    {
        private readonly Settings _settings;
        private readonly Dictionary<ChartRole, Color> _colours = new Dictionary<ChartRole, Color>();
        private readonly Dictionary<ChartRole, string> _hexes = new Dictionary<ChartRole, string>();
        private ChartSurface _surface = ChartSurface.Dark;

        public event EventHandler? PaletteChanged;

        public ChartPaletteService(Settings settings)
        {
            _settings = settings;
            Recompute();
        }

        public string SchemeId => _settings.ChartSchemeId;

        public bool IsCustom => string.Equals(
            _settings.ChartSchemeId,
            ChartSchemeCatalog.CustomSchemeId,
            StringComparison.OrdinalIgnoreCase);

        public Color Resolve(ChartRole role)
        {
            Color result = _colours[role];

            return result;
        }

        public string ResolveHex(ChartRole role)
        {
            string result = _hexes[role];

            return result;
        }

        public ChartPalette CurrentBasePalette()
        {
            ChartPalette result;

            if (IsCustom)
            {
                result = new ChartPalette(
                    _settings.ChartCustomDownload,
                    _settings.ChartCustomUpload,
                    _settings.ChartCustomLatency,
                    _settings.ChartCustomJitter,
                    _settings.ChartCustomSelection);
            }
            else
            {
                result = ChartSchemeCatalog.Resolve(_settings.ChartSchemeId).Palette;
            }

            return result;
        }

        public void SetSurface(ChartSurface surface)
        {

            if (_surface != surface)
            {
                _surface = surface;
                Recompute();
                PaletteChanged?.Invoke(this, EventArgs.Empty);
            }

        }

        public void ApplyScheme(string schemeId)
        {
            _settings.ChartSchemeId = schemeId;
            _settings.Save();
            Recompute();
            PaletteChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyCustomColour(ChartRole role, string baseHex)
        {

            switch (role)
            {
                case ChartRole.Download:
                    _settings.ChartCustomDownload = baseHex;
                    break;
                case ChartRole.Upload:
                    _settings.ChartCustomUpload = baseHex;
                    break;
                case ChartRole.Latency:
                    _settings.ChartCustomLatency = baseHex;
                    break;
                case ChartRole.Jitter:
                    _settings.ChartCustomJitter = baseHex;
                    break;
                case ChartRole.Selection:
                    _settings.ChartCustomSelection = baseHex;
                    break;
            }

            _settings.Save();
            Recompute();
            PaletteChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetToDefault()
        {
            ApplyScheme(ChartSchemeCatalog.DefaultSchemeId);
        }

        private void Recompute()
        {
            ChartPalette basePalette = CurrentBasePalette();

            foreach (ChartRole role in Enum.GetValues<ChartRole>())
            {
                string derived = PaletteVariant.Derive(basePalette.ForRole(role), _surface);
                _hexes[role] = derived;
                _colours[role] = ToColor(derived);
            }

        }

        private static Color ToColor(string hex)
        {
            string clean = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;
            byte red = byte.Parse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Color result = Color.FromArgb(0xFF, red, green, blue);

            return result;
        }
    }
}
```

`Recompute()` runs the derivation five times, which is why every consumer reads the cache instead of calling `PaletteVariant.Derive` — the Win2D draw path runs once per frame while smooth scrolling is on.

Note the deliberate asymmetry: `ApplyScheme` and `ApplyCustomColour` always raise `PaletteChanged`, but `SetSurface` only raises it when the surface actually changed. Windows raises `ActualThemeChanged` for reasons other than a light/dark flip, and a repaint per spurious event would be wasted work.

- [ ] **Step 3: Build**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds. `ChartPaletteService` is not yet registered or consumed — that is Task 5.

If `Windows.UI.Color` will not resolve, confirm you are editing the Services project (`net10.0-windows`, `UseWinUI`) and not Core.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor.Services/Data/Settings.cs NetworkMonitor.Services/Charting/ChartPaletteService.cs
git commit -m "Hold the chosen chart scheme in settings and resolve it once."
```

---

### Task 5: App brush resources, DI and the theme hook

This is the task that makes "immediate" cheap. WinUI `SolidColorBrush` resources are `DependencyObject`s shared by reference, so mutating `.Color` on one repaints every `{StaticResource}` that points at it — no bindings, no per-page code.

The five brushes go in the **root** `ResourceDictionary`, not in `ThemeDictionaries`. The existing `Digest*` brushes are theme-dictionary entries because XAML owns their light/dark switch; these five are owned by `ChartPaletteService`, which handles the surface itself via `SetSurface`. Putting them in a theme dictionary would have WinUI replace the instance on a theme change and silently discard the mutation.

**Files:**
- Modify: `NetworkMonitor/App.xaml` (root `ResourceDictionary`, after the `ThemeDictionaries` block closing at line 80)
- Create: `NetworkMonitor/Charting/ChartBrushes.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (DI at line 114; startup at line 256)

**Interfaces:**
- Consumes: `ChartPaletteService`, `ChartRole`, `ChartSurface`.
- Produces: the resource keys `ChartDownloadBrush`, `ChartUploadBrush`, `ChartLatencyBrush`, `ChartJitterBrush`, `ChartSelectionBrush`; `ChartBrushes.Attach(ChartPaletteService palette)`; `ChartPaletteService` resolvable from `App.AppHost.Services`.

- [ ] **Step 1: Add the brush resources**

In `NetworkMonitor/App.xaml`, immediately after `</ResourceDictionary.ThemeDictionaries>` (line 80) and before the `<converters:OnlineStatusConverter` entry:

```xml
            <SolidColorBrush
                x:Key="ChartDownloadBrush"
                Color="#FF1976D2" />

            <SolidColorBrush
                x:Key="ChartUploadBrush"
                Color="#FFAB47BC" />

            <SolidColorBrush
                x:Key="ChartLatencyBrush"
                Color="#FFF57C00" />

            <SolidColorBrush
                x:Key="ChartJitterBrush"
                Color="#FF2E7D32" />

            <SolidColorBrush
                x:Key="ChartSelectionBrush"
                Color="#FFF57C00" />
```

The literal colours are Classic and act only as the pre-`Attach` value, so the XAML designer and any frame before startup completes show something sensible.

- [ ] **Step 2: Write the brush bridge**

Create `NetworkMonitor/Charting/ChartBrushes.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Charting;

namespace NetworkMonitor.Charting
{
    public static class ChartBrushes
    {
        private static readonly Dictionary<ChartRole, string> _resourceKeys = new Dictionary<ChartRole, string>
        {
            { ChartRole.Download, "ChartDownloadBrush" },
            { ChartRole.Upload, "ChartUploadBrush" },
            { ChartRole.Latency, "ChartLatencyBrush" },
            { ChartRole.Jitter, "ChartJitterBrush" },
            { ChartRole.Selection, "ChartSelectionBrush" }
        };

        private static ChartPaletteService? _palette;

        public static void Attach(ChartPaletteService palette)
        {

            if (_palette is not null)
            {
                _palette.PaletteChanged -= OnPaletteChanged;
            }

            _palette = palette;
            _palette.PaletteChanged += OnPaletteChanged;
            Apply(palette);
        }

        private static void OnPaletteChanged(object? sender, EventArgs args)
        {

            if (_palette is not null)
            {
                Apply(_palette);
            }

        }

        private static void Apply(ChartPaletteService palette)
        {

            foreach (KeyValuePair<ChartRole, string> entry in _resourceKeys)
            {

                if (Application.Current.Resources.TryGetValue(entry.Value, out object? resource)
                    && resource is SolidColorBrush brush)
                {
                    brush.Color = palette.Resolve(entry.Key);
                }

            }

        }
    }
}
```

- [ ] **Step 3: Register the service**

In `NetworkMonitor/App.xaml.cs`, after `services.AddSingleton<MiniGraphState>();` (line 114):

```csharp
                        services.AddSingleton<ChartPaletteService>();
```

Add `using NetworkMonitor.Services.Charting;` and `using NetworkMonitor.Charting;` to the file's using block.

- [ ] **Step 4: Attach the brushes and hook the theme**

In `NetworkMonitor/App.xaml.cs`, immediately after `MainWindow window = AppHost.Services.GetRequiredService<MainWindow>();` (line 256) and before the `_mainWindowHwnd` assignment:

```csharp
                ChartPaletteService chartPalette = AppHost.Services.GetRequiredService<ChartPaletteService>();

                if (window.Content is FrameworkElement paletteRoot)
                {
                    ChartSurface startingSurface = paletteRoot.ActualTheme == ElementTheme.Light
                        ? ChartSurface.Light
                        : ChartSurface.Dark;
                    chartPalette.SetSurface(startingSurface);

                    paletteRoot.ActualThemeChanged += (FrameworkElement sender, object args) =>
                    {
                        ChartSurface surface = sender.ActualTheme == ElementTheme.Light
                            ? ChartSurface.Light
                            : ChartSurface.Dark;
                        chartPalette.SetSurface(surface);
                    };
                }

                ChartBrushes.Attach(chartPalette);
```

`Attach` runs after `SetSurface` so the first `Apply` already carries the correct surface, and `Attach`'s own subscription then covers every later change.

- [ ] **Step 5: Build and smoke-test**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds.

Launch the app. Expected: identical appearance to before — the derived Classic values on the dark card are a very slight lightening of the raw hexes (the contrast loop lifts `#1976D2` from 2.99:1 to at least 3:1), so the charts should look unchanged to the eye. Nothing is wired to the brushes yet.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/App.xaml NetworkMonitor/Charting/ChartBrushes.cs NetworkMonitor/App.xaml.cs
git commit -m "Publish the chart palette as live brush resources."
```

---

### Task 6: Replace the XAML literals

Twenty sites across four files. Each is a mechanical swap of a hex literal for the matching `{StaticResource}`. Use `StaticResource`, not `ThemeResource` — these brushes live in the root dictionary and the service owns their theme response.

**Files:**
- Modify: `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml` lines 96, 114
- Modify: `NetworkMonitor/Views/InternetPage.xaml` lines 122, 143, 393, 415
- Modify: `NetworkMonitor/Views/LocalPage.xaml` lines 140, 161, 441, 463, 564, 573
- Modify: `NetworkMonitor/Views/SpeedTestPage.xaml` lines 102, 109, 126, 133, 209, 229, 292, 312

**Interfaces:**
- Consumes: the five resource keys from Task 5.
- Produces: nothing new.

- [ ] **Step 1: Confirm the inventory before editing**

Run: `git grep -n -E '"#(1976D2|AB47BC|F57C00|2E7D32)"' -- 'NetworkMonitor/Views'`
Expected: exactly 20 lines, matching the table below. If the count differs, the file has moved on since this plan was written — map the extra sites by role before continuing.

| File | Line | Attribute | Replace with |
|---|---|---|---|
| `Views/Controls/TrafficAreaChart.xaml` | 96 | `Fill="#1976D2"` | `Fill="{StaticResource ChartDownloadBrush}"` |
| `Views/Controls/TrafficAreaChart.xaml` | 114 | `Fill="#AB47BC"` | `Fill="{StaticResource ChartUploadBrush}"` |
| `Views/InternetPage.xaml` | 122 | `Fill="#1976D2"` | `Fill="{StaticResource ChartDownloadBrush}"` |
| `Views/InternetPage.xaml` | 143 | `Fill="#AB47BC"` | `Fill="{StaticResource ChartUploadBrush}"` |
| `Views/InternetPage.xaml` | 393 | `Foreground="#1976D2"` | `Foreground="{StaticResource ChartDownloadBrush}"` |
| `Views/InternetPage.xaml` | 415 | `Foreground="#AB47BC"` | `Foreground="{StaticResource ChartUploadBrush}"` |
| `Views/LocalPage.xaml` | 140 | `Fill="#1976D2"` | `Fill="{StaticResource ChartDownloadBrush}"` |
| `Views/LocalPage.xaml` | 161 | `Fill="#AB47BC"` | `Fill="{StaticResource ChartUploadBrush}"` |
| `Views/LocalPage.xaml` | 441 | `Foreground="#1976D2"` | `Foreground="{StaticResource ChartDownloadBrush}"` |
| `Views/LocalPage.xaml` | 463 | `Foreground="#AB47BC"` | `Foreground="{StaticResource ChartUploadBrush}"` |
| `Views/LocalPage.xaml` | 564 | `Foreground="#1976D2"` | `Foreground="{StaticResource ChartDownloadBrush}"` |
| `Views/LocalPage.xaml` | 573 | `Foreground="#AB47BC"` | `Foreground="{StaticResource ChartUploadBrush}"` |
| `Views/SpeedTestPage.xaml` | 102 | `Foreground="#1976D2"` | `Foreground="{StaticResource ChartDownloadBrush}"` |
| `Views/SpeedTestPage.xaml` | 109 | `Foreground="#1976D2"` | `Foreground="{StaticResource ChartDownloadBrush}"` |
| `Views/SpeedTestPage.xaml` | 126 | `Foreground="#AB47BC"` | `Foreground="{StaticResource ChartUploadBrush}"` |
| `Views/SpeedTestPage.xaml` | 133 | `Foreground="#AB47BC"` | `Foreground="{StaticResource ChartUploadBrush}"` |
| `Views/SpeedTestPage.xaml` | 209 | `Fill="#1976D2"` | `Fill="{StaticResource ChartDownloadBrush}"` |
| `Views/SpeedTestPage.xaml` | 229 | `Fill="#AB47BC"` | `Fill="{StaticResource ChartUploadBrush}"` |
| `Views/SpeedTestPage.xaml` | 292 | `Fill="#F57C00"` | `Fill="{StaticResource ChartLatencyBrush}"` |
| `Views/SpeedTestPage.xaml` | 312 | `Fill="#2E7D32"` | `Fill="{StaticResource ChartJitterBrush}"` |

- [ ] **Step 2: Apply the twenty replacements**

Edit each line in place. `Fill` and `Foreground` are simple assignments, so they keep their existing position in the attribute order — a resource reference is not a value-assignment binding and does not move to the end of the element.

Example, `TrafficAreaChart.xaml` lines 93–99 before:

```xml
                    <Rectangle
                        Width="8"
                        Height="8"
                        Fill="#1976D2"
                        RadiusX="1"
                        RadiusY="1"
                        VerticalAlignment="Center" />
```

after:

```xml
                    <Rectangle
                        Width="8"
                        Height="8"
                        Fill="{StaticResource ChartDownloadBrush}"
                        RadiusX="1"
                        RadiusY="1"
                        VerticalAlignment="Center" />
```

- [ ] **Step 3: Verify no literals remain**

Run: `git grep -n -E '"#(1976D2|AB47BC|F57C00|2E7D32)"' -- 'NetworkMonitor/Views'`
Expected: no output.

- [ ] **Step 4: Build and check every affected surface**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds.

Launch the app and visit Internet, Local and Speed test. Expected: legend swatches, chip swatches and the coloured Download/Upload grid text all render as before. A swatch rendering **transparent** means the resource key is misspelled — WinUI resolves a missing `StaticResource` at parse time and will usually throw, but a typo inside a page loaded lazily surfaces only on navigation, so visit all three pages.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Views/Controls/TrafficAreaChart.xaml NetworkMonitor/Views/InternetPage.xaml NetworkMonitor/Views/LocalPage.xaml NetworkMonitor/Views/SpeedTestPage.xaml
git commit -m "Point every chart swatch at the palette brushes."
```

---

### Task 7: The Win2D traffic chart

`TrafficAreaChart` is the one control behind all four live traffic surfaces — the Internet page, the Local page, and both `MiniTrafficSection` instances in the mini graph. Its colours are `static readonly` and its gradient brushes are built once in `CreateResources`, so both have to become instance state that can be rebuilt.

**Files:**
- Modify: `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs` lines 22–28 (the colour fields), 412–421 (`OnLoaded`), 423–446 (`OnUnloaded`), 458–469 (`ChartCanvasCreateResources`), 519–520 and 559 (draw calls)

**Interfaces:**
- Consumes: `ChartPaletteService`, `ChartRole`; `App.AppHost.Services` as the resolution point, matching how `MiniGraphWindow` and the pages resolve singletons.
- Produces: nothing new.

- [ ] **Step 1: Replace the static colour fields**

In `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs`, delete lines 22–28 and put in their place, in the Fields section:

```csharp
        private readonly ChartPaletteService _palette;

        private Color _downloadStrokeColour;
        private Color _downloadFillTop;
        private Color _downloadFillBottom;
        private Color _uploadStrokeColour;
        private Color _uploadFillTop;
        private Color _uploadFillBottom;
        private Color _selectionLineColour;
        private bool _paletteHooked;
```

Add `using NetworkMonitor.Services.Charting;` to the using block.

- [ ] **Step 2: Resolve the service and seed the colours in the constructor**

The constructor is at line 130. Replace it in full:

```csharp
        public TrafficAreaChart()
        {
            InitializeComponent();

            _palette = App.AppHost.Services.GetRequiredService<ChartPaletteService>();
            ReadPaletteColours();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
```

Add `using Microsoft.Extensions.DependencyInjection;` to the using block.

- [ ] **Step 3: Add the colour reader and the change handler**

Add these to the Private methods section:

```csharp
        private void ReadPaletteColours()
        {
            Color download = _palette.Resolve(ChartRole.Download);
            Color upload = _palette.Resolve(ChartRole.Upload);
            Color selection = _palette.Resolve(ChartRole.Selection);

            _downloadStrokeColour = Color.FromArgb(0xFF, download.R, download.G, download.B);
            _downloadFillTop = Color.FromArgb(0xCC, download.R, download.G, download.B);
            _downloadFillBottom = Color.FromArgb(0x00, download.R, download.G, download.B);
            _uploadStrokeColour = Color.FromArgb(0xFF, upload.R, upload.G, upload.B);
            _uploadFillTop = Color.FromArgb(0xCC, upload.R, upload.G, upload.B);
            _uploadFillBottom = Color.FromArgb(0x00, upload.R, upload.G, upload.B);
            _selectionLineColour = Color.FromArgb(0xCC, selection.R, selection.G, selection.B);
        }

        private void OnPaletteChanged(object? sender, EventArgs args)
        {
            ReadPaletteColours();

            if (ChartCanvas.ReadyToDraw)
            {
                _downloadFill?.Dispose();
                _downloadFill = new CanvasLinearGradientBrush(ChartCanvas, _downloadFillTop, _downloadFillBottom);
                _uploadFill?.Dispose();
                _uploadFill = new CanvasLinearGradientBrush(ChartCanvas, _uploadFillTop, _uploadFillBottom);
            }

            ChartCanvas.Invalidate();
        }
```

The alpha values are the existing ones: `0xFF` stroke, `0xCC` gradient top, `0x00` gradient bottom, `0xCC` selection line. The scheme changes the hue; the fill ramp is unchanged.

The `ReadyToDraw` guard matters. `PaletteChanged` can arrive before `CreateResources` has run or after `OnUnloaded` has called `RemoveFromVisualTree`, and constructing a `CanvasLinearGradientBrush` against a canvas with no device throws.

- [ ] **Step 4: Subscribe and unsubscribe**

In `OnLoaded` (line 412), after the existing `_renderingHooked` block:

```csharp
            if (!_paletteHooked)
            {
                _palette.PaletteChanged += OnPaletteChanged;
                _paletteHooked = true;
            }
```

In `OnUnloaded` (line 423), after the existing `_renderingHooked` block and before the brush disposals:

```csharp
            if (_paletteHooked)
            {
                _palette.PaletteChanged -= OnPaletteChanged;
                _paletteHooked = false;
            }
```

The `_paletteHooked` flag mirrors the existing `_renderingHooked` pattern. Four instances live across two windows and the mini graph is created once then hidden and shown for the life of the session, so a leaked handler would keep a control with a removed canvas subscribed and rooted.

- [ ] **Step 5: Point the gradient construction and draw calls at the fields**

In `ChartCanvasCreateResources` (line 458), replace lines 460–461:

```csharp
            _downloadFill = new CanvasLinearGradientBrush(sender, _downloadFillTop, _downloadFillBottom);
            _uploadFill = new CanvasLinearGradientBrush(sender, _uploadFillTop, _uploadFillBottom);
```

In `ChartCanvasDraw`, replace the two `DrawArea` calls at lines 519–520:

```csharp
                DrawArea(sender, args.DrawingSession, _downloadPointBuffer, plotBottom, _downloadFill, _downloadStrokeColour);
                DrawArea(sender, args.DrawingSession, _uploadPointBuffer, plotBottom, _uploadFill, _uploadStrokeColour);
```

And the selection line at line 559:

```csharp
                        args.DrawingSession.DrawLine(selectionX, 0f, selectionX, (float)height, _selectionLineColour, 1.5f, dashStyle);
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds with no reference to `DownloadStrokeColor`, `UploadStrokeColor` or `SelectionLineColor` remaining.

Run: `git grep -n -E 'DownloadStrokeColor|UploadStrokeColor|SelectionLineColor|DownloadFillTop|UploadFillTop' -- NetworkMonitor`
Expected: no output.

Launch the app. Expected: the Internet and Local charts draw as before, and hovering still shows the dashed selection line.

- [ ] **Step 7: Commit**

```bash
git add NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs
git commit -m "Draw the traffic chart from the palette service."
```

---

### Task 8: The speed test series

`SpeedTrendChart` rebuilds its `Shape` objects from `ChartSeries.ColorHex` on every render, so it needs no colour code at all — only a fresh hex and a re-raise of the bound property.

**Files:**
- Modify: `NetworkMonitor/ViewModels/SpeedTestViewModel.cs` lines 116–126 (the two series lists), plus the constructor and a new handler

**Interfaces:**
- Consumes: `ChartPaletteService`, `ChartRole`.
- Produces: nothing new.

- [ ] **Step 1: Inject the service**

`SpeedTestViewModel` is resolved from DI, so adding a constructor parameter is enough — no registration change. Add the field at line 22, after `_dbFactory`:

```csharp
        private readonly ChartPaletteService _chartPalette;
```

Replace the constructor at lines 25–33:

```csharp
        public SpeedTestViewModel(SpeedTestWorker worker, Settings settings, IDbContextFactory<AppDbContext> dbFactory, ChartPaletteService chartPalette)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _worker = worker;
            _settings = settings;
            _dbFactory = dbFactory;
            _chartPalette = chartPalette;
            RunNowCommand = new AsyncRelayCommand(RunNowAsync);
            _worker.SpeedTestCompleted += OnSpeedTestCompleted;
            _chartPalette.PaletteChanged += OnPaletteChanged;
        }
```

Add `using NetworkMonitor.Core.Charting;` and `using NetworkMonitor.Services.Charting;` to the using block.

- [ ] **Step 2: Retain the plotted values**

A palette change must re-colour without re-querying the database, which means the four `ChartValue` lists have to outlive `LoadAsync`. Add these fields after `_allResults` (line 23):

```csharp
        private IReadOnlyList<ChartValue> _downloadValues = [];
        private IReadOnlyList<ChartValue> _uploadValues = [];
        private IReadOnlyList<ChartValue> _latencyValues = [];
        private IReadOnlyList<ChartValue> _jitterValues = [];
```

- [ ] **Step 3: Take the hexes from the service**

Replace lines 116–126:

```csharp
            _downloadValues = download;
            _uploadValues = upload;
            _latencyValues = latency;
            _jitterValues = jitter;

            RebuildSeries();
```

- [ ] **Step 4: Rebuild on a palette change**

Add to the Private methods section:

```csharp
        private void RebuildSeries()
        {
            ThroughputSeries = new List<ChartSeries>
            {
                new ChartSeries("Download", _chartPalette.ResolveHex(ChartRole.Download), _downloadValues),
                new ChartSeries("Upload", _chartPalette.ResolveHex(ChartRole.Upload), _uploadValues)
            };

            LatencySeries = new List<ChartSeries>
            {
                new ChartSeries("Latency", _chartPalette.ResolveHex(ChartRole.Latency), _latencyValues),
                new ChartSeries("Jitter", _chartPalette.ResolveHex(ChartRole.Jitter), _jitterValues)
            };
        }

        private void OnPaletteChanged(object? sender, EventArgs args)
        {
            RebuildSeries();
        }
```

One builder serves both the initial load and every later re-colour, so the role of each series is fixed in one place rather than inferred from its display name. `ThroughputSeries` and `LatencySeries` are observable properties, so assigning them raises the change notification that makes `SpeedTrendChart` redraw.

`ChartSeries` takes `IReadOnlyList<ChartValue>` for its points, so the fields match its parameter type directly.

- [ ] **Step 5: Build and verify**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds.

Launch the app, open Speed test. Expected: both charts and all four legend swatches render as before. `ThroughputSeries` (line 55) and `LatencySeries` are already `IReadOnlyList<ChartSeries>` properties backed by `SetProperty`, so assigning a `List<ChartSeries>` to them raises the change notification `SpeedTrendChart` needs.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/ViewModels/SpeedTestViewModel.cs
git commit -m "Colour the speed test series from the palette service."
```

---

### Task 9: The Theme tab

**Files:**
- Modify: `NetworkMonitor/Views/SettingsPage.xaml` — a `SelectorBarItem` between Devices and Other (lines 67–73), and a new `ThemePanel` `ScrollViewer`
- Modify: `NetworkMonitor/Views/SettingsPage.xaml.cs` lines 41–52 (`TabBarSelectionChanged`)
- Modify: `NetworkMonitor/ViewModels/SettingsViewModel.cs`

**Interfaces:**
- Consumes: `ChartPaletteService`, `ChartSchemeCatalog`, `ChartRole`.
- Produces: `SettingsViewModel.ChartSchemeNames → IReadOnlyList<string>`, `ChartSchemeIndex → int`, `IsCustomScheme → bool`, `CustomDownloadColour` / `CustomUploadColour` / `CustomLatencyColour` / `CustomJitterColour` / `CustomSelectionColour` (all `Color`), `ResetChartSchemeCommand`.

- [ ] **Step 1: Add the tab item**

In `NetworkMonitor/Views/SettingsPage.xaml`, between the `Device` and `Other` items (after line 69, before line 71):

```xml
            <SelectorBarItem
                Tag="Theme"
                Text="Theme" />
```

- [ ] **Step 2: Add the panel**

After the closing `</ScrollViewer>` of `DevicePanel` and before `OtherPanel`, add:

```xml
        <ScrollViewer
            Grid.Row="1"
            x:Name="ThemePanel"
            Margin="0,8,0,0"
            Visibility="Collapsed">

            <StackPanel
                HorizontalAlignment="Center"
                MaxWidth="480"
                Padding="24,16,24,16"
                Spacing="12">

                <Border
                    Style="{StaticResource SettingsCard}">

                    <StackPanel
                        Spacing="12">

                        <TextBlock
                            Style="{StaticResource SettingsCardHeader}"
                            Text="Chart colours" />

                        <StackPanel
                            Spacing="4">

                            <TextBlock
                                Text="Colour scheme" />

                            <ComboBox
                                HorizontalAlignment="Stretch"
                                ItemsSource="{x:Bind ViewModel.ChartSchemeNames}"
                                SelectedIndex="{x:Bind ViewModel.ChartSchemeIndex, Mode=TwoWay}" />

                            <TextBlock
                                Text="Applies to the Internet, Local and Speed test charts, the mini graph, and the coloured Download and Upload figures in the grids. The daily digest keeps its own fixed colours."
                                FontSize="12"
                                Opacity="0.65"
                                TextWrapping="Wrap" />

                        </StackPanel>

                        <StackPanel
                            Orientation="Horizontal"
                            Spacing="12">

                            <StackPanel
                                Spacing="4">

                                <Rectangle
                                    Width="44"
                                    Height="20"
                                    Fill="{StaticResource ChartDownloadBrush}"
                                    RadiusX="3"
                                    RadiusY="3" />

                                <TextBlock
                                    Text="Download"
                                    FontSize="11"
                                    Opacity="0.65" />

                            </StackPanel>

                            <StackPanel
                                Spacing="4">

                                <Rectangle
                                    Width="44"
                                    Height="20"
                                    Fill="{StaticResource ChartUploadBrush}"
                                    RadiusX="3"
                                    RadiusY="3" />

                                <TextBlock
                                    Text="Upload"
                                    FontSize="11"
                                    Opacity="0.65" />

                            </StackPanel>

                            <StackPanel
                                Spacing="4">

                                <Rectangle
                                    Width="44"
                                    Height="20"
                                    Fill="{StaticResource ChartLatencyBrush}"
                                    RadiusX="3"
                                    RadiusY="3" />

                                <TextBlock
                                    Text="Latency"
                                    FontSize="11"
                                    Opacity="0.65" />

                            </StackPanel>

                            <StackPanel
                                Spacing="4">

                                <Rectangle
                                    Width="44"
                                    Height="20"
                                    Fill="{StaticResource ChartJitterBrush}"
                                    RadiusX="3"
                                    RadiusY="3" />

                                <TextBlock
                                    Text="Jitter"
                                    FontSize="11"
                                    Opacity="0.65" />

                            </StackPanel>

                            <StackPanel
                                Spacing="4">

                                <Rectangle
                                    Width="44"
                                    Height="20"
                                    Fill="{StaticResource ChartSelectionBrush}"
                                    RadiusX="3"
                                    RadiusY="3" />

                                <TextBlock
                                    Text="Hover"
                                    FontSize="11"
                                    Opacity="0.65" />

                            </StackPanel>

                        </StackPanel>

                        <StackPanel
                            Spacing="8"
                            Visibility="{x:Bind ViewModel.IsCustomScheme, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">

                            <TextBlock
                                Text="Custom colours"
                                FontSize="12"
                                Opacity="0.65" />

                            <StackPanel
                                Orientation="Horizontal"
                                Spacing="8">

                                <Button
                                    Content="Download">

                                    <Button.Flyout>

                                        <Flyout>

                                            <ColorPicker
                                                IsAlphaEnabled="False"
                                                IsColorSliderVisible="True"
                                                IsMoreButtonVisible="True"
                                                Color="{x:Bind ViewModel.CustomDownloadColour, Mode=TwoWay}" />

                                        </Flyout>

                                    </Button.Flyout>

                                </Button>

                                <Button
                                    Content="Upload">

                                    <Button.Flyout>

                                        <Flyout>

                                            <ColorPicker
                                                IsAlphaEnabled="False"
                                                IsColorSliderVisible="True"
                                                IsMoreButtonVisible="True"
                                                Color="{x:Bind ViewModel.CustomUploadColour, Mode=TwoWay}" />

                                        </Flyout>

                                    </Button.Flyout>

                                </Button>

                                <Button
                                    Content="Latency">

                                    <Button.Flyout>

                                        <Flyout>

                                            <ColorPicker
                                                IsAlphaEnabled="False"
                                                IsColorSliderVisible="True"
                                                IsMoreButtonVisible="True"
                                                Color="{x:Bind ViewModel.CustomLatencyColour, Mode=TwoWay}" />

                                        </Flyout>

                                    </Button.Flyout>

                                </Button>

                            </StackPanel>

                            <StackPanel
                                Orientation="Horizontal"
                                Spacing="8">

                                <Button
                                    Content="Jitter">

                                    <Button.Flyout>

                                        <Flyout>

                                            <ColorPicker
                                                IsAlphaEnabled="False"
                                                IsColorSliderVisible="True"
                                                IsMoreButtonVisible="True"
                                                Color="{x:Bind ViewModel.CustomJitterColour, Mode=TwoWay}" />

                                        </Flyout>

                                    </Button.Flyout>

                                </Button>

                                <Button
                                    Content="Hover line">

                                    <Button.Flyout>

                                        <Flyout>

                                            <ColorPicker
                                                IsAlphaEnabled="False"
                                                IsColorSliderVisible="True"
                                                IsMoreButtonVisible="True"
                                                Color="{x:Bind ViewModel.CustomSelectionColour, Mode=TwoWay}" />

                                        </Flyout>

                                    </Button.Flyout>

                                </Button>

                            </StackPanel>

                            <TextBlock
                                Text="Each colour is adjusted automatically for the light and dark card backgrounds, so it always stays readable."
                                FontSize="12"
                                Opacity="0.65"
                                TextWrapping="Wrap" />

                        </StackPanel>

                        <Button
                            Content="Reset to Classic"
                            HorizontalAlignment="Left"
                            Command="{x:Bind ViewModel.ResetChartSchemeCommand}" />

                    </StackPanel>

                </Border>

            </StackPanel>

        </ScrollViewer>
```

`SettingsCard` (line 12) and `SettingsCardHeader` (line 38) are both defined in the `Page.Resources` block at the top of the file, so both keys resolve without further work.

- [ ] **Step 3: Show and hide the panel**

In `NetworkMonitor/Views/SettingsPage.xaml.cs`, replace the body of `TabBarSelectionChanged` (lines 44–50):

```csharp
            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;
                TrafficPanel.Visibility = selectedTag == "Traffic" ? Visibility.Visible : Visibility.Collapsed;
                DevicePanel.Visibility = selectedTag == "Device" ? Visibility.Visible : Visibility.Collapsed;
                ThemePanel.Visibility = selectedTag == "Theme" ? Visibility.Visible : Visibility.Collapsed;
                OtherPanel.Visibility = selectedTag == "Other" ? Visibility.Visible : Visibility.Collapsed;
            }
```

- [ ] **Step 4: Add the ViewModel members**

In `NetworkMonitor/ViewModels/SettingsViewModel.cs`, add to the Fields section:

```csharp
        private readonly ChartPaletteService _chartPalette;
```

Add the constructor parameter `ChartPaletteService chartPalette` and, in the body:

```csharp
            _chartPalette = chartPalette;
            _chartSchemeIndex = IndexForSchemeId(chartPalette.SchemeId);
```

Add to the Properties section:

```csharp
        public IReadOnlyList<string> ChartSchemeNames
        {
            get;
        } = ChartSchemeCatalog.Presets
            .Select(preset => preset.DisplayName)
            .Append("Custom")
            .ToList();

        private int _chartSchemeIndex;

        public int ChartSchemeIndex
        {
            get => _chartSchemeIndex;
            set
            {

                if (SetProperty(ref _chartSchemeIndex, value))
                {
                    string schemeId = value >= 0 && value < ChartSchemeCatalog.Presets.Count
                        ? ChartSchemeCatalog.Presets[value].Id
                        : ChartSchemeCatalog.CustomSchemeId;
                    _chartPalette.ApplyScheme(schemeId);
                    OnPropertyChanged(nameof(IsCustomScheme));
                }

            }
        }

        public bool IsCustomScheme => _chartPalette.IsCustom;

        public Color CustomDownloadColour
        {
            get => ColourForRole(ChartRole.Download);
            set
            {
                SetCustomColour(ChartRole.Download, value, nameof(CustomDownloadColour));
            }
        }

        public Color CustomUploadColour
        {
            get => ColourForRole(ChartRole.Upload);
            set
            {
                SetCustomColour(ChartRole.Upload, value, nameof(CustomUploadColour));
            }
        }

        public Color CustomLatencyColour
        {
            get => ColourForRole(ChartRole.Latency);
            set
            {
                SetCustomColour(ChartRole.Latency, value, nameof(CustomLatencyColour));
            }
        }

        public Color CustomJitterColour
        {
            get => ColourForRole(ChartRole.Jitter);
            set
            {
                SetCustomColour(ChartRole.Jitter, value, nameof(CustomJitterColour));
            }
        }

        public Color CustomSelectionColour
        {
            get => ColourForRole(ChartRole.Selection);
            set
            {
                SetCustomColour(ChartRole.Selection, value, nameof(CustomSelectionColour));
            }
        }
```

Add to the Public methods section:

```csharp
        [RelayCommand]
        public void ResetChartScheme()
        {
            _chartPalette.ResetToDefault();
            ChartSchemeIndex = IndexForSchemeId(ChartSchemeCatalog.DefaultSchemeId);
            OnPropertyChanged(nameof(IsCustomScheme));
        }
```

Add to the Private methods section:

```csharp
        private static int IndexForSchemeId(string schemeId)
        {
            int match = -1;

            for (int index = 0; index < ChartSchemeCatalog.Presets.Count; index++)
            {

                if (string.Equals(ChartSchemeCatalog.Presets[index].Id, schemeId, StringComparison.OrdinalIgnoreCase))
                {
                    match = index;
                }

            }

            int result = match >= 0 ? match : ChartSchemeCatalog.Presets.Count;

            return result;
        }

        private Color ColourForRole(ChartRole role)
        {
            string hex = _chartPalette.CurrentBasePalette().ForRole(role);
            byte red = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Color result = Color.FromArgb(0xFF, red, green, blue);

            return result;
        }

        private void SetCustomColour(ChartRole role, Color colour, string propertyName)
        {
            string hex = string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}",
                colour.R,
                colour.G,
                colour.B);

            _chartPalette.ApplyCustomColour(role, hex);
            OnPropertyChanged(propertyName);
        }
```

Add `using System.Collections.Generic;`, `using System.Globalization;`, `using NetworkMonitor.Core.Charting;`, `using NetworkMonitor.Services.Charting;` and `using Windows.UI;` to the using block. `System.Linq` and `CommunityToolkit.Mvvm.Input` are already imported.

The picker properties read the **base** palette, not the derived one — the user set a base colour and should see that value when reopening the picker. The swatch row shows the derived result, since it binds to the App brushes.

`ChartSchemeIndex` writes through `_chartPalette.ApplyScheme`, which persists to `settings.json` itself, so this property must **not** be added to the `OnSettingChanged` handler that the other settings use.

- [ ] **Step 5: Build**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds.

If `[RelayCommand]` does not generate `ResetChartSchemeCommand`, confirm the class is declared `partial` — it is, at line 16.

- [ ] **Step 6: Verify the whole feature by hand**

Launch the app.

1. Settings shows four tabs in the order Traffic, Devices, Theme, Other. The Theme tab shows the Chart colours card.
2. Pick each of the five presets in turn. The swatch row changes immediately.
3. With the Internet page open in one window and the mini graph showing, change the scheme. Both repaint immediately, with no restart.
4. Visit Local and Speed test after a change. Grid text, chips and legend swatches all follow.
5. Hover the Internet chart. The dashed selection line uses the new scheme's hover colour.
6. Select Custom. The five picker buttons appear. Change Download to a strong green; the charts, grids and mini graph all follow.
7. Switch to a preset and back to Custom. The green is still there.
8. Switch Windows to light mode with the app open. Colours re-derive and stay readable on the light cards.
9. Restart the app. The chosen scheme persists.
10. Press Reset to Classic. The ComboBox returns to Classic and the charts return to the original blue and purple.

Report any step that fails rather than working around it.

- [ ] **Step 7: Commit**

```bash
git add NetworkMonitor/Views/SettingsPage.xaml NetworkMonitor/Views/SettingsPage.xaml.cs NetworkMonitor/ViewModels/SettingsViewModel.cs
git commit -m "Add a Theme tab with the chart colour scheme picker."
```

---

### Task 10: Documentation and spec corrections

**Files:**
- Modify: `Documents/superpowers/specs/2026-08-12-chart-colour-schemes-design.md`
- Modify: `CLAUDE.md` (the Key Files table)
- Modify: `Documents/To Do.txt`
- Modify: `Documents/Release Notes (pending).md`
- Modify: `NetworkMonitor.slnx`

- [ ] **Step 1: Confirm the spec corrections**

Two spec inaccuracies — the literal count and the missing `Oklch` row — were corrected when this plan was written. Confirm:

Run: `git grep -n -E "twenty XAML literals|An L/C/H triple" -- Documents/superpowers/specs/2026-08-12-chart-colour-schemes-design.md`
Expected: two lines. If either is missing, apply it now.

- [ ] **Step 2: Add the new files to the CLAUDE.md Key Files table**

Add these rows:

```markdown
| `NetworkMonitor.Core/Charting/PaletteVariant.cs` | Derives a chart colour for the dark or light card surface from one base hex |
| `NetworkMonitor.Core/Charting/ChartSchemeCatalog.cs` | The five chart colour presets; Classic is the default |
| `NetworkMonitor.Services/Charting/ChartPaletteService.cs` | Resolved palette per role + `PaletteChanged`; the single source of chart colour |
```

- [ ] **Step 3: Confirm the slnx registration**

This plan was registered in `NetworkMonitor.slnx` under `/Documents/Superpowers/Plans/` when it was written, so there is nothing to add. Confirm it is still there:

Run: `git grep -n "2026-08-12-chart-colour-schemes" -- NetworkMonitor.slnx`
Expected: one line.

- [ ] **Step 4: Update the To Do and release notes**

In `Documents/To Do.txt`, mark the roadmap line: `Done - Chart colour schemes`.

In `Documents/Release Notes (pending).md`, add an entry describing the feature in user terms: a new Theme tab in Settings with five chart colour schemes plus custom colours, applied instantly across the charts, grids and mini graph.

- [ ] **Step 5: Full verification**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`
Expected: PASS, all tests including the 94 added by this plan (14 + 19 + 61).

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: build succeeds.

Confirm no migration was created: `git status` shows nothing under `NetworkMonitor.Services/Data/Migrations/`. This feature touches no schema.

- [ ] **Step 6: Commit**

```bash
git add Documents/superpowers/specs/2026-08-12-chart-colour-schemes-design.md CLAUDE.md Documents/To Do.txt "Documents/Release Notes (pending).md" NetworkMonitor.slnx
git commit -m "Document the chart colour scheme feature."
```

---

## Verification Summary

| Concern | How it is verified |
|---|---|
| Colour maths correct | `OklchColourTests` — round-trip, contrast anchors, gamut reduction |
| Derivation correct | `PaletteVariantTests` — contrast floor, band, hue held, deterministic |
| No preset ships unreadable | `ChartSchemeCatalogTests` — 5 presets × 5 roles × 2 surfaces contrast sweep |
| Corrupt settings survive | `ChartSchemeCatalogTests` — null, empty, whitespace, unknown id all fall back to Classic |
| Every literal replaced | `git grep` returns nothing for all four hexes in `Views` and in `TrafficAreaChart.xaml.cs` |
| Immediate repaint | Task 9 Step 6, items 2–7 — manual, spans WinUI and Win2D and cannot be unit-tested |
| Light mode | Task 9 Step 6, item 8 — manual |
| Persistence | Task 9 Step 6, item 9 — manual |
| No schema change | Task 10 Step 5 — no migration in `git status` |
