using System.IO;
using System.Net.Http;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    internal class GenericGatewayImporterFileImportTest
    {
        [Test]
        public async Task ImportProxyRulesAsync_ReadsRulesFromJsonFile()
        {
            Assembly? importerAssembly = TryLoadGenericGatewayImporterAssembly();
            if (importerAssembly == null)
            {
                Assert.Ignore("Proxy importer assembly not available in this test environment.");
                return;
            }

            string rulesPath = GetSampleRulesPath();
            Assert.That(File.Exists(rulesPath), Is.True, $"Missing sample rules file at {rulesPath}");

            object options = CreateSkyhighOptions(importerAssembly, rulesPath);

            object importer = CreateImporter(importerAssembly, options);
            List<object> rules = await InvokeImportAsync(importer);

            Assert.That(rules.Count, Is.EqualTo(2));
            Assert.That(GetPropertyValue<int>(rules[0], "ManagementId"), Is.EqualTo(42));
            Assert.That(GetPropertyValue<string>(rules[0], "Name"), Is.EqualTo("Allow Web"));
            Assert.That(GetPropertyValue<string>(rules[0], "Action"), Is.EqualTo("allow"));
            Assert.That(GetPropertyValue<List<string>>(rules[0], "Sources"), Is.EquivalentTo(new[] { "any" }));
            Assert.That(GetPropertyValue<List<string>>(rules[0], "Destinations"), Is.EquivalentTo(new[] { "example.com", "api.example.com" }));
            Assert.That(GetPropertyValue<List<string>>(rules[0], "Services"), Is.EquivalentTo(new[] { "https" }));
            Assert.That(GetPropertyValue<string>(rules[0], "OwnerName"), Is.EqualTo("demo-owner"));

            Assert.That(GetPropertyValue<int>(rules[1], "ManagementId"), Is.EqualTo(42));
            Assert.That(GetPropertyValue<string>(rules[1], "Name"), Is.EqualTo("Block Social"));
            Assert.That(GetPropertyValue<string>(rules[1], "Action"), Is.EqualTo("deny"));
            Assert.That(GetPropertyValue<List<string>>(rules[1], "Sources"), Is.EquivalentTo(new[] { "branch-office" }));
            Assert.That(GetPropertyValue<List<string>>(rules[1], "Destinations"), Is.EquivalentTo(new[] { "facebook.com" }));
            Assert.That(GetPropertyValue<List<string>>(rules[1], "Services"), Is.EquivalentTo(new[] { "http", "https" }));
        }

        private static string GetSampleRulesPath()
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "..",
                "sample-data",
                "files",
                "sample-configs",
                "proxy",
                "proxy-rules.json"));
        }

        private static Assembly? TryLoadGenericGatewayImporterAssembly()
        {
            try
            {
                return Assembly.Load("FWO.GenericGatewayImporter");
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static object CreateSkyhighOptions(Assembly importerAssembly, string rulesPath)
        {
            Type optionsType = importerAssembly.GetType("FWO.GenericGatewayImporter.Models.SkyhighOptions", throwOnError: true)!;
            object options = Activator.CreateInstance(optionsType)!;
            PropertyInfo rulesPathProperty = optionsType.GetProperty("RulesJsonPath")!;
            rulesPathProperty.SetValue(options, rulesPath);
            return options;
        }

        private static object CreateImporter(Assembly importerAssembly, object options)
        {
            Type importerType = importerAssembly.GetType("FWO.GenericGatewayImporter.Importers.SkyhighGenericGatewayImporter", throwOnError: true)!;
            Type optionsType = options.GetType();
            MethodInfo createMethod = typeof(Options)
                .GetMethod(nameof(Options.Create), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(optionsType);
            object optionsWrapper = createMethod.Invoke(null, new[] { options })!;
            return Activator.CreateInstance(importerType, new HttpClient(), optionsWrapper)!;
        }

        private static async Task<List<object>> InvokeImportAsync(object importer)
        {
            MethodInfo method = importer.GetType().GetMethod("ImportProxyRulesAsync")!;
            Task task = (Task)method.Invoke(importer, new object[] { 42, CancellationToken.None })!;
            await task.ConfigureAwait(false);
            object? result = task.GetType().GetProperty("Result")!.GetValue(task);
            return ((IEnumerable<object>)result!).ToList();
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(propertyName)!;
            return (T)property.GetValue(instance)!;
        }
    }
}
