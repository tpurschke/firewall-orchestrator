using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Data.Generic;

namespace FWO.Services.Generic
{
    /// <summary>
    /// Maintains which generic (non-firewall) device types the users of an owner responsible type may recertify.
    /// A responsible type without device types may not recertify any generic items.
    /// </summary>
    public class GenericRecertScopeService(ApiConnection apiConnection)
    {
        /// <summary>
        /// All device type assignments, grouped by owner responsible type id.
        /// </summary>
        public async Task<Dictionary<int, List<int>>> GetDevTypesByResponsibleType()
        {
            List<GenericRecertDevType> assignments = await apiConnection.SendQueryAsync<List<GenericRecertDevType>>(GenericQueries.getGenericRecertDevTypes);
            return assignments
                .GroupBy(assignment => assignment.OwnerResponsibleTypeId)
                .ToDictionary(group => group.Key, group => group.Select(assignment => assignment.DevTypId).Order().ToList());
        }

        /// <summary>
        /// Replaces the device types of an owner responsible type.
        /// </summary>
        public async Task SetDevTypes(int ownerResponsibleTypeId, IEnumerable<int> devTypIds)
        {
            List<object> devTypes = [.. devTypIds.Distinct().Select(devTypId => new { owner_responsible_type_id = ownerResponsibleTypeId, dev_typ_id = devTypId })];
            await apiConnection.SendQueryAsync<object>(GenericQueries.setGenericRecertDevTypes, new { ownerResponsibleTypeId, devTypes });
        }

        /// <summary>
        /// Display text of the device types assigned to a responsible type.
        /// </summary>
        public static string DisplayDevTypes(IEnumerable<int> devTypIds, IEnumerable<GenericKeyConfig> keyConfigs)
        {
            Dictionary<int, string> names = keyConfigs.ToDictionary(keyConfig => keyConfig.DevTypId, keyConfig => keyConfig.DeviceTypeName);
            return string.Join(", ", devTypIds.Select(devTypId => names.TryGetValue(devTypId, out string? name) ? name : devTypId.ToString()));
        }
    }
}
