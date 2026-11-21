using FWO.Logging;

namespace FWO.Api.Client.Queries
{
    /// <summary>
    /// API calls for generic (non-firewall) config items and their recertification.
    /// </summary>
    public class GenericQueries : Queries
    {
        public static readonly string genericConfigItemDetailsFragment;

        public static readonly string getGenericKeyConfigs;
        public static readonly string getGenericManagements;
        public static readonly string getGenericConfigItems;
        public static readonly string getGenericConfigItemsOfOwners;
        public static readonly string getGenericRecertifications;
        public static readonly string addGenericRecertifications;
        public static readonly string addGenericOwnerRecertification;

        static GenericQueries()
        {
            try
            {
                genericConfigItemDetailsFragment = GetQueryText("generic/fragments/genericConfigItemDetails.graphql");

                getGenericKeyConfigs = GetQueryText("generic/getGenericKeyConfigs.graphql");
                getGenericManagements = GetQueryText("generic/getGenericManagements.graphql");
                getGenericConfigItems = genericConfigItemDetailsFragment + GetQueryText("generic/getGenericConfigItems.graphql");
                getGenericConfigItemsOfOwners = genericConfigItemDetailsFragment + GetQueryText("generic/getGenericConfigItemsOfOwners.graphql");
                getGenericRecertifications = GetQueryText("generic/getGenericRecertifications.graphql");
                addGenericRecertifications = GetQueryText("generic/addGenericRecertifications.graphql");
                addGenericOwnerRecertification = GetQueryText("generic/addGenericOwnerRecertification.graphql");
            }
            catch (Exception exception)
            {
                Log.WriteError("Initialize Api Queries", "Api Generic Queries could not be loaded.", exception);
#if RELEASE
                Environment.Exit(-1);
#else
                throw;
#endif
            }
        }
    }
}
