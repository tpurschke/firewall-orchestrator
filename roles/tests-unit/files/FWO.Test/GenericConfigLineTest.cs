using System.Text.Json;
using FWO.Data.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class GenericConfigLineTest
    {
        private const string kItemJson = """
            {"uid": "r1", "name": "Block", "enabled": true, "hits": 5, "tags": ["a", "b"],
             "meta": {"owner": "APP-1", "inner": {"deep": "x"}}, "empty": null}
            """;

        private static Dictionary<string, object?> NewtonsoftLine()
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object?>>(kItemJson)!;
        }

        private static Dictionary<string, object?> SystemTextJsonLine()
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(kItemJson)!;
        }

        private static IEnumerable<Func<Dictionary<string, object?>>> Deserializers()
        {
            yield return NewtonsoftLine;
            yield return SystemTextJsonLine;
        }

        [TestCaseSource(nameof(Deserializers))]
        public void GetDisplayValue_ReadsScalarsNestedValuesAndLists(Func<Dictionary<string, object?>> deserialize)
        {
            Dictionary<string, object?> line = deserialize();

            Assert.Multiple(() =>
            {
                Assert.That(GenericConfigLine.GetDisplayValue(line, "name"), Is.EqualTo("Block"));
                Assert.That(GenericConfigLine.GetDisplayValue(line, "enabled"), Is.EqualTo("true").IgnoreCase);
                Assert.That(GenericConfigLine.GetDisplayValue(line, "hits"), Is.EqualTo("5"));
                Assert.That(GenericConfigLine.GetDisplayValue(line, "tags"), Is.EqualTo("a, b"));
                Assert.That(GenericConfigLine.GetDisplayValue(line, "meta.owner"), Is.EqualTo("APP-1"));
                Assert.That(GenericConfigLine.GetDisplayValue(line, "meta.inner.deep"), Is.EqualTo("x"));
                Assert.That(GenericConfigLine.GetDisplayValue(line, "empty"), Is.EqualTo(""));
            });
        }

        [TestCaseSource(nameof(Deserializers))]
        public void GetValue_MissingPathsGiveNull(Func<Dictionary<string, object?>> deserialize)
        {
            Dictionary<string, object?> line = deserialize();

            Assert.Multiple(() =>
            {
                Assert.That(GenericConfigLine.GetValue(line, null), Is.Null);
                Assert.That(GenericConfigLine.GetValue(line, ""), Is.Null);
                Assert.That(GenericConfigLine.GetValue(line, "missing"), Is.Null);
                Assert.That(GenericConfigLine.GetValue(line, "meta.missing"), Is.Null);
                Assert.That(GenericConfigLine.GetValue(line, "name.below.scalar"), Is.Null);
                Assert.That(GenericConfigLine.GetValue(line, string.Join(".", Enumerable.Repeat("meta", 101))), Is.Null);
            });
        }

        [Test]
        public void GetDisplayValue_ShowsObjectsAsCompactJson()
        {
            Assert.That(GenericConfigLine.GetDisplayValue(NewtonsoftLine(), "meta.inner"), Is.EqualTo("{\"deep\":\"x\"}"));
            Assert.That(GenericConfigLine.GetDisplayValue(SystemTextJsonLine(), "meta.inner").Replace(" ", ""), Is.EqualTo("{\"deep\":\"x\"}"));
        }

        [Test]
        public void ToDisplayString_HandlesPlainDotNetValues()
        {
            Dictionary<string, object?> nested = new() { ["k"] = "v" };
            Assert.Multiple(() =>
            {
                Assert.That(GenericConfigLine.ToDisplayString(null), Is.EqualTo(""));
                Assert.That(GenericConfigLine.ToDisplayString(false), Is.EqualTo("false"));
                Assert.That(GenericConfigLine.ToDisplayString(1.5), Is.EqualTo("1.5"));
                Assert.That(GenericConfigLine.ToDisplayString(new List<object?> { "x", 2 }), Is.EqualTo("x, 2"));
                Assert.That(GenericConfigLine.ToDisplayString(nested), Is.EqualTo("{\"k\":\"v\"}"));
                Assert.That(GenericConfigLine.ToDisplayString(new JValue((object?)null)), Is.EqualTo(""));
                Assert.That(GenericConfigLine.GetValue(new Dictionary<string, object?> { ["a"] = nested }, "a.k"), Is.EqualTo("v"));
            });
        }

        [Test]
        public void ToIndentedJson_ContainsAllValues()
        {
            string json = GenericConfigLine.ToIndentedJson(NewtonsoftLine());

            Assert.That(JObject.Parse(json)["meta"]!["owner"]!.ToString(), Is.EqualTo("APP-1"));
            Assert.That(json, Does.Contain(Environment.NewLine));
        }
    }
}
