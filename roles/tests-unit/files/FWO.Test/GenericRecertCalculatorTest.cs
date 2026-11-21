using FWO.Data.Generic;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class GenericRecertCalculatorTest
    {
        private static readonly DateTime kFirstSeen = new(2026, 1, 1);
        private static readonly DateTime kNow = new(2026, 6, 1);

        private static GenericConfigItem Item(int mgmId, string uid, DateTime? firstSeen = null)
        {
            return new GenericConfigItem { MgmId = mgmId, ItemUid = uid, FirstSeen = firstSeen ?? kFirstSeen };
        }

        private static GenericRecertification Decision(long id, int mgmId, string uid, DateTime date, bool recertified, DateTime? next)
        {
            return new GenericRecertification
            {
                Id = id,
                RecertDate = date,
                Recertified = recertified,
                NextRecertDate = next,
                ConfigItem = new GenericItemRef { MgmId = mgmId, ItemUid = uid }
            };
        }

        [Test]
        public void ApplyRecertifications_UsesLatestDecisionOfTheItemOverAllVersions()
        {
            GenericConfigItem item = Item(1, "r1");
            GenericConfigItem otherManagement = Item(2, "r1");
            List<GenericRecertification> decisions =
            [
                Decision(1, 1, "r1", new DateTime(2026, 2, 1), true, new DateTime(2027, 2, 1)),
                Decision(3, 1, "r1", new DateTime(2026, 3, 1), true, new DateTime(2027, 3, 1)),
                Decision(2, 1, "r1", new DateTime(2026, 3, 1), false, null),
                new GenericRecertification { Id = 9, RecertDate = new DateTime(2026, 5, 1) }
            ];

            GenericRecertCalculator.ApplyRecertifications([item, otherManagement], decisions, 30);

            Assert.Multiple(() =>
            {
                Assert.That(item.LastRecertification?.Id, Is.EqualTo(3));
                Assert.That(item.NextRecertDate, Is.EqualTo(new DateTime(2027, 3, 1)));
                Assert.That(item.IsDecertified, Is.False);
                Assert.That(otherManagement.LastRecertification, Is.Null);
                Assert.That(otherManagement.NextRecertDate, Is.EqualTo(kFirstSeen.AddDays(30)));
            });
        }

        [Test]
        public void ApplyRecertifications_DecertifiedItemIsNotDue()
        {
            GenericConfigItem item = Item(1, "r1");

            GenericRecertCalculator.ApplyRecertifications([item], [Decision(1, 1, "r1", kNow, false, null)], 30);

            Assert.Multiple(() =>
            {
                Assert.That(item.IsDecertified, Is.True);
                Assert.That(item.NextRecertDate, Is.Null);
                Assert.That(GenericRecertCalculator.IsDueWithin(item, kNow, 10000), Is.False);
            });
        }

        [TestCase(null, 365, 365)]
        [TestCase(0, 365, 365)]
        [TestCase(90, 365, 90)]
        public void CalcNextRecertDate_PrefersOwnerInterval(int? ownerInterval, int period, int expectedDays)
        {
            Assert.That(GenericRecertCalculator.CalcNextRecertDate(kNow, ownerInterval, period), Is.EqualTo(kNow.AddDays(expectedDays)));
        }

        [TestCase(-5, 0, true)]
        [TestCase(5, 0, false)]
        [TestCase(5, 5, true)]
        [TestCase(5, 4, false)]
        public void IsDueWithin_IncludesOverdueItems(int dueInDays, int lookAheadDays, bool expected)
        {
            GenericConfigItem item = new() { NextRecertDate = kNow.AddDays(dueInDays) };

            Assert.That(GenericRecertCalculator.IsDueWithin(item, kNow, lookAheadDays), Is.EqualTo(expected));
        }

        [Test]
        public void IsUnused_UsesLastUseOrFirstSeen()
        {
            DateTime cut = kNow.AddDays(-30);
            DateTime tolerance = kNow.AddDays(-10);

            Assert.Multiple(() =>
            {
                Assert.That(GenericRecertCalculator.IsUnused(new GenericConfigItem { LastUsed = kNow.AddDays(-40) }, cut, tolerance), Is.True);
                Assert.That(GenericRecertCalculator.IsUnused(new GenericConfigItem { LastUsed = kNow.AddDays(-1) }, cut, tolerance), Is.False);
                Assert.That(GenericRecertCalculator.IsUnused(new GenericConfigItem { FirstSeen = kNow.AddDays(-20) }, cut, tolerance), Is.True);
                Assert.That(GenericRecertCalculator.IsUnused(new GenericConfigItem { FirstSeen = kNow.AddDays(-5) }, cut, tolerance), Is.False);
            });
        }

        [Test]
        public void DisplayName_FallsBackToUid()
        {
            Assert.That(new GenericConfigItem { ItemUid = "u1" }.DisplayName, Is.EqualTo("u1"));
            Assert.That(new GenericConfigItem { ItemUid = "u1", ItemName = "n1" }.DisplayName, Is.EqualTo("n1"));
        }
    }
}
