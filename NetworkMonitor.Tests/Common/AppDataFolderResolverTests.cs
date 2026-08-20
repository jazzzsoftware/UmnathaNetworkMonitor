using Xunit;
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.Tests.Common
{
    public class AppDataFolderResolverTests
    {
        [Fact]
        public void NoOverrideFallsBackToTheProductFolderUnderLocalApplicationData()
        {
            string resolved = AppDataFolderResolver.Resolve(null, @"C:\Users\Someone\AppData\Local");

            Assert.Equal(@"C:\Users\Someone\AppData\Local\UmnathaNetworkMonitor", resolved);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void AnEmptyOrWhitespaceOverrideIsTreatedAsAbsent(string overrideValue)
        {
            string resolved = AppDataFolderResolver.Resolve(overrideValue, @"C:\Local");

            Assert.Equal(@"C:\Local\UmnathaNetworkMonitor", resolved);
        }

        [Fact]
        public void AnOverrideIsUsedExactlyAndDoesNotGainTheProductFolder()
        {
            string resolved = AppDataFolderResolver.Resolve(@"D:\uitest\data", @"C:\Local");

            Assert.Equal(@"D:\uitest\data", resolved);
        }

        [Fact]
        public void AnOverrideIsTrimmedSoATrailingSpaceCannotCreateASecondFolder()
        {
            string resolved = AppDataFolderResolver.Resolve(@"  D:\uitest\data  ", @"C:\Local");

            Assert.Equal(@"D:\uitest\data", resolved);
        }
    }
}
