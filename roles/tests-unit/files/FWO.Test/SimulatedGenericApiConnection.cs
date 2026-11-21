using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Data;
using FWO.Data.Generic;
using GraphQL.Client.Http;

namespace FWO.Test
{
    /// <summary>
    /// Simulated API for generic config items: answers the generic queries from in-memory lists (with paging)
    /// and records every call with its variables.
    /// </summary>
    internal class SimulatedGenericApiConnection : SimulatedApiConnection
    {
        public List<GenericKeyConfig> KeyConfigs { get; set; } = [];
        public List<GenericItemManagement> Managements { get; set; } = [];
        public List<GenericConfigItem> Items { get; set; } = [];
        public List<GenericRecertification> Recertifications { get; set; } = [];
        public long? OwnerRecertId { get; set; } = 77;
        public List<(string Query, object? Variables)> Calls { get; } = [];

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            Calls.Add((query, variables));
            object result = Answer(query, variables);
            return Task.FromResult((QueryResponseType)result);
        }

        public List<object?> VariablesOf(string query)
        {
            return [.. Calls.Where(call => call.Query == query).Select(call => call.Variables)];
        }

        public static T Read<T>(object? variables, string name)
        {
            return (T)variables!.GetType().GetProperty(name)!.GetValue(variables)!;
        }

        private object Answer(string query, object? variables)
        {
            if (query == GenericQueries.getGenericKeyConfigs)
            {
                return KeyConfigs;
            }
            if (query == GenericQueries.getGenericManagements)
            {
                List<int> devTypIds = Read<List<int>>(variables, "devTypIds");
                return Managements.Where(management => devTypIds.Contains(management.DevTypId)).ToList();
            }
            if (query == GenericQueries.getGenericConfigItems || query == GenericQueries.getGenericConfigItemsOfOwners)
            {
                List<int> mgmIds = Read<List<int>>(variables, "mgmIds");
                List<int>? ownerIds = query == GenericQueries.getGenericConfigItemsOfOwners ? Read<List<int>>(variables, "ownerIds") : null;
                return Page(Items.Where(item => mgmIds.Contains(item.MgmId) && (ownerIds == null || (item.OwnerId != null && ownerIds.Contains(item.OwnerId.Value)))), variables);
            }
            if (query == GenericQueries.getGenericRecertifications)
            {
                List<int> mgmIds = Read<List<int>>(variables, "mgmIds");
                return Page(Recertifications.Where(recert => recert.ConfigItem != null && mgmIds.Contains(recert.ConfigItem.MgmId)), variables);
            }
            if (query == GenericQueries.addGenericRecertifications)
            {
                object[] recertifications = Read<object[]>(variables, "recertifications");
                return new ReturnId { AffectedRows = recertifications.Length };
            }
            if (query == GenericQueries.addGenericOwnerRecertification)
            {
                return new ReturnId { Id = OwnerRecertId };
            }
            throw new NotImplementedException(query);
        }

        private static List<T> Page<T>(IEnumerable<T> elements, object? variables)
        {
            int limit = Read<int>(variables, "limit");
            int offset = Read<int>(variables, "offset");
            return [.. elements.Skip(offset).Take(limit)];
        }
    }
}
