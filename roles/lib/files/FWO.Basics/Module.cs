namespace FWO.Basics
{
    public enum Module
    {
        Report = 1,
        Workflow = 2,
        Recertification = 3,
        Modelling = 4,
        NetworkAnalysis = 5,
        Compliance = 6,

        /// <summary>
        /// Recertification of generic (non-firewall) config items: their section on the recertification page,
        /// their reports and their part of the daily recertification check.
        /// </summary>
        GenericRecertification = 7
    }

    public static class ModuleGroups
    {
        public static string AllModulesNumList()
        {
            return $"[{string.Join(",", Enum.GetValues<Module>().Select(m => (int)m).ToList())}]";
        }
    }
}
