using DS4WinWPF.DS4Control;

namespace DS4WindowsTests
{
    [TestClass]
    public class AppUpdaterTests
    {
        private const string SetupHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string ZipHash = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        // Same format as installer\build.ps1: "<hash>  <file>", LF line endings
        private const string Sums =
            ZipHash + "  DS4Windows_4.1.0_x64.zip\n" +
            SetupHash + "  DS4Windows_4.1.0_x64_Setup.exe\n";

        [TestMethod]
        public void InstallerAssetNameMatchesBuildScript()
        {
            Assert.AreEqual("DS4Windows_4.1.0_x64_Setup.exe", AppUpdater.InstallerAssetName("4.1.0", false));
            Assert.AreEqual("DS4Windows_4.1.0_x64_selfcontained_Setup.exe", AppUpdater.InstallerAssetName("4.1.0", true));
        }

        [TestMethod]
        public void FindsChecksumOfListedFile()
        {
            Assert.AreEqual(SetupHash, AppUpdater.FindChecksum(Sums, "DS4Windows_4.1.0_x64_Setup.exe"));
            Assert.AreEqual(ZipHash, AppUpdater.FindChecksum(Sums, "DS4Windows_4.1.0_x64.zip"));
        }

        [TestMethod]
        public void ReturnsNullForUnlistedFile()
        {
            Assert.IsNull(AppUpdater.FindChecksum(Sums, "DS4Windows_4.1.0_x64_selfcontained_Setup.exe"));
            Assert.IsNull(AppUpdater.FindChecksum(Sums, "Setup.exe"));
            Assert.IsNull(AppUpdater.FindChecksum("", "DS4Windows_4.1.0_x64_Setup.exe"));
            Assert.IsNull(AppUpdater.FindChecksum(null, "DS4Windows_4.1.0_x64_Setup.exe"));
        }

        [TestMethod]
        public void AcceptsCrlfBinaryModeAndUppercaseHash()
        {
            string sums = SetupHash.ToUpperInvariant() + " *DS4Windows_4.1.0_x64_Setup.exe\r\n";
            Assert.AreEqual(SetupHash, AppUpdater.FindChecksum(sums, "DS4Windows_4.1.0_x64_Setup.exe"));
        }

        [TestMethod]
        public void IgnoresMalformedLines()
        {
            string sums = "not-a-hash  DS4Windows_4.1.0_x64_Setup.exe\n" +
                SetupHash[..63] + "  DS4Windows_4.1.0_x64_Setup.exe\n";
            Assert.IsNull(AppUpdater.FindChecksum(sums, "DS4Windows_4.1.0_x64_Setup.exe"));
        }

        [TestMethod]
        public void ComputesSha256OfFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "abc");
                Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                    AppUpdater.ComputeSha256(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
