using FWO.Data.Proxy;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    internal class ProxyRulesReportTest
    {
        [Test]
        public void BuildTableData_ExtractsDynamicColumnsFromRawJson()
        {
            List<ProxyRule> rules =
            [
                new ProxyRule
                {
                    Id = "r1",
                    ManagementId = 1,
                    ManagementName = "Mgmt-1",
                    Name = "Base Rule 1",
                    Action = "allow",
                    RawJson = "{\"id\":\"r1\",\"name\":\"Rule One\",\"action\":\"allow\",\"sources\":[{\"name\":\"src1\"},{\"value\":\"src2\"}],\"metadata\":{\"priority\":5,\"flags\":[\"a\",\"b\"]},\"enabled\":true,\"threshold\":12.5}"
                },
                new ProxyRule
                {
                    Id = "r2",
                    ManagementId = 2,
                    ManagementName = "Mgmt-2",
                    Name = "Base Rule 2",
                    Action = "deny",
                    RawJson = "{\"id\":\"r2\",\"name\":\"Rule Two\",\"decision\":\"deny\",\"owner\":\"team\",\"destinations\":[\"dest1\"],\"services\":[{\"name\":\"http\"}],\"meta\":{\"last\":\"2024-01-02\"},\"enabled\":false,\"extra\":null}"
                }
            ];

            ProxyRuleTableData tableData = ProxyRuleColumnHelper.BuildTableData(rules);

            Assert.That(tableData.Columns, Does.Contain("metadata"));
            Assert.That(tableData.Columns, Does.Contain("decision"));
            Assert.That(tableData.Columns, Does.Contain("meta"));
            Assert.That(tableData.Columns, Does.Contain("sources"));
            Assert.That(tableData.Columns, Does.Contain("destinations"));
            Assert.That(tableData.Columns, Does.Contain("services"));
            Assert.That(tableData.Columns, Does.Not.Contain("raw_json"));

            Dictionary<string, string> row1 = tableData.Rows[0];
            Assert.That(row1["sources"], Is.EqualTo("[{\"name\":\"src1\"},{\"value\":\"src2\"}]"));
            Assert.That(row1["metadata"], Is.EqualTo("{\"priority\":5,\"flags\":[\"a\",\"b\"]}"));
            Assert.That(row1["enabled"], Is.EqualTo("true"));
            Assert.That(row1["threshold"], Is.EqualTo("12.5"));

            Dictionary<string, string> row2 = tableData.Rows[1];
            Assert.That(row2["decision"], Is.EqualTo("deny"));
            Assert.That(row2["destinations"], Is.EqualTo("[\"dest1\"]"));
            Assert.That(row2["services"], Is.EqualTo("[{\"name\":\"http\"}]"));
            Assert.That(row2["meta"], Is.EqualTo("{\"last\":\"2024-01-02\"}"));
            Assert.That(row2["extra"], Is.EqualTo(""));
        }
    }
}
