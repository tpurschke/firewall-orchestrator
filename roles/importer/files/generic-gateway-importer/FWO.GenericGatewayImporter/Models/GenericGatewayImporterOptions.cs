namespace FWO.GenericGatewayImporter.Models
{
    public class GenericGatewayImporterOptions
    {
        public string ConnectionString { get; set; } = "";
        public string Schema { get; set; } = "proxy";
        public int DefaultRecertificationDays { get; set; } = 365;
    }
}
