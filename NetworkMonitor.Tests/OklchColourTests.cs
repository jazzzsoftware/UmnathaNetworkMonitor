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

        [Fact]
        public void TryParseHexAcceptsAValidHashPrefixedHex()
        {
            bool isValid = OklchColour.TryParseHex("#1976D2", out string normalisedHex);

            Assert.True(isValid);
            Assert.Equal("#1976D2", normalisedHex);
        }

        [Fact]
        public void TryParseHexAcceptsAValidBareHex()
        {
            bool isValid = OklchColour.TryParseHex("1976D2", out string normalisedHex);

            Assert.True(isValid);
            Assert.Equal("#1976D2", normalisedHex);
        }

        [Fact]
        public void TryParseHexNormalisesLowercaseToUppercase()
        {
            bool isValid = OklchColour.TryParseHex("#1976d2", out string normalisedHex);

            Assert.True(isValid);
            Assert.Equal("#1976D2", normalisedHex);
        }

        [Fact]
        public void TryParseHexRejectsNull()
        {
            bool isValid = OklchColour.TryParseHex(null, out string normalisedHex);

            Assert.False(isValid);
            Assert.Equal(string.Empty, normalisedHex);
        }

        [Fact]
        public void TryParseHexRejectsEmpty()
        {
            bool isValid = OklchColour.TryParseHex(string.Empty, out string normalisedHex);

            Assert.False(isValid);
            Assert.Equal(string.Empty, normalisedHex);
        }

        [Fact]
        public void TryParseHexRejectsWhitespace()
        {
            bool isValid = OklchColour.TryParseHex("   ", out string normalisedHex);

            Assert.False(isValid);
            Assert.Equal(string.Empty, normalisedHex);
        }

        [Fact]
        public void TryParseHexRejectsTheWrongLength()
        {
            bool isValid = OklchColour.TryParseHex("#19D2", out string normalisedHex);

            Assert.False(isValid);
            Assert.Equal(string.Empty, normalisedHex);
        }

        [Fact]
        public void TryParseHexRejectsNonHexCharacters()
        {
            bool isValid = OklchColour.TryParseHex("#GGGGGG", out string normalisedHex);

            Assert.False(isValid);
            Assert.Equal(string.Empty, normalisedHex);
        }
    }
}
