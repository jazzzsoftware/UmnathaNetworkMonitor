using NetworkMonitor.Core.Widget;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SectionVisibilityTests
    {
        [Theory]
        [InlineData(true, true, true, true, 4)]
        [InlineData(true, false, false, false, 1)]
        [InlineData(false, false, false, false, 0)]
        [InlineData(false, true, false, true, 2)]
        public void CountVisibleCountsEverySectionThatIsOn(bool internet, bool local, bool speedTest, bool unknown, int expected)
        {
            int count = SectionVisibility.CountVisible(internet, local, speedTest, unknown);

            Assert.Equal(expected, count);
        }

        [Fact]
        public void TheLastVisibleSectionCannotBeTurnedOff()
        {
            bool allowed = SectionVisibility.CanApply(true, false, 1);

            Assert.False(allowed);
        }

        [Fact]
        public void TurningOffOneOfSeveralIsAllowed()
        {
            bool allowed = SectionVisibility.CanApply(true, false, 2);

            Assert.True(allowed);
        }

        [Fact]
        public void TurningASectionOnIsAlwaysAllowedEvenAtTheFloor()
        {
            bool allowed = SectionVisibility.CanApply(false, true, 1);

            Assert.True(allowed);
        }

        [Fact]
        public void SettingASectionToWhatItAlreadyIsChangesNothing()
        {
            bool turningOn = SectionVisibility.CanApply(true, true, 3);
            bool turningOff = SectionVisibility.CanApply(false, false, 3);

            Assert.False(turningOn);
            Assert.False(turningOff);
        }
    }
}
