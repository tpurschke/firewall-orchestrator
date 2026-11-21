namespace FWO.Data.Generic
{
    /// <summary>
    /// Recertification state of generic config items.
    /// A decision applies to the item (management and uid), not only to the version it was made for:
    /// the latest decision gives the next due date; an item without any decision is due the initial
    /// recertification period after it was first seen.
    /// </summary>
    public static class GenericRecertCalculator
    {
        /// <summary>
        /// Sets <see cref="GenericConfigItem.LastRecertification"/> and <see cref="GenericConfigItem.NextRecertDate"/>
        /// of all items from the given recertification decisions.
        /// </summary>
        public static void ApplyRecertifications(IEnumerable<GenericConfigItem> items, IEnumerable<GenericRecertification> recertifications, int initialRecertPeriodDays)
        {
            Dictionary<(int, string), GenericRecertification> latestDecisions = [];
            foreach (GenericRecertification recertification in recertifications)
            {
                if (recertification.ConfigItem == null)
                {
                    continue;
                }
                (int, string) itemKey = (recertification.ConfigItem.MgmId, recertification.ConfigItem.ItemUid);
                if (!latestDecisions.TryGetValue(itemKey, out GenericRecertification? latest) || IsLater(recertification, latest))
                {
                    latestDecisions[itemKey] = recertification;
                }
            }

            foreach (GenericConfigItem item in items)
            {
                latestDecisions.TryGetValue((item.MgmId, item.ItemUid), out GenericRecertification? latest);
                item.LastRecertification = latest;
                item.NextRecertDate = CalcDueDate(item.FirstSeen, latest, initialRecertPeriodDays);
            }
        }

        /// <summary>
        /// Due date of an item: from the latest decision, or the initial period after first seen.
        /// Decertified items are not due any more.
        /// </summary>
        public static DateTime? CalcDueDate(DateTime firstSeen, GenericRecertification? latestDecision, int initialRecertPeriodDays)
        {
            if (latestDecision == null)
            {
                return firstSeen.AddDays(initialRecertPeriodDays);
            }
            return latestDecision.Recertified ? latestDecision.NextRecertDate : null;
        }

        /// <summary>
        /// Next due date after a recertification today: the owner's interval, or the general recertification period.
        /// </summary>
        public static DateTime CalcNextRecertDate(DateTime recertDate, int? ownerRecertInterval, int recertPeriodDays)
        {
            int intervalDays = ownerRecertInterval is > 0 ? ownerRecertInterval.Value : recertPeriodDays;
            return recertDate.AddDays(intervalDays);
        }

        /// <summary>
        /// Whether an item is due within the given number of days (overdue items included).
        /// </summary>
        public static bool IsDueWithin(GenericConfigItem item, DateTime now, int lookAheadDays)
        {
            return item.NextRecertDate != null && item.NextRecertDate.Value <= now.AddDays(lookAheadDays);
        }

        /// <summary>
        /// Whether an item counts as unused: last used before the cut date, or never used and first seen
        /// before the creation tolerance date.
        /// </summary>
        public static bool IsUnused(GenericConfigItem item, DateTime cutDate, DateTime creationToleranceDate)
        {
            return item.LastUsed != null ? item.LastUsed.Value <= cutDate : item.FirstSeen <= creationToleranceDate;
        }

        private static bool IsLater(GenericRecertification candidate, GenericRecertification current)
        {
            return candidate.RecertDate > current.RecertDate || (candidate.RecertDate == current.RecertDate && candidate.Id > current.Id);
        }
    }
}
