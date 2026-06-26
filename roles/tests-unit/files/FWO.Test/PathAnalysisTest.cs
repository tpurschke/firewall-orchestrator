using FWO.Basics;
using FWO.Data;
using FWO.Data.Flow;
using FWO.Data.Modelling;
using FWO.Services;
using NUnit.Framework;

namespace FWO.Test
{
    internal class PathAnalysisTest
    {
        [TestCase("10.2.0.0/16", "10.3.0.0/16", new[] { "FW-B", "FW-A", "FW-C" })]
        [TestCase("10.1.0.0/16", "10.2.0.0/16", new[] { "FW-A", "FW-B" })]
        [TestCase("10.2.0.0/16", "10.4.0.0/16", new[] { "FW-B", "FW-D" })]
        [TestCase("10.2.0.10/32", "10.2.0.20/32", new[] { "FW-B" })]
        [TestCase("10.9.0.0/24", "10.2.0.0/16", new[] { "FW-A", "FW-B" })]
        [TestCase("10.2.0.0/16", "Internet", new[] { "FW-B", "FW-E" })]
        [TestCase("Internet", "10.2.0.0/16", new[] { "FW-E", "FW-B" })]
        public void AnalyzeSinglePath_ImplementsPdfExamples(string source, string destination, string[] expectedGateways)
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(ExampleImportData());

            List<string> gatewayNames = table.AnalyzeSinglePath(source, destination);

            Assert.That(gatewayNames, Is.EqualTo(expectedGateways));
        }

        [TestCase("2001:db8:2::/48", "2001:db8:3::/48", new[] { "FW-B", "FW-A", "FW-C" })]
        [TestCase("2001:db8:1::/48", "2001:db8:2::/48", new[] { "FW-A", "FW-B" })]
        [TestCase("2001:db8:2::/48", "2001:db8:4::/48", new[] { "FW-B", "FW-D" })]
        [TestCase("2001:db8:2::10", "2001:db8:2::20", new[] { "FW-B" })]
        [TestCase("2001:db8:9::/48", "2001:db8:2::/48", new[] { "FW-A", "FW-B" })]
        [TestCase("2001:db8:2::/48", "Internet", new[] { "FW-B", "FW-E" })]
        [TestCase("Internet", "2001:db8:2::/48", new[] { "FW-E", "FW-B" })]
        public void AnalyzeSinglePath_ImplementsPdfExamplesForIpv6(string source, string destination, string[] expectedGateways)
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(Ipv6ImportData());

            List<string> gatewayNames = table.AnalyzeSinglePath(source, destination);

