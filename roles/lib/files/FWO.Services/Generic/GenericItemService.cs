using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics.Exceptions;
using FWO.Data.Generic;

namespace FWO.Services.Generic
{
    /// <summary>
    /// Reads generic (non-firewall) config items, their device type key configuration and recertifications.
    /// All lists are read in pages and capped, so a huge rule base cannot exhaust memory.
    /// </summary>
    public class GenericItemService(ApiConnection apiConnection)
    {
        /// <summary>
        /// Elements per API call when no other page size is given.
        /// </summary>
        public const int kDefaultPageSize = 1000;

        /// <summary>
        /// Maximum number of elements read for one list.
        /// </summary>
        public const int kMaxElements = 200000;

        /// <summary>
        /// Upper bound of a requested page size.
        /// </summary>
        public const int kMaxPageSize = 10000;

        private readonly ApiConnection apiConnection = apiConnection;

        /// <summary>
        /// Key configurations of all generic device types.
        /// </summary>
        public async Task<List<GenericKeyConfig>> GetKeyConfigs()
        {
            return await apiConnection.SendQueryAsync<List<GenericKeyConfig>>(GenericQueries.getGenericKeyConfigs);
        }

        /// <summary>
        /// Managements of generic device types the user may see.
        /// </summary>
        public async Task<List<GenericItemManagement>> GetGenericManagements(List<GenericKeyConfig> keyConfigs)
        {
            if (keyConfigs.Count == 0)
            {
                return [];
            }
            List<int> devTypIds = [.. keyConfigs.Select(keyConfig => keyConfig.DevTypId)];
            return await apiConnection.SendQueryAsync<List<GenericItemManagement>>(GenericQueries.getGenericManagements, new { devTypIds });
        }

        /// <summary>
        /// Active config items of the given managements, optionally restricted to some owners.
        /// </summary>
        public async Task<List<GenericConfigItem>> GetItems(List<int> mgmIds, List<int>? ownerIds = null, int pageSize = kDefaultPageSize)
        {
            if (mgmIds.Count == 0 || ownerIds is { Count: 0 })
            {
                return [];
            }
            return ownerIds == null
                ? await FetchAll<GenericConfigItem>(GenericQueries.getGenericConfigItems, pageSize,
                    (limit, offset) => new { mgmIds, limit, offset })
                : await FetchAll<GenericConfigItem>(GenericQueries.getGenericConfigItemsOfOwners, pageSize,
                    (limit, offset) => new { mgmIds, ownerIds, limit, offset });
        }

        /// <summary>
        /// All recertification decisions for items of the given managements, latest first.
        /// </summary>
        public async Task<List<GenericRecertification>> GetRecertifications(List<int> mgmIds, int pageSize = kDefaultPageSize)
        {
            if (mgmIds.Count == 0)
            {
                return [];
            }
            return await FetchAll<GenericRecertification>(GenericQueries.getGenericRecertifications, pageSize,
                (limit, offset) => new { mgmIds, limit, offset });
        }

        /// <summary>
        /// Active config items with their latest recertification decision and due date.
        /// </summary>
        public async Task<List<GenericConfigItem>> GetItemsWithRecertState(List<int> mgmIds, List<int>? ownerIds, int initialRecertPeriodDays, int pageSize = kDefaultPageSize)
        {
            List<GenericConfigItem> items = await GetItems(mgmIds, ownerIds, pageSize);
            if (items.Count > 0)
            {
                List<int> itemMgmIds = [.. items.Select(item => item.MgmId).Distinct()];
                List<GenericRecertification> recertifications = await GetRecertifications(itemMgmIds, pageSize);
                GenericRecertCalculator.ApplyRecertifications(items, recertifications, initialRecertPeriodDays);
            }
            return items;
        }

        private async Task<List<T>> FetchAll<T>(string query, int pageSize, Func<int, int, object> buildVariables)
        {
            int limit = Math.Clamp(pageSize, 1, kMaxPageSize);
            List<T> result = [];
            int offset = 0;
            while (true)
            {
                List<T> page = await apiConnection.SendQueryAsync<List<T>>(query, buildVariables(limit, offset));
                result.AddRange(page);
                if (page.Count < limit)
                {
                    return result;
                }
                if (result.Count >= kMaxElements)
                {
                    throw new CollectionLimitExceededException(kMaxElements);
                }
                offset += limit;
            }
        }
    }
}
