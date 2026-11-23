using FWO.Api.Client.Queries;
using FWO.Basics.Exceptions;
using FWO.Data.Generic;
using FWO.Services.Generic;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class GenericItemServiceTest
    {
        private static SimulatedGenericApiConnection CreateApi(int itemCount = 5)
        {
            SimulatedGenericApiConnection api = new()
            {
                KeyConfigs = [new GenericKeyConfig { DevTypId = 33 }],
                Managements = [new GenericItemManagement { Id = 1, Name = "proxy", DevTypId = 33 }, new GenericItemManagement { Id = 2, Name = "fw", DevTypId = 9 }]
            };
            for (int index = 0; index < itemCount; index++)
            {
                api.Items.Add(new GenericConfigItem { Id = index, MgmId = 1, ItemUid = $"r{index}", OwnerId = index % 2, FirstSeen = new DateTime(2026, 1, 1) });
            }
            return api;
        }

        [Test]
        public async Task GetGenericManagements_ReturnsManagementsOfGenericDeviceTypes()
        {
            SimulatedGenericApiConnection api = CreateApi();
            GenericItemService service = new(api);

            List<GenericItemManagement> managements = await service.GetGenericManagements(await service.GetKeyConfigs());

            Assert.That(managements.Select(management => management.Id), Is.EqualTo([1]));
            Assert.That(await service.GetGenericManagements([]), Is.Empty);
        }

        [Test]
        public async Task GetItems_ReadsAllPages()
        {
            SimulatedGenericApiConnection api = CreateApi(5);

            List<GenericConfigItem> items = await new GenericItemService(api).GetItems([1], null, 2);

            Assert.Multiple(() =>
            {
                Assert.That(items, Has.Count.EqualTo(5));
                Assert.That(api.VariablesOf(GenericQueries.getGenericConfigItems), Has.Count.EqualTo(3));
            });
        }

        [Test]
        public async Task GetItems_OfOwnersUsesOwnerQuery()
        {
            SimulatedGenericApiConnection api = CreateApi(5);

            List<GenericConfigItem> items = await new GenericItemService(api).GetItems([1], [1]);

            Assert.Multiple(() =>
            {
                Assert.That(items.Select(item => item.OwnerId).Distinct(), Is.EqualTo([1]));
                Assert.That(api.VariablesOf(GenericQueries.getGenericConfigItemsOfOwners), Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task GetItems_WithoutManagementsOrOwnersAsksNothing()
        {
            SimulatedGenericApiConnection api = CreateApi();
            GenericItemService service = new(api);

            Assert.That(await service.GetItems([]), Is.Empty);
            Assert.That(await service.GetItems([1], []), Is.Empty);
            Assert.That(await service.GetRecertifications([]), Is.Empty);
            Assert.That(api.Calls, Is.Empty);
        }

        [Test]
        public void GetItems_StopsAtTheElementLimit()
        {
            SimulatedGenericApiConnection api = CreateApi(GenericItemService.kMaxElements + 1);

            Assert.ThrowsAsync<CollectionLimitExceededException>(async () =>
                await new GenericItemService(api).GetItems([1], null, GenericItemService.kMaxPageSize));
        }

        [Test]
        public async Task GetItemsWithRecertState_AppliesDecisions()
        {
            SimulatedGenericApiConnection api = CreateApi(2);
            api.Recertifications =
            [
                new GenericRecertification
                {
                    Id = 1, Recertified = true, RecertDate = new DateTime(2026, 2, 1), NextRecertDate = new DateTime(2027, 2, 1),
                    ConfigItem = new GenericItemRef { MgmId = 1, ItemUid = "r0" }
                }
            ];

            List<GenericConfigItem> items = await new GenericItemService(api).GetItemsWithRecertState([1], null, 10);

            Assert.Multiple(() =>
            {
                Assert.That(items[0].NextRecertDate, Is.EqualTo(new DateTime(2027, 2, 1)));
                Assert.That(items[1].NextRecertDate, Is.EqualTo(new DateTime(2026, 1, 11)));
            });
        }

        [Test]
        public async Task GetItemsWithRecertState_SkipsRecertificationsWithoutItems()
        {
            SimulatedGenericApiConnection api = CreateApi(0);

            Assert.That(await new GenericItemService(api).GetItemsWithRecertState([1], null, 10), Is.Empty);
            Assert.That(api.VariablesOf(GenericQueries.getGenericRecertifications), Is.Empty);
        }
    }
}