            Assert.That(gatewayNames, Is.EqualTo(expectedGateways));
        }

        [Test]
        public void AnalyzeSinglePath_ResolvesIpv4AndIpv6FromTheSameTable()
        {
            PathAnalysisTable table = PathAnalysisTable.Merge(
            [
                PathAnalysisTable.FromImportData(ExampleImportData()),
                PathAnalysisTable.FromImportData(Ipv6ImportData())
            ]);

            Assert.That(table.AnalyzeSinglePath("10.2.0.0/16", "10.3.0.0/16"), Is.EqualTo(new[] { "FW-B", "FW-A", "FW-C" }));
            Assert.That(table.AnalyzeSinglePath("2001:db8:2::/48", "2001:db8:3::/48"), Is.EqualTo(new[] { "FW-B", "FW-A", "FW-C" }));
        }

        [Test]
        public void AnalyzeSinglePath_TreatsUnknownPublicIpv6AsInternet()
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(Ipv6ImportData());

            List<string> gatewayNames = table.AnalyzeSinglePath("2001:db8:2::/48", "2606:4700::1111");

            Assert.That(gatewayNames, Is.EqualTo(new[] { "FW-B", "FW-E" }));
        }

        [Test]
        public void AnalyzeSinglePath_RejectsUnknownPrivateIpv6()
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(Ipv6ImportData());

            Assert.Throws<PathAnalysisException>(() => table.AnalyzeSinglePath("2001:db8:2::/48", "fd00:dead:beef::1"));
        }

        [Test]
        public void FromImportData_RejectsInconsistentRootSuccessor()
        {
            PathAnalysisImportParameters invalidImport = new()
            {
                SourceName = "invalid.json",
                Entries =
                [
                    Entry("A", "10.1.0.0/16", "Start|FW-A|Root", "Start|FW-A|Internet"),
                    Entry("B", "10.2.0.0/16", "Start|FW-A|FW-B|Root", "Start|FW-A|Internet")
                ]
            };

            Assert.Throws<PathAnalysisException>(() => PathAnalysisTable.FromImportData(invalidImport));
        }

        [Test]
        public void ValidateImportSourceShape_RejectsCsvFiles()
        {
            Assert.Throws<ArgumentException>(() =>
                ImportPathPolicy.ValidateImportSourceShape("/usr/local/fworch/scripts/customizing/path-analysis/tsq.csv"));
        }

        [Test]
        public async Task GetDeviceNamesForSinglePath_StaticImportModeUsesActiveTable()
        {
            PathAnalysisTableStore.Replace(PathAnalysisTable.FromImportData(ExampleImportData()));
            try
            {
                string gatewayNames = await PathAnalysis.GetDeviceNamesForSinglePath("10.2.0.0/16", "10.4.0.0/16",
                    new SimulatedApiConnection(), PathAnalysisMode.StaticImport);

                Assert.That(gatewayNames, Is.EqualTo("FW-B, FW-D"));
            }
            finally
            {
                PathAnalysisTableStore.Replace(null);
            }
        }

        [Test]
        public void GetGatewayNames_FlowAccess_ExpandsSourcesAndDestinations()
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(ExampleImportData());
            FlowAccess access = new()
            {
                Sources = [new() { NwObject = new() { IpStart = "10.1.0.10", IpEnd = "10.1.0.10" } }],
                Destinations = [new() { NwObject = new() { IpStart = "10.2.0.10", IpEnd = "10.2.0.10" } }]
            };

            List<string> gatewayNames = PathAnalysis.GetGatewayNames(access, table);

            Assert.That(gatewayNames, Is.EqualTo(new[] { "FW-A", "FW-B" }));
        }

        [Test]
        public void GetGatewayNames_Rule_ExpandsRuleNetworkLocations()
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(ExampleImportData());
            Rule rule = new()
            {
                Froms = [new(new(), new() { IP = "10.2.0.10/32" })],
                Tos = [new(new(), new() { IP = "10.3.0.10/32" })]
            };

            List<string> gatewayNames = PathAnalysis.GetGatewayNames(rule, table);

            Assert.That(gatewayNames, Is.EqualTo(new[] { "FW-B", "FW-A", "FW-C" }));
        }

        [Test]
        public void GetGatewayNames_ModellingConnection_ExpandsAppServers()
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(ExampleImportData());
            ModellingConnection connection = new()
            {
                SourceAppServers = [new() { Content = new() { Ip = "10.2.0.10", IpEnd = "10.2.0.10" } }],
                DestinationAppServers = [new() { Content = new() { Ip = "10.4.0.10", IpEnd = "10.4.0.10" } }]
            };

            List<string> gatewayNames = PathAnalysis.GetGatewayNames(connection, table);

            Assert.That(gatewayNames, Is.EqualTo(new[] { "FW-B", "FW-D" }));
        }

        private static PathAnalysisImportParameters ExampleImportData()
        {
            return new()
            {
                SourceName = "examples.json",
                Entries =
                [
                    Entry("A", "10.1.0.0/16", "Start|FW-A|Root", "Start|FW-A|FW-E|Internet"),
                    Entry("B", "10.2.0.0/16", "Start|FW-B|FW-A|Root", "Start|FW-B|FW-E|Internet"),
                    Entry("C", "10.3.0.0/16", "Start|FW-C|Root", "Start|FW-C|FW-E|Internet"),
                    Entry("D", "10.4.0.0/16", "Start|FW-D|FW-B|FW-A|Root", "Start|FW-D|FW-B|FW-E|Internet"),
                    Entry("R", "10.9.0.0/24", "Start|-|Root", "Start|FW-E|Internet"),
                    Entry("INTERNET", "! RFC1918", "Start|-|Root", "Start|-|Internet")
                ]
            };
        }

        private static PathAnalysisImportParameters Ipv6ImportData()
        {
            return new()
            {
                SourceName = "examples-ipv6.json",
                Entries =
                [
                    Entry("A", "2001:db8:1::/48", "Start|FW-A|Root", "Start|FW-A|FW-E|Internet"),
                    Entry("B", "2001:db8:2::/48", "Start|FW-B|FW-A|Root", "Start|FW-B|FW-E|Internet"),
                    Entry("C", "2001:db8:3::/48", "Start|FW-C|Root", "Start|FW-C|FW-E|Internet"),
                    Entry("D", "2001:db8:4::/48", "Start|FW-D|FW-B|FW-A|Root", "Start|FW-D|FW-B|FW-E|Internet"),
                    Entry("R", "2001:db8:9::/48", "Start|-|Root", "Start|FW-E|Internet"),
                    Entry("INTERNET", "! RFC4193", "Start|-|Root", "Start|-|Internet")
                ]
            };
        }

        private static PathAnalysisImportEntry Entry(string zone, string network, string rootPath, string internetPath)
        {
            return new()
            {
                Version = "v",
                Zone = zone,
                Network = network,
                RootPath = rootPath,
                InternetPath = internetPath
            };
        }
    }
}
