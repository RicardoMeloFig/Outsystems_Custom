using System;
using System.Linq;
using System.Net;
using NetTools;
using NUnit.Framework;
using NUnitExtension.OutSystems.Framework;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.ServerTests.Framework;
using OutSystems.TestsCommon;

namespace OutSystems.ServerTests.RuntimePlatform.LoginBruteForce {
    [DashboardTestFixture(TestBase.DashboardTestKind)]
    public class IPAddressRangeTests : TestBase {

        [Test(Description = "Tests if a given ip address is a valid IPv4 address and is correctly parsed to create a range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void CtorTest_Single() {
            var range = new IPAddressRange(IPAddress.Parse("192.168.0.88"));
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("192.168.0.88", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("192.168.0.88", range.End.ToString());
        }

        [Test(Description = "Tests if a given ip address is a valid IPv4 address and a range is correctly created by giving the number of bits of a mask")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void CtorTest_MaskLength() {
            var range = new IPAddressRange(IPAddress.Parse("192.168.0.80"), 24);
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("192.168.0.0", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("192.168.0.255", range.End.ToString());
        }

        [Test(Description = "Tests if a given ip address is a valid IPv4 address and is correctly parsed to create a range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv4_Uniaddress() {
            var range = IPAddressRange.Parse("192.168.60.13");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("192.168.60.13", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("192.168.60.13", range.End.ToString());
        }

        [Test(Description = "Tests if a range is correctly created by givin an ip address in IPv4 with CIDR block notation")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv4_CIDR() {
            var range = IPAddressRange.Parse("219.165.64.0/19");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("219.165.64.0", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("219.165.95.255", range.End.ToString());
        }

        [Test(Description = "Tests if a range is correctly created by givin an ip address in IPv4 with CIDR block notation with the maximum possible value")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv4_CIDR_Max() {
            var range = IPAddressRange.Parse("219.165.64.73/32");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("219.165.64.73", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("219.165.64.73", range.End.ToString());
        }

        [Test(Description = "Tests if a range is correctly created by giving a IPv4  address with a bit mask")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv4_Bitmask() {
            var range = IPAddressRange.Parse("192.168.1.0/255.255.255.0");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("192.168.1.0", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("192.168.1.255", range.End.ToString());
        }

        [Test(Description = "Tests if a range is correctly created by giving a IPv6 address with a bit mask")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv6_Bitmask() {
            var range = IPAddressRange.Parse("fe80:de40:fbaa::/ffff:ffff:ffff::");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv6(range.Begin.ToString()));
            Assert.IsTrue(range.Begin.ToString().Equals("fe80:de40:fbaa::") || range.Begin.ToString().Equals("fe80:de40:fbaa:0:0:0:0:0"));
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv6(range.End.ToString()));
            Assert.AreEqual("fe80:de40:fbaa:ffff:ffff:ffff:ffff:ffff", range.End.ToString());
        }

        [Test(Description = "Tests if a ranges is correctly created by giving two ip addresses")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv4_Begin_to_End() {
            var range = IPAddressRange.Parse("192.168.60.26-192.168.60.37");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.Begin.ToString()));
            Assert.AreEqual("192.168.60.26", range.Begin.ToString());
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv4(range.End.ToString()));
            Assert.AreEqual("192.168.60.37", range.End.ToString());
        }

        [Test(Description = "Tests if a set of addresses is contained in a range between two ip addresses")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_IPv4() {
            var range = IPAddressRange.Parse("192.168.60.26-192.168.60.37");

            Assert.IsFalse(range.Contains(IPAddress.Parse("192.168.60.25")));
            Assert.IsTrue(range.Contains(IPAddress.Parse("192.168.60.26")));
            Assert.IsTrue(range.Contains(IPAddress.Parse("192.168.60.27")));

            Assert.IsTrue(range.Contains(IPAddress.Parse("192.168.60.36")));
            Assert.IsTrue(range.Contains(IPAddress.Parse("192.168.60.37")));
            Assert.IsFalse(range.Contains(IPAddress.Parse("192.168.60.38")));
        }

        [Test(Description = "Tests if a ip address in IPv6 format is in a range between two IPv4 addresses")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_TestIPv6_to_IPv4Range() {
            var range = IPAddressRange.Parse("192.168.60.26-192.168.60.37");

            Assert.IsFalse(range.Contains(IPAddress.Parse("c0a8:3c1a::")));
        }

        [Test(Description = "Tests if a full range of IPv4 addresses doesn't belong in a IPv6 range and vice-versa")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_with_IPV4andv6_is_False_ever() {
            var fullRangeIPv6 = IPAddressRange.Parse("::-fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");
            Assert.IsFalse(fullRangeIPv6.Contains(IPAddressRange.Parse("192.168.0.0/24")));

            var fullRangeIPv4 = IPAddressRange.Parse("0.0.0.0-255.255.255.255");
            Assert.IsFalse(fullRangeIPv4.Contains(IPAddressRange.Parse("::1-::2")));
        }

        [Test(Description = "Tests if a range of ip addresses is contained in another range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_Range_is_True_IPv4() {
            var range = IPAddressRange.Parse("192.168.60.26-192.168.60.37");
            var range1_same = IPAddressRange.Parse("192.168.60.26-192.168.60.37");
            var range2_samestart = IPAddressRange.Parse("192.168.60.26-192.168.60.30");
            var range3_sameend = IPAddressRange.Parse("192.168.60.36-192.168.60.37");
            var range4_subset = IPAddressRange.Parse("192.168.60.29-192.168.60.32");

            Assert.IsTrue(range.Contains(range1_same));
            Assert.IsTrue(range.Contains(range2_samestart));
            Assert.IsTrue(range.Contains(range3_sameend));
            Assert.IsTrue(range.Contains(range4_subset));
        }

        [Test(Description = "Tests that a range of ip addresses isn't contained in a range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_Range_is_False_IPv4() {
            var range = IPAddressRange.Parse("192.168.60.29-192.168.60.32");
            var range1_overLeft = IPAddressRange.Parse("192.168.60.26-192.168.70.1");
            var range2_overRight = IPAddressRange.Parse("192.168.50.1-192.168.60.37");
            var range3_outOfLeft = IPAddressRange.Parse("192.168.50.30-192.168.50.31");
            var range4_outOfRight = IPAddressRange.Parse("192.168.70.30-192.168.70.31");

            Assert.IsFalse(range.Contains(range1_overLeft));
            Assert.IsFalse(range.Contains(range2_overRight));
            Assert.IsFalse(range.Contains(range3_outOfLeft));
            Assert.IsFalse(range.Contains(range4_outOfRight));
        }

        [Test(Description = "Tests if a IPv6 address is correctly parsed with CIDR notation")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseTest_IPv6_CIDR() {
            var range = IPAddressRange.Parse("fe80::/10");
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv6(range.Begin.ToString()));
            Assert.IsTrue(range.Begin.ToString().Equals("fe80::") || range.Begin.ToString().Equals("fe80:0:0:0:0:0:0:0"));
            Assert.IsTrue(RuntimePlatformUtils.IPAddressIsValidIPv6(range.End.ToString()));
            Assert.AreEqual("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff", range.End.ToString());
        }

        [Test(Description = "Tests if an ip address is contained in a IPv6 range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_IPv6() {
            var range = IPAddressRange.Parse("FE80::/10");

            Assert.IsFalse(range.Contains(IPAddress.Parse("::1")));
            Assert.IsTrue(range.Contains(IPAddress.Parse("fe80::d503:4ee:3882:c586")));
        }

        [Test(Description = "Tests if a set of IPv6 ranges are contained in another IPv6 range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_Range_is_True_IPv6() {
            var range = IPAddressRange.Parse("fe80::/10");
            var range1_same = IPAddressRange.Parse("fe80::/10");
            var range2_samestart = IPAddressRange.Parse("fe80::-fe80::d503:4ee:3882:c586");
            var range3_sameend = IPAddressRange.Parse("fe80::d503:4ee:3882:c586-febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff");
            var range4_subset = IPAddressRange.Parse("fe80::d503:4ee:3882:c586-fe80::d504:4ee:3882:c586");

            Assert.IsTrue(range.Contains(range1_same));
            Assert.IsTrue(range.Contains(range2_samestart));
            Assert.IsTrue(range.Contains(range3_sameend));
            Assert.IsTrue(range.Contains(range4_subset));
        }

        [Test(Description = "Test if a set of IPv6 ranges are not contained in another IPv6 range")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ContainsTest_Range_is_False_IPv6() {
            var range = IPAddressRange.Parse("fe80::d503:4ee:3882:c586-fe80::d504:4ee:3882:c586");
            var range1_overLeft = IPAddressRange.Parse("fe80::d502:4ee:3882:c586-fe80::d503:4ee:3882:c586");
            var range2_overRight = IPAddressRange.Parse("fe80::d503:4ef:3882:c586-fe80::d505:4ee:3882:c586");
            var range3_outOfLeft = IPAddressRange.Parse("fe80::d501:4ee:3882:c586-fe80::d502:4ee:3882:c586");
            var range4_outOfRight = IPAddressRange.Parse("fe80::d505:4ee:3882:c586-fe80::d506:4ee:3882:c586");

            Assert.IsFalse(range.Contains(range1_overLeft));
            Assert.IsFalse(range.Contains(range2_overRight));
            Assert.IsFalse(range.Contains(range3_outOfLeft));
            Assert.IsFalse(range.Contains(range4_outOfRight));
        }

        [Test(Description = "Tests if a range of ip addresses is correctly created with a given mask")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void SubnetMaskLengthTest_Valid() {
            var range = new IPAddressRange(IPAddress.Parse("192.168.75.23"), IPAddressRange.SubnetMaskLength(IPAddress.Parse("255.255.254.0")));
            Assert.AreEqual("192.168.74.0", range.Begin.ToString());
            Assert.AreEqual("192.168.75.255", range.End.ToString());
        }

        [Test(Description = "Tests that an exception is thrown when we try to create a range of ip addresses with an invalid mask")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void SubnetMaskLengthTest_Invalid() {
            try {
                new IPAddressRange(IPAddress.Parse("192.168.75.23"), IPAddressRange.SubnetMaskLength(IPAddress.Parse("255.255.54.0")));
            } catch (ArgumentException) {
                Assert.IsTrue(true, "Exception was successfully trown");
            } catch (Exception) {
                Assert.Fail("Should have trown an ArgumentException for using an invalid subnetmask");
            }
        }

        [Test(Description = "Tests the enumerate of a range of IPv4 addresses and if it contains all the addresses")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void Enumerate_IPv4() {
            var ips = IPAddressRange.Parse("192.168.60.253-192.168.61.2").AsEnumerable().ToArray();
            var expected = new IPAddress[]
                {
                    IPAddress.Parse("192.168.60.253"),
                    IPAddress.Parse("192.168.60.254"),
                    IPAddress.Parse("192.168.60.255"),
                    IPAddress.Parse("192.168.61.0"),
                    IPAddress.Parse("192.168.61.1"),
                    IPAddress.Parse("192.168.61.2"),
                };
            //<st replaceBy="Assert.assertArrayEquals(expected, ips);
            //"/>
            Assert.AreEqual(expected, ips);
        }

        [Test(Description = "Tests the enumerate of a range of IPv6 addresses and if it contains the expected number of addresses, as its first and last address")]
        [TestDetails(TestIssue = "", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void Enumerate_IPv6() {
            var ips = IPAddressRange.Parse("fe80::d503:4ee:3882:c586/120").AsEnumerable().ToArray();
            Assert.IsTrue(256 == ips.Length);
            Assert.AreEqual(IPAddress.Parse("fe80::d503:4ee:3882:c500"), ips.First());
            Assert.AreEqual(IPAddress.Parse("fe80::d503:4ee:3882:c5ff"), ips.Last());
        }

        [Test(Description = "Tests the enumerate of a range in a for each loop")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void EnumerateTest_With_Foreach() {
            foreach (var ip in IPAddressRange.Parse("192.168.60.2")) {
                Assert.AreEqual(IPAddress.Parse("192.168.60.2"), ip);
            }

        }


        [Test(Description = "Tests the ToString of a set of ip address ranges")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ToString_Output() {
            var testCases = new string[][] {
                new string[]{"192.168.60.2", "192.168.60.2"}, 
                new string[]{"192.168.60.2/24", "192.168.60.0-192.168.60.255"},
#if JAVA
                new string[]{"fe80::d503:4ee:3882:c586", "fe80:0:0:0:d503:4ee:3882:c586"}, 
                new string[]{"fe80::d503:4ee:3882:c586/120", "fe80:0:0:0:d503:4ee:3882:c500-fe80:0:0:0:d503:4ee:3882:c5ff"}
#else
                new string[]{"fe80::d503:4ee:3882:c586", "fe80::d503:4ee:3882:c586"}, 
                new string[]{"fe80::d503:4ee:3882:c586/120", "fe80::d503:4ee:3882:c500-fe80::d503:4ee:3882:c5ff"}
#endif
            };

            foreach (string[] testCase in testCases) {
                string input = testCase[0];
                string expected = testCase[1];

                var output = IPAddressRange.Parse(input).ToString();
                Assert.AreEqual(expected, output);

                var parsed = IPAddressRange.Parse(output).ToString();
                Assert.AreEqual(expected, parsed, "Output of ToString() should be usable by Parse() and result in the same output");
            }
        }

        [Test(Description = "Tests the number of bits static in a ip address range is valid")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void GetPrefixLength_Success() {
            var testCases = new string[][] {
                new string[]{"fe80::/10", "10"},
                new string[]{"192.168.0.0/24", "24"},
                new string[]{"192.168.0.0", "32"},
                new string[]{"192.168.0.0-192.168.0.0", "32"},
                new string[]{"fe80::", "128"},
                new string[]{"192.168.0.0-192.168.0.255", "24"},
                new string[]{"fe80::-fe80:ffff:ffff:ffff:ffff:ffff:ffff:ffff", "16"}
            };

            foreach (string[] testCase in testCases) {
                string input = testCase[0];
                string expected = testCase[1];
                var output = IPAddressRange.Parse(input).GetPrefixLength().ToString();
                Assert.AreEqual(expected, output);
            }
        }

        [Test(Description = "Tests the number of bits static in a ipaddress range is invalid")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void GetPrefixLength_Failures() {
            var testCases = new string[] {
                "192.168.0.0-192.168.0.254",
                "fe80::-fe80:ffff:ffff:ffff:ffff:ffff:ffff:fffe"
            };

            foreach (string testCase in testCases) {
                string input = testCase;
                try {
                    IPAddressRange.Parse(input).GetPrefixLength();
                    Assert.Fail("Expected exception of type FormatException to be thrown for input \"" + input + "\"");
                } catch (FormatException) {
                    continue; // allow Assert.Fail to pass through 
                } catch (Exception) {
                    Assert.Fail("Expected exception of type FormatException to be thrown for input \"" + input + "\"");
                }
            }
        }

        [Test(Description = "Tests if a ip address range is correctly outputed to a CIDR format")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ToCidrString_Output() {
            var testCases = new string[][] {
                new string[]{"192.168.0.0/24", "192.168.0.0/24"},
                new string[]{"192.168.0.0", "192.168.0.0/32"},
                new string[]{"192.168.0.0-192.168.0.0", "192.168.0.0/32"},
                new string[]{"192.168.0.0-192.168.0.255", "192.168.0.0/24"},
#if JAVA
                new string[]{"fe80::", "fe80:0:0:0:0:0:0:0/128"},
                new string[]{"fe80::/10", "fe80:0:0:0:0:0:0:0/10"},
                new string[]{"fe80::-fe80:ffff:ffff:ffff:ffff:ffff:ffff:ffff", "fe80:0:0:0:0:0:0:0/16"}
#else
                new string[]{"fe80::", "fe80::/128"},
                new string[]{"fe80::/10", "fe80::/10"},
                new string[]{"fe80::-fe80:ffff:ffff:ffff:ffff:ffff:ffff:ffff", "fe80::/16"}
#endif
            };

            foreach (string[] testCase in testCases) {
                string input = testCase[0];
                string expected = testCase[1];
                var output = IPAddressRange.Parse(input).ToCidrString();
                Assert.AreEqual(expected, output);
            }
        }

        [Test(Description = "Tests that an exception if trown when trying to output to CIDR notation a range that doesn't respect such notation")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ToCidrString_ThrowsOnNonCidr() {
            var testCases = new string[] {
                "192.168.0.0-192.168.0.254",
                "fe80::-fe80:ffff:ffff:ffff:ffff:ffff:ffff:fffe"
            };

            foreach (string testCase in testCases) {
                string input = testCase;
                try {
                    IPAddressRange.Parse(input).ToCidrString();
                    Assert.Fail("Expected exception of type FormatException to be thrown for input \"" + input + "\"");
                } catch (FormatException) {
                    continue; // allow Assert.Fail to pass through 
                } catch (Exception) {
                    Assert.Fail("Expected exception of type FormatException to be thrown for input \"" + input + "\"");
                }
            }
        }

        [Test(Description = "Tests the parsing of multiple ranges and checks if the being and end of the range are the expected")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseSucceeds() {
            var testCases = new string[][] {
                new string[]{"192.168.60.13", "192.168.60.13", "192.168.60.13"},
                new string[]{"  192.168.60.13  ", "192.168.60.13", "192.168.60.13"},
                new string[]{"3232252004", "192.168.64.100", "192.168.64.100"},
                new string[]{"  3232252004  ", "192.168.64.100", "192.168.64.100"},
                new string[]{"219.165.64.0/19", "219.165.64.0", "219.165.95.255"},
                new string[]{"  219.165.64.0  /  19  ", "219.165.64.0", "219.165.95.255"},
                new string[]{"192.168.1.0/255.255.255.0", "192.168.1.0", "192.168.1.255"},
                new string[]{"  192.168.1.0  /  255.255.255.0  ", "192.168.1.0", "192.168.1.255"},
                new string[]{"3232252004/24", "192.168.64.0", "192.168.64.255"},
                new string[]{"  3232252004  /  24  ", "192.168.64.0", "192.168.64.255"},
                new string[]{"192.168.60.26–192.168.60.37", "192.168.60.26", "192.168.60.37"},
                new string[]{"  192.168.60.26  –  192.168.60.37  ", "192.168.60.26", "192.168.60.37"},
                new string[]{"3232252004-3232252504", "192.168.64.100", "192.168.66.88"},
                new string[]{"  3232252004  -  3232252504  ", "192.168.64.100", "192.168.66.88"},
                new string[]{"192.168.61.26–192.168.61.37", "192.168.61.26", "192.168.61.37"},
                new string[]{"  192.168.61.26  –  192.168.61.37  ", "192.168.61.26", "192.168.61.37"},
                new string[]{"3232252004–3232252504", "192.168.64.100", "192.168.66.88"},
                new string[]{"  3232252004  –  3232252504  ", "192.168.64.100", "192.168.66.88"},
#if JAVA
                new string[]{"  fe80::c586  –  fe80::c600  ", "fe80:0:0:0:0:0:0:c586", "fe80:0:0:0:0:0:0:c600"},
                new string[]{"fe80::d503:4ee:3882:c586", "fe80:0:0:0:d503:4ee:3882:c586", "fe80:0:0:0:d503:4ee:3882:c586"},
                new string[]{"  fe80::d503:4ee:3882:c586  ", "fe80:0:0:0:d503:4ee:3882:c586", "fe80:0:0:0:d503:4ee:3882:c586"},
                new string[]{"fe80::c586-fe80::c600", "fe80:0:0:0:0:0:0:c586", "fe80:0:0:0:0:0:0:c600"},
                new string[]{"  fe80::c586  -  fe80::c600  ", "fe80:0:0:0:0:0:0:c586", "fe80:0:0:0:0:0:0:c600"},
                new string[]{"fe80::c586–fe80::c600", "fe80:0:0:0:0:0:0:c586", "fe80:0:0:0:0:0:0:c600"}
#else
                new string[]{"  fe80::c586  –  fe80::c600  ", "fe80::c586", "fe80::c600"},
                new string[]{"fe80::d503:4ee:3882:c586", "fe80::d503:4ee:3882:c586", "fe80::d503:4ee:3882:c586"},
                new string[]{"  fe80::d503:4ee:3882:c586  ", "fe80::d503:4ee:3882:c586", "fe80::d503:4ee:3882:c586"},
                new string[]{"fe80::c586-fe80::c600", "fe80::c586", "fe80::c600"},
                new string[]{"  fe80::c586  -  fe80::c600  ", "fe80::c586", "fe80::c600"},
                new string[]{"fe80::c586–fe80::c600", "fe80::c586", "fe80::c600"}
#endif
            };

            foreach (string[] testCase in testCases) {
                string input = testCase[0];
                string expectedBegin = testCase[1];
                string expectedEnd = testCase[2];

                var range = IPAddressRange.Parse(input);
                Assert.AreEqual(expectedBegin, range.Begin.ToString());
                Assert.AreEqual(expectedEnd, range.End.ToString());
            }
        }

        [Test(Description = "Tests the parsing of an invalid range fails with the expected exception")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void ParseFails() {
            var testCases = new string[][] {
                new string[]{null, typeof(ArgumentNullException).ToString()},
                //<st replaceBy='new String[] { "", NumberFormatException.class.toString() } '/>
                new string[]{"", typeof(FormatException).ToString()},
                //<st replaceBy='new String[] { " ", NumberFormatException.class.toString() } '/>
                new string[]{" ", typeof(FormatException).ToString()},
                //<st replaceBy='new String[] { "gvvdv", NumberFormatException.class.toString() } '/>
                new string[]{"gvvdv", typeof(FormatException).ToString()},
                //<st replaceBy='new String[] { "192.168.0.10/48", NumberFormatException.class.toString() } '/>
                new string[]{"192.168.0.10/48", typeof(FormatException).ToString()},
                new string[]{"192.168.0.10-192.168.0.5", typeof(ArgumentException).ToString()},
                //<st replaceBy='new String[] { "10.256.1.1", UnknownHostException.class.toString() } '/>
                new string[]{"10.256.1.1", typeof(FormatException).ToString()}
            };

            foreach (string[] testCase in testCases) {
                string input = testCase[0];
                string expectedException = testCase[1];

                try {
                    IPAddressRange.Parse(input);
                    Assert.Fail("Expected exception of type " + expectedException + " to be thrown for input \"" + input + "\"");
                } catch (Exception e) {
                    Assert.AreEqual(expectedException, e.GetType().ToString(), "Expected exception of type " + expectedException + " to be thrown for input \"" + input + "\"");
                }
            }
        }

        [Test(Description = "Tests if the TryParse method correctly parses a set of ranges and fails when its expected.")]
        [TestDetails(TestIssue = "#LBP-41 #LBP-51", CreatedBy = "hgg", Feature = Features.LoginBruteForceProtection)]
        public void TryParse() {
            string[] successCases = new string[] {
                "192.168.60.13",
                "fe80::d503:4ee:3882:c586",
                "219.165.64.0/19",
                "219.165.64.73/32",
                "192.168.1.0/255.255.255.0", 
                "192.168.60.26-192.168.60.37"
            };
            foreach (string testCase in successCases) {
                IPAddressRange temp;
                Assert.IsTrue(IPAddressRange.TryParse(testCase, out temp));
                Assert.IsNotNull(temp);
            }

            string[] failCases = new string[] {
                null,
                "",
                " ",
                "fdfv",
                "192.168.0.10/48",
                "192.168.60.26-192.168.60.22"
            };

            foreach (string testCase in failCases) {
                IPAddressRange temp;
                Assert.IsFalse(IPAddressRange.TryParse(testCase, out temp));
                Assert.IsNull(temp);
            }
        }
    }
}
