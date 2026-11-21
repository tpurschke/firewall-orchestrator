using FWO.Api.Client.Queries;
using FWO.Data.Generic;
using FWO.Recert;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class GenericRecertHandlerTest
    {
        private static readonly DateTime kRecertDate = new(2026, 6, 1);
        private const string kUserDn = "cn=recertifier,dc=test";

        private static SimulatedUserConfig CreateUserConfig()
        {
            return new SimulatedUserConfig
            {
                RecertificationPeriod = 30,
                User = { Dn = kUserDn }
            };
        }

        private static GenericConfigItem Item(long id, int? ownerId, GenericRecertAction action, int? ownerInterval = null)
        {
            return new GenericConfigItem
            {
                Id = id,
                OwnerId = ownerId,
                SelectedAction = action,
                Owner = ownerId == null ? null : new GenericItemOwner { Id = ownerId.Value, RecertInterval = ownerInterval }
            };
        }

        private static object[] Decisions(SimulatedGenericApiConnection api)
        {
            return [.. api.VariablesOf(GenericQueries.addGenericRecertifications)
                .SelectMany(variables => SimulatedGenericApiConnection.Read<object[]>(variables, "recertifications"))];
        }

        [Test]
        public async Task RecertifyItems_WritesSelectedDecisionsOnly()
        {
            SimulatedGenericApiConnection api = new();
            GenericRecertHandler handler = new(api, CreateUserConfig());

            int written = await handler.RecertifyItems(
                [Item(1, 7, GenericRecertAction.Recertify, 90), Item(2, 7, GenericRecertAction.Decertify), Item(3, 7, GenericRecertAction.None)],
                "ok", kRecertDate);

            object[] decisions = Decisions(api);
            Assert.Multiple(() =>
            {
                Assert.That(written, Is.EqualTo(2));
                Assert.That(decisions, Has.Length.EqualTo(2));
                Assert.That(SimulatedGenericApiConnection.Read<long>(decisions[0], "config_item_id"), Is.EqualTo(1));
                Assert.That(SimulatedGenericApiConnection.Read<bool>(decisions[0], "recertified"), Is.True);
                Assert.That(SimulatedGenericApiConnection.Read<DateTime?>(decisions[0], "next_recert_date"), Is.EqualTo(kRecertDate.AddDays(90)));
                Assert.That(SimulatedGenericApiConnection.Read<string>(decisions[0], "user_dn"), Is.EqualTo(kUserDn));
                Assert.That(SimulatedGenericApiConnection.Read<bool>(decisions[1], "recertified"), Is.False);
                Assert.That(decisions[1].GetType().GetProperty("next_recert_date")!.GetValue(decisions[1]), Is.Null);
            });
        }

        [Test]
        public async Task RecertifyItems_WritesLargeSelectionsInChunks()
        {
            SimulatedGenericApiConnection api = new();
            List<GenericConfigItem> items = [.. Enumerable.Range(0, GenericRecertHandler.kMaxDecisionsPerCall + 1)
                .Select(index => Item(index, 7, GenericRecertAction.Recertify))];

            int written = await new GenericRecertHandler(api, CreateUserConfig()).RecertifyItems(items, null, kRecertDate);

            Assert.That(written, Is.EqualTo(GenericRecertHandler.kMaxDecisionsPerCall + 1));
            Assert.That(api.VariablesOf(GenericQueries.addGenericRecertifications), Has.Count.EqualTo(2));
        }

        [Test]
        public async Task RecertifyOwner_RecertifiesOwnerAndItsItems()
        {
            SimulatedGenericApiConnection api = new() { OwnerRecertId = 55 };
            GenericItemOwner owner = new() { Id = 7, RecertInterval = 60 };

            long? ownerRecertId = await new GenericRecertHandler(api, CreateUserConfig()).RecertifyOwner(owner,
                [Item(1, 7, GenericRecertAction.None), Item(2, 8, GenericRecertAction.None)], "all fine", kRecertDate);

            object? ownerVariables = api.VariablesOf(GenericQueries.addGenericOwnerRecertification).Single();
            object[] decisions = Decisions(api);
            Assert.Multiple(() =>
            {
                Assert.That(ownerRecertId, Is.EqualTo(55));
                Assert.That(SimulatedGenericApiConnection.Read<DateTime>(ownerVariables, "nextRecertDate"), Is.EqualTo(kRecertDate.AddDays(60)));
                Assert.That(SimulatedGenericApiConnection.Read<string>(ownerVariables, "userDn"), Is.EqualTo(kUserDn));
                Assert.That(decisions, Has.Length.EqualTo(1));
                Assert.That(SimulatedGenericApiConnection.Read<long>(decisions[0], "config_item_id"), Is.EqualTo(1));
                Assert.That(SimulatedGenericApiConnection.Read<long?>(decisions[0], "owner_recert_id"), Is.EqualTo(55));
            });
        }

        [Test]
        public async Task RecertifyOwner_WritesNoItemDecisionsWhenOwnerRecertFails()
        {
            SimulatedGenericApiConnection api = new() { OwnerRecertId = null };

            long? ownerRecertId = await new GenericRecertHandler(api, CreateUserConfig()).RecertifyOwner(new GenericItemOwner { Id = 7 },
                [Item(1, 7, GenericRecertAction.None)], null, kRecertDate);

            Assert.That(ownerRecertId, Is.Null);
            Assert.That(api.VariablesOf(GenericQueries.addGenericRecertifications), Is.Empty);
        }
    }
}
