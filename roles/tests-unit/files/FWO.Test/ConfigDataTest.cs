using FWO.Basics;
using FWO.Config.Api.Data;
using NUnit.Framework;
using System.Text.Json;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class ConfigDataTest
    {
        [Test]
        public void IsModuleAvailable_EmptySettingOffersAllModules()
        {
            ConfigData configData = new() { AvailableModules = "" };

            Assert.That(Enum.GetValues<Module>().All(configData.IsModuleAvailable), Is.True);
        }

        [Test]
        public void IsModuleAvailable_OffersOnlyListedModules()
        {
            ConfigData configData = new() { AvailableModules = "[1,3]" };

            Assert.Multiple(() =>
            {
                Assert.That(configData.IsModuleAvailable(Module.Report), Is.True);
                Assert.That(configData.IsModuleAvailable(Module.Recertification), Is.True);
                Assert.That(configData.IsModuleAvailable(Module.GenericRecertification), Is.False);
            });
        }

        [Test]
        public void IsModuleAvailable_EmptyListOffersNoModule()
        {
            ConfigData configData = new() { AvailableModules = "[]" };

            Assert.That(Enum.GetValues<Module>().Any(configData.IsModuleAvailable), Is.False);
        }

        [TestCase("no list")]
        [TestCase("null")]
        public void IsModuleAvailable_RejectsInvalidSetting(string availableModules)
        {
            ConfigData configData = new() { AvailableModules = availableModules };

            Assert.That(() => configData.IsModuleAvailable(Module.Report), Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void AllModulesNumList_ContainsGenericRecertification()
        {
            List<Module>? modules = JsonSerializer.Deserialize<List<Module>>(ModuleGroups.AllModulesNumList());

            Assert.That(modules, Does.Contain(Module.GenericRecertification));
        }
    }
}
