using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data;
using FWO.Logging;
using FWO.Services;
using System.Text.Json;

namespace FWO.Middleware.Server
{
    /// <summary>
    /// Imports converted TSQ JSON path-analysis data into the middleware process snapshot.
    /// </summary>
    public class PathAnalysisDataImport(ApiConnection apiConnection, GlobalConfig globalConfig) : DataImportBase(apiConnection, globalConfig)
    {
        private const string LogMessageTitle = "Import Path Analysis Data";
        private const string LevelFile = "Import File";

        /// <summary>
        /// Loads configured converted TSQ JSON files and replaces the active path-analysis snapshot.
        /// </summary>
        public async Task<List<string>> Run()
        {
            List<string> importfilePathAndNames = JsonSerializer.Deserialize<List<string>>(globalConfig.ImportPathAnalysisDataPath)
                ?? throw new JsonException("Config Data could not be deserialized.");
            List<string> failedImports = [];
            List<PathAnalysisTable> importedTables = [];

            foreach (string importfilePathAndName in importfilePathAndNames)
            {
                await ImportSingleSource(importfilePathAndName, importedTables, failedImports);
            }

            if (importedTables.Count > 0)
            {
                PathAnalysisTableStore.Replace(PathAnalysisTable.Merge(importedTables));
                string messageText = $"Imported {importedTables.Sum(table => table.Entries.Count)} path-analysis entries from {importedTables.Count} JSON file(s).";
                Log.WriteInfo(LogMessageTitle, messageText);
                await AddLogEntry(GlobalConst.kImportPathAnalysisData, 0, LevelFile, messageText);
            }
            return failedImports;
        }

        /// <summary>
        /// Imports converted path-analysis JSON data and replaces the active snapshot.
        /// </summary>
        public async Task<PathAnalysisImportResult> Import(PathAnalysisImportParameters importParameters)
        {
            PathAnalysisTable table = PathAnalysisTable.FromImportData(importParameters);
            table = await MapGatewaysToDevices(table, importParameters.SourceName);
            PathAnalysisTableStore.Replace(table);

            string messageText = $"Imported {table.Entries.Count} path-analysis entries from {importParameters.SourceName}.";
            Log.WriteInfo(LogMessageTitle, messageText);
            await AddLogEntry(GlobalConst.kImportPathAnalysisData, 0, LevelFile, messageText);
            return new() { ImportedEntries = table.Entries.Count, MappedGateways = table.GatewayNames.Count };
        }

        private async Task ImportSingleSource(string importfilePathAndName, List<PathAnalysisTable> importedTables, List<string> failedImports)
        {
            string importSourcePath = ImportPathPolicy.RemoveAllowedExtension(importfilePathAndName);
            try
            {
                List<string> validatedImportFiles = ValidateConfiguredImportSource(importSourcePath);
                string scriptPath = importSourcePath + ".py";
                if (validatedImportFiles.Contains(scriptPath) && !RunImportScript(scriptPath, null))
                {
                    Log.WriteInfo(LogMessageTitle, $"Script {scriptPath} failed but trying to import from existing file.");
                }

                string jsonPath = importSourcePath + ".json";
                if (!validatedImportFiles.Contains(jsonPath))
                {
                    throw new FileNotFoundException($"Converted path-analysis import source '{jsonPath}' does not exist.");
                }

                ReadFile(jsonPath);
                PathAnalysisImportParameters importParameters = JsonSerializer.Deserialize<PathAnalysisImportParameters>(importFile)
                    ?? throw new JsonException("Path-analysis import JSON could not be deserialized.");
                importParameters.SourceName = string.IsNullOrWhiteSpace(importParameters.SourceName) ? jsonPath : importParameters.SourceName;
                importedTables.Add(await MapGatewaysToDevices(PathAnalysisTable.FromImportData(importParameters), importParameters.SourceName));
            }
            catch (Exception exception)
            {
                string errorText = $"Import from converted file {importSourcePath}.json could not be processed.";
                Log.WriteError(LogMessageTitle, errorText, exception);
                await AddLogEntry(GlobalConst.kImportPathAnalysisData, 2, LevelFile, errorText);
                failedImports.Add(importSourcePath);
            }
        }

        private async Task<PathAnalysisTable> MapGatewaysToDevices(PathAnalysisTable table, string sourceName)
        {
            List<Device> devices = await apiConnection.SendQueryAsync<List<Device>>(DeviceQueries.getDeviceDetails);
            Dictionary<string, int> deviceIdsByName = devices
                .Where(device => !string.IsNullOrWhiteSpace(device.Name))
                .GroupBy(device => device.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

            List<string> missingGateways = [.. table.GatewayNames.Where(gateway => !deviceIdsByName.ContainsKey(gateway))];
            if (missingGateways.Count > 0)
            {
                string description = $"Path-analysis import '{sourceName}' contains gateway name(s) not found in public.device: {string.Join(", ", missingGateways)}.";
                Log.WriteError(LogMessageTitle, description);
                await AddLogEntry(GlobalConst.kImportPathAnalysisData, 2, "Device Mapping", description);
                await AlertHelper.SetAlert(apiConnection, LogMessageTitle, description, GlobalConst.kImportPathAnalysisData,
                    AlertCode.ImportPathAnalysisData, new AlertHelper.AdditionalAlertData { JsonData = new { sourceName, missingGateways } });
                throw new PathAnalysisException(description);
            }
            return table.WithDeviceMappings(deviceIdsByName);
        }
    }
}
