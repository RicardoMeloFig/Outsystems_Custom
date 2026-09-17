using NetTools;
using NUnit.Framework;
using NUnitExtension.OutSystems.Framework;
using OutSystems.RuntimeCommon.Cryptography;
using OutSystems.ServerTests.Framework;
using OutSystems.TestsCommon;

namespace OutSystems.ServerTests.RuntimePlatform.LoginBruteForce {
    [DashboardTestFixture(TestBase.DashboardTestKind)]
    public class BitsLibTests : TestBase {

        [Test(Description = "Test applies a NOT operation over a byte array")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void NotTest() {
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x29, 0xA1 }, Bits.Not(new byte[] { 0xD6, 0x5E })));
        }

        [Test(Description = "Test applies a AND operation between two byte arrays")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void AndTest() {
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x00, 0x5E, 0x52 }, Bits.And(new byte[] { 0xD6, 0x5E, 0xD6 }, new byte[] { 0x00, 0xFF, 0x72 })));
        }

        [Test(Description = "Test applies a OR operation between two byte arrays")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void OrTest() {
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xD6, 0xFF, 0xF6 }, Bits.Or(
                new byte[] { 0xD6, 0x5E, 0xD6 },
                new byte[] { 0x00, 0xFF, 0x72 })));
        }

        [Test(Description = "Test compares if one byte array is greater or equal to another byte array")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void GETest() {
            Assert.IsTrue(
            Bits.GE(
                new byte[] { 0x12, 0x3c, 0xA5 },
                new byte[] { 0x12, 0x3c, 0xA5 }));

            Assert.IsTrue(
            Bits.GE(
                new byte[] { 0x12, 0x3c, 0xA5 },
                new byte[] { 0x12, 0x4c, 0x00 }));

            Assert.IsTrue(
            Bits.GE(
                new byte[] { 0x12, 0x3c, 0xA5 },
                new byte[] { 0x13, 0x00, 0xA5 }));

            Assert.IsFalse(
            Bits.GE(
                new byte[] { 0x12, 0x3d, 0xFF },
                new byte[] { 0x12, 0x3c, 0xA5 }));

            Assert.IsFalse(
            Bits.GE(
                new byte[] { 0x11, 0xFF, 0xA5 },
                new byte[] { 0x10, 0x3c, 0xA5 }));
        }

        [Test(Description = "Test compares if one byte array is lesser or equal to another byte array")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void LETest() {
            Assert.IsTrue(
            Bits.LE(
                new byte[] { 0x12, 0x3c, 0xA5 },
                new byte[] { 0x12, 0x3c, 0xA5 }));

            Assert.IsFalse(
            Bits.LE(
                new byte[] { 0x12, 0x3c, 0xA5 },
                new byte[] { 0x12, 0x4c, 0x00 }));


            Assert.IsFalse(
            Bits.LE(
                new byte[] { 0x12, 0x3c, 0xA5 },
                new byte[] { 0x13, 0x00, 0xA5 }));

            Assert.IsTrue(
            Bits.LE(
                new byte[] { 0x12, 0x3d, 0xFF },
                new byte[] { 0x12, 0x3c, 0xA5 }));

            Assert.IsTrue(
            Bits.LE(
                new byte[] { 0x11, 0xFF, 0xA5 },
                new byte[] { 0x10, 0x3c, 0xA5 }));
        }

        [Test(Description = "Tests if a byte array of a given size with a given number of bits is successfully created")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void GetBitMaskTest() {
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xff, 0xfe, 0x00, 0x00 }, Bits.GetBitMask(4, 15)));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xff, 0xff, 0x00, 0x00 }, Bits.GetBitMask(4, 16)));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xff, 0xff, 0xe0, 0x00 }, Bits.GetBitMask(4, 19)));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xff, 0xff, 0xff, 0x00 }, Bits.GetBitMask(4, 24)));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xff, 0xff, 0xff, 0xff }, Bits.GetBitMask(4, 32)));
        }

        [Test(Description = "Tests if the number of bits returned for a given bit mask")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void GetBitMaskLengthTest() {
            Assert.IsTrue(1 == Bits.GetBitMaskLength(new byte[] { 128, 0, 0, 0 }));
            Assert.IsTrue(8 == Bits.GetBitMaskLength(new byte[] { 255, 0, 0, 0 }));
            Assert.IsTrue(16 == Bits.GetBitMaskLength(new byte[] { 255, 255, 0, 0 }));
            Assert.IsTrue(24 == Bits.GetBitMaskLength(new byte[] { 255, 255, 255, 0 }));
            Assert.IsTrue(23 == Bits.GetBitMaskLength(new byte[] { 255, 255, 254, 0 }));
            Assert.IsTrue(22 == Bits.GetBitMaskLength(new byte[] { 255, 255, 252, 0 }));
            Assert.IsTrue(21 == Bits.GetBitMaskLength(new byte[] { 255, 255, 248, 0 }));
            Assert.IsTrue(20 == Bits.GetBitMaskLength(new byte[] { 255, 255, 240, 0 }));
            Assert.IsTrue(19 == Bits.GetBitMaskLength(new byte[] { 255, 255, 224, 0 }));
            Assert.IsTrue(18 == Bits.GetBitMaskLength(new byte[] { 255, 255, 192, 0 }));
            Assert.IsTrue(17 == Bits.GetBitMaskLength(new byte[] { 255, 255, 128, 0 }));
            Assert.IsTrue(31 == Bits.GetBitMaskLength(new byte[] { 255, 255, 255, 254 }));
            Assert.IsTrue(32 == Bits.GetBitMaskLength(new byte[] { 255, 255, 255, 255 }));

            Assert.IsNull(Bits.GetBitMaskLength(new byte[] { 255, 1, 0, 0 }));
            Assert.IsNull(Bits.GetBitMaskLength(new byte[] { 255, 127, 0, 0 }));

            Assert.IsNull(Bits.GetBitMaskLength(new byte[] { 255, 0, 0, 128 }));
            Assert.IsNull(Bits.GetBitMaskLength(new byte[] { 255, 192, 0, 255 }));
        }

        [Test(Description = "Tests if a byte array is correctly incremented by one")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void IncrementTest() {
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x00, 0x00, 0x00, 0x01 }, Bits.Increment(new byte[] { 0x00, 0x00, 0x00, 0x00 })));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x01, 0x00, 0x00, 0x00 }, Bits.Increment(new byte[] { 0x00, 0xff, 0xff, 0xff })));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x0a, 0x00, 0x00, 0x02 }, Bits.Increment(new byte[] { 0x0a, 0x00, 0x00, 0x01 })));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x0a, 0x00, 0x01, 0x00 }, Bits.Increment(new byte[] { 0x0a, 0x00, 0x00, 0xff })));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x0a, 0x00, 0xf5, 0x00 }, Bits.Increment(new byte[] { 0x0a, 0x00, 0xf4, 0xff })));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0x0b, 0x00, 0x00, 0x00 }, Bits.Increment(new byte[] { 0x0a, 0xff, 0xff, 0xff })));
            Assert.IsTrue(CryptManager.SlowEquals(new byte[] { 0xff, 0xff, 0xff, 0xff }, Bits.Increment(new byte[] { 0xff, 0xff, 0xff, 0xfe })));
        }
    }       
}
