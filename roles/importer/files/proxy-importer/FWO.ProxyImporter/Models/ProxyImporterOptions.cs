namespace FWO.ProxyImporter.Models
{
    public class ProxyImporterOptions
    {
        public string ConnectionString { get; set; } = "";
        public string Schema { get; set; } = "proxy";
        public int DefaultRecertificationDays { get; set; } = 365;
    }
}
