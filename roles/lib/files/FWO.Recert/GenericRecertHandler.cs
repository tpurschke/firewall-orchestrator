using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Config.Api;
using FWO.Data;
using FWO.Data.Generic;
using FWO.Logging;

namespace FWO.Recert
{
    /// <summary>
    /// Records recertification decisions for generic (non-firewall) config items.
    /// Decisions are only added, never changed: the latest one per item counts.
    /// The API only accepts decisions for items of the user's recertifiable owners made under the user's own DN.
    /// </summary>
    public class GenericRecertHandler(ApiConnection apiConnection, UserConfig userConfig)
    {
        private const string kLogTitle = "Generic Recertification";

        /// <summary>
        /// Maximum number of decisions written in one API call.
        /// </summary>
        public const int kMaxDecisionsPerCall = 1000;

        /// <summary>
        /// Records the selected decision (recertify or decertify) of each item; items without selection are skipped.
        /// </summary>
        /// <returns>Number of recorded decisions.</returns>
        public async Task<int> RecertifyItems(IEnumerable<GenericConfigItem> items, string? comment, DateTime recertDate)
        {
            List<object> decisions = [.. items
                .Where(item => item.SelectedAction != GenericRecertAction.None)
                .Select(item => BuildDecision(item, item.SelectedAction == GenericRecertAction.Recertify, comment, recertDate, null))];
            return await WriteDecisions(decisions);
        }

        /// <summary>
        /// Recertifies an owner and all given generic config items of this owner in one step.
        /// </summary>
        /// <returns>Id of the owner recertification, or null if it could not be written.</returns>
        public async Task<long?> RecertifyOwner(GenericItemOwner owner, List<GenericConfigItem> itemsOfOwner, string? comment, DateTime recertDate)
        {
            DateTime nextRecertDate = GenericRecertCalculator.CalcNextRecertDate(recertDate, owner.RecertInterval, userConfig.RecertificationPeriod);
            var ownerVariables = new
            {
                ownerId = owner.Id,
                userDn = userConfig.User.Dn,
                recertified = true,
                comment,
                nextRecertDate
            };
            ReturnId ownerRecert = await apiConnection.SendQueryAsync<ReturnId>(GenericQueries.addGenericOwnerRecertification, ownerVariables);
            if (ownerRecert.Id == null)
            {
                Log.WriteWarning(kLogTitle, $"Owner recertification of owner {owner.Id} was not written.");
                return null;
            }
            List<object> decisions = [.. itemsOfOwner
                .Where(item => item.OwnerId == owner.Id)
                .Select(item => BuildDecision(item, true, comment, recertDate, ownerRecert.Id))];
            await WriteDecisions(decisions);
            return ownerRecert.Id;
        }

        private object BuildDecision(GenericConfigItem item, bool recertified, string? comment, DateTime recertDate, long? ownerRecertId)
        {
            return new
            {
                config_item_id = item.Id,
                owner_id = item.OwnerId,
                user_dn = userConfig.User.Dn,
                recertified,
                comment,
                next_recert_date = recertified
                    ? GenericRecertCalculator.CalcNextRecertDate(recertDate, item.Owner?.RecertInterval, userConfig.RecertificationPeriod)
                    : (DateTime?)null,
                owner_recert_id = ownerRecertId
            };
        }

        private async Task<int> WriteDecisions(List<object> decisions)
        {
            int written = 0;
            foreach (object[] chunk in decisions.Chunk(kMaxDecisionsPerCall))
            {
                ReturnId result = await apiConnection.SendQueryAsync<ReturnId>(GenericQueries.addGenericRecertifications, new { recertifications = chunk });
                written += result.AffectedRows;
            }
            Log.WriteDebug(kLogTitle, $"{written} of {decisions.Count} decisions written.");
            return written;
        }
    }
}
