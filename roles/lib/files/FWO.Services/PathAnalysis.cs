using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Data;
using FWO.Data.Flow;
using FWO.Data.Modelling;
using FWO.Data.Workflow;
using FWO.Logging;
using NetTools;
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace FWO.Services
{
    public enum PathAnalysisOptions
    {
        WriteToDeviceList,
        DisplayFoundDevices
    }

    public class PathAnalysisActionParams
    {
        [JsonProperty("option"), JsonPropertyName("option")]
        public PathAnalysisOptions Option { get; set; } = PathAnalysisOptions.DisplayFoundDevices;
    }

    public class PathAnalysisException : Exception
    {
        public PathAnalysisException(string message) : base(message)
        { }
    }

    public class PathAnalysisResult
    {
        public List<string> GatewayNames { get; set; } = [];
        public List<PathAnalysisPairResult> PairResults { get; set; } = [];
    }

    public class PathAnalysisPairResult
    {
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public List<string> GatewayNames { get; set; } = [];
    }

    /// <summary>
    /// Represents a parsed IPv4 or IPv6 address as an unsigned integer value tagged with its family.
    /// </summary>
    internal readonly record struct IpEndpoint(AddressFamily Family, BigInteger Value);

    public class PathAnalysisTableEntry
    {
        internal AddressFamily AddressFamily { get; init; }
        internal BigInteger NetworkStart { get; init; }
        internal BigInteger NetworkEnd { get; init; }

        public int LineNumber { get; init; }
        public string SourceName { get; init; } = "";
        public string Version { get; init; } = "";
        public string Zone { get; init; } = "";
        public string Network { get; init; } = "";
        public int PrefixLength { get; init; }
        public bool IsInternet { get; init; }
        public bool EmptyRootPathByDefinition { get; init; }
        public List<string> RootPath { get; init; } = [];
        public List<string> InternetPath { get; init; } = [];
        public Dictionary<string, int> GatewayDeviceIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        internal bool Contains(IpEndpoint endpoint)
        {
            return !IsInternet && endpoint.Family == AddressFamily &&
                endpoint.Value >= NetworkStart && endpoint.Value <= NetworkEnd;
        }
    }

    public class PathAnalysisTable
    {
        private readonly List<PathAnalysisTableEntry> entries;
        private readonly PathAnalysisTableEntry? internetEntry;

        public IReadOnlyList<PathAnalysisTableEntry> Entries => entries;

        public List<string> GatewayNames => [.. entries
            .SelectMany(entry => entry.RootPath.Concat(entry.InternetPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        public PathAnalysisTable(IEnumerable<PathAnalysisTableEntry> entries)
        {
            this.entries = [.. entries.OrderByDescending(entry => entry.PrefixLength)];
            internetEntry = this.entries.FirstOrDefault(entry => entry.IsInternet);
            Validate();
        }

        /// <summary>
        /// Builds a validated path-analysis table from converted TSQ import data.
        /// </summary>
        public static PathAnalysisTable FromImportData(PathAnalysisImportParameters importData)
        {
            return new(ParseImportEntries(importData));
        }

        /// <summary>
        /// Returns a copy of the table enriched with gateway-to-device mappings.
        /// </summary>
        public PathAnalysisTable WithDeviceMappings(Dictionary<string, int> gatewayDeviceIds)
        {
            return new(entries.Select(entry => CopyEntryWithMappings(entry, gatewayDeviceIds)));
        }

        /// <summary>
        /// Merges already parsed path-analysis tables into one validated lookup table.
        /// </summary>
        public static PathAnalysisTable Merge(IEnumerable<PathAnalysisTable> tables)
        {
            return new(tables.SelectMany(table => table.Entries));
        }

        /// <summary>
        /// Calculates all relevant gateway names for the Cartesian product of sources and destinations.
        /// </summary>
        public PathAnalysisResult Analyze(IEnumerable<string> sources, IEnumerable<string> destinations)
        {
            PathAnalysisResult result = new();
            foreach (string source in ReduceToUniqueInputs(sources))
            {
                foreach (string destination in ReduceToUniqueInputs(destinations))
                {
                    List<string> gatewayNames = AnalyzeSinglePath(source, destination);
                    result.PairResults.Add(new() { Source = source, Destination = destination, GatewayNames = gatewayNames });
                    AddDistinct(result.GatewayNames, gatewayNames);
                }
            }
            return result;
        }

        /// <summary>
        /// Calculates all relevant gateway names for one source-to-destination flow.
        /// </summary>
        public List<string> AnalyzeSinglePath(string source, string destination)
        {
            ResolvedPathEndpoint sourceEndpoint = ResolveEndpoint(source);
            ResolvedPathEndpoint destinationEndpoint = ResolveEndpoint(destination);
            return AnalyzeResolvedPair(sourceEndpoint, destinationEndpoint);
        }

        private static List<PathAnalysisTableEntry> ParseImportEntries(PathAnalysisImportParameters importData)
        {
            List<PathAnalysisTableEntry> parsedEntries = [];
            for (int index = 0; index < importData.Entries.Count; index++)
            {
                parsedEntries.Add(ParseEntry(importData.Entries[index], index + 1, importData.SourceName));
            }
            return parsedEntries;
        }

        private static PathAnalysisTableEntry ParseEntry(PathAnalysisImportEntry importEntry, int lineNumber, string sourceName)
        {
            string network = importEntry.Network.Trim();
            List<string> rootPath = ParsePath(importEntry.RootPath);
            bool isInternet = IsInternetNetworkToken(network);
            bool emptyRootPathByDefinition = rootPath.Count == 0;

            if (isInternet)
            {
                return new()
                {
                    LineNumber = lineNumber,
                    SourceName = sourceName,
                    Version = importEntry.Version,
                    Zone = importEntry.Zone,
                    Network = network,
                    PrefixLength = 0,
                    IsInternet = true,
                    EmptyRootPathByDefinition = emptyRootPathByDefinition,
                    RootPath = rootPath,
                    InternetPath = ParsePath(importEntry.InternetPath)
                };
            }

            (AddressFamily family, BigInteger networkStart, BigInteger networkEnd, int prefixLength) = ParseNetwork(network, lineNumber, sourceName);
            return new()
            {
                LineNumber = lineNumber,
                SourceName = sourceName,
                Version = importEntry.Version,
                Zone = importEntry.Zone,
                Network = network,
                PrefixLength = prefixLength,
                AddressFamily = family,
                NetworkStart = networkStart,
                NetworkEnd = networkEnd,
                EmptyRootPathByDefinition = emptyRootPathByDefinition,
                RootPath = rootPath,
                InternetPath = ParsePath(importEntry.InternetPath)
            };
        }

        private static PathAnalysisTableEntry CopyEntryWithMappings(PathAnalysisTableEntry entry, Dictionary<string, int> gatewayDeviceIds)
        {
            return new()
            {
                AddressFamily = entry.AddressFamily,
                NetworkStart = entry.NetworkStart,
                NetworkEnd = entry.NetworkEnd,
                LineNumber = entry.LineNumber,
                SourceName = entry.SourceName,
                Version = entry.Version,
                Zone = entry.Zone,
                Network = entry.Network,
                PrefixLength = entry.PrefixLength,
                IsInternet = entry.IsInternet,
                EmptyRootPathByDefinition = entry.EmptyRootPathByDefinition,
                RootPath = [.. entry.RootPath],
                InternetPath = [.. entry.InternetPath],
                GatewayDeviceIds = new(gatewayDeviceIds, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static List<string> ParsePath(string path)
        {
            List<string> gateways = [];
            foreach (string rawToken in path.Split('|', StringSplitOptions.TrimEntries))
            {
                string token = rawToken.Trim();
                if (token == "" || token == "-" || token == "#" ||
                    token.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Root", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Internet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                gateways.Add(token);
            }
            return gateways;
        }

        private static (AddressFamily Family, BigInteger NetworkStart, BigInteger NetworkEnd, int PrefixLength) ParseNetwork(string network, int lineNumber, string sourceName)
        {
            if (!network.Contains('/'))
            {
                throw new PathAnalysisException($"Network '{network}' in '{sourceName}' line {lineNumber} must use CIDR notation.");
            }

            string[] parts = network.Split('/', 2);
            if (!TryParseIp(parts[0], out IpEndpoint baseAddress))
            {
                throw new PathAnalysisException($"Invalid network '{network}' in '{sourceName}' line {lineNumber}.");
            }
            int totalBits = AddressBitLength(baseAddress.Family);
            if (!int.TryParse(parts[1], out int prefixLength) || prefixLength < 0 || prefixLength > totalBits)
            {
                throw new PathAnalysisException($"Invalid mask '{parts[1]}' in '{sourceName}' line {lineNumber}.");
            }
            BigInteger hostMask = (BigInteger.One << (totalBits - prefixLength)) - 1;
            BigInteger networkMask = ((BigInteger.One << totalBits) - 1) ^ hostMask;
            BigInteger networkStart = baseAddress.Value & networkMask;
            return (baseAddress.Family, networkStart, networkStart | hostMask, prefixLength);
        }

        private ResolvedPathEndpoint ResolveEndpoint(string value)
        {
            if (IsExplicitInternet(value))
            {
                return ResolvedPathEndpoint.Internet(value);
            }
            if (!TryParseIp(value, out IpEndpoint endpoint))
            {
                throw new PathAnalysisException($"'{value}' is not a valid IPv4 or IPv6 endpoint.");
            }

            PathAnalysisTableEntry? entry = entries.FirstOrDefault(entry => entry.Contains(endpoint));
            if (entry != null)
            {
                return ResolvedPathEndpoint.Network(value, entry);
            }
            if (internetEntry != null && IsPublicIp(endpoint))
            {
                return ResolvedPathEndpoint.Internet(value);
            }
            throw new PathAnalysisException($"No path-analysis entry found for '{value}'.");
        }

        private List<string> AnalyzeResolvedPair(ResolvedPathEndpoint sourceEndpoint, ResolvedPathEndpoint destinationEndpoint)
        {
            if (sourceEndpoint.IsInternet && destinationEndpoint.IsInternet)
            {
                throw new PathAnalysisException("Internet is only allowed on one side of a path-analysis flow.");
            }
            if (sourceEndpoint.IsInternet || destinationEndpoint.IsInternet)
            {
                return AnalyzeInternetPath(sourceEndpoint, destinationEndpoint);
            }

            List<string> sourcePath = [.. sourceEndpoint.Entry!.RootPath];
            List<string> destinationPath = [.. destinationEndpoint.Entry!.RootPath];
            List<string> originalSourcePath = [.. sourcePath];
            List<string> originalDestinationPath = [.. destinationPath];
            TrimCommonRootSuffix(sourcePath, destinationPath);
            ApplyFallback(sourcePath, originalSourcePath, sourceEndpoint.Entry.EmptyRootPathByDefinition);
            ApplyFallback(destinationPath, originalDestinationPath, destinationEndpoint.Entry.EmptyRootPathByDefinition);
            List<string> result = [.. sourcePath, .. destinationPath.AsEnumerable().Reverse()];
            return DistinctPreserveOrder(result);
        }

        private static List<string> AnalyzeInternetPath(ResolvedPathEndpoint sourceEndpoint, ResolvedPathEndpoint destinationEndpoint)
        {
            PathAnalysisTableEntry nonInternetEntry = sourceEndpoint.IsInternet ? destinationEndpoint.Entry! : sourceEndpoint.Entry!;
            List<string> internetPath = [.. nonInternetEntry.InternetPath];
            if (sourceEndpoint.IsInternet)
            {
                internetPath.Reverse();
            }
            return DistinctPreserveOrder(internetPath);
        }

        private void Validate()
        {
            Dictionary<string, FirewallSuccessor> successors = new(StringComparer.OrdinalIgnoreCase);
            foreach (PathAnalysisTableEntry entry in entries)
            {
                ValidatePathHasNoDuplicates(entry, entry.RootPath, "root");
                ValidatePathHasNoDuplicates(entry, entry.InternetPath, "internet");
                ValidateSuccessors(entry, successors);
            }
        }

        private static void ValidatePathHasNoDuplicates(PathAnalysisTableEntry entry, List<string> path, string pathName)
        {
            HashSet<string> seenGateways = new(StringComparer.OrdinalIgnoreCase);
            foreach (string gateway in path)
            {
                if (!seenGateways.Add(gateway))
                {
                    throw new PathAnalysisException($"Gateway '{gateway}' appears more than once in the {pathName} path at '{entry.SourceName}' line {entry.LineNumber}.");
                }
            }
        }

        private static void ValidateSuccessors(PathAnalysisTableEntry entry, Dictionary<string, FirewallSuccessor> successors)
        {
            for (int index = 0; index < entry.RootPath.Count; index++)
            {
                string firewall = entry.RootPath[index];
                string successor = index + 1 < entry.RootPath.Count ? entry.RootPath[index + 1] : "Root";
                if (successors.TryGetValue(firewall, out FirewallSuccessor? existing) &&
                    !existing.Successor.Equals(successor, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PathAnalysisException(
                        $"Gateway '{firewall}' has successors '{existing.Successor}' and '{successor}' in root paths " +
                        $"('{existing.SourceName}' line {existing.LineNumber}, '{entry.SourceName}' line {entry.LineNumber}).");
                }
                successors[firewall] = new(successor, entry.SourceName, entry.LineNumber);
            }
        }

        private static void TrimCommonRootSuffix(List<string> sourcePath, List<string> destinationPath)
        {
            while (sourcePath.Count > 0 && destinationPath.Count > 0 &&
                sourcePath[^1].Equals(destinationPath[^1], StringComparison.OrdinalIgnoreCase))
            {
                sourcePath.RemoveAt(sourcePath.Count - 1);
                destinationPath.RemoveAt(destinationPath.Count - 1);
            }
        }

        private static void ApplyFallback(List<string> path, List<string> originalPath, bool emptyByDefinition)
        {
            if (path.Count == 0 && originalPath.Count > 0 && !emptyByDefinition)
            {
                path.Add(originalPath[0]);
            }
        }

        private static List<string> ReduceToUniqueInputs(IEnumerable<string> values)
        {
            return [.. values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private static void AddDistinct(List<string> target, IEnumerable<string> values)
        {
            foreach (string value in values)
            {
                if (!target.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    target.Add(value);
                }
            }
        }

        private static List<string> DistinctPreserveOrder(IEnumerable<string> values)
        {
            List<string> result = [];
            AddDistinct(result, values);
            return result;
        }

        private static int AddressBitLength(AddressFamily family)
        {
            return family == AddressFamily.InterNetworkV6 ? 128 : 32;
        }

        private static bool TryParseIp(string value, out IpEndpoint endpoint)
        {
            string trimmedValue = value.Trim();
            if (trimmedValue.Contains('/'))
            {
                trimmedValue = trimmedValue.Split('/', 2)[0];
            }
            if (IPAddress.TryParse(trimmedValue, out IPAddress? parsedIp) &&
                (parsedIp.AddressFamily == AddressFamily.InterNetwork || parsedIp.AddressFamily == AddressFamily.InterNetworkV6))
            {
                endpoint = new(parsedIp.AddressFamily, new BigInteger(parsedIp.GetAddressBytes(), isUnsigned: true, isBigEndian: true));
                return true;
            }
            endpoint = default;
            return false;
        }

        private static bool IsInternetNetworkToken(string network)
        {
            string trimmedValue = network.Trim();
            return trimmedValue.StartsWith('!') || IsExplicitInternet(trimmedValue);
        }

        private static bool IsExplicitInternet(string value)
        {
            string trimmedValue = value.Trim();
            return trimmedValue.Equals("Internet", StringComparison.OrdinalIgnoreCase) ||
                trimmedValue.Equals("Any", StringComparison.OrdinalIgnoreCase) ||
                trimmedValue.Equals("0.0.0.0/0", StringComparison.OrdinalIgnoreCase) ||
                trimmedValue.Equals("::/0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPublicIp(IpEndpoint endpoint)
        {
            if (endpoint.Family == AddressFamily.InterNetworkV6)
            {
                return !IsInRange(endpoint, "::1", 128) &&        // loopback
                    !IsInRange(endpoint, "fc00::", 7) &&          // unique local (RFC 4193)
                    !IsInRange(endpoint, "fe80::", 10);           // link-local
            }
            return !IsInRange(endpoint, "10.0.0.0", 8) &&
                !IsInRange(endpoint, "172.16.0.0", 12) &&
                !IsInRange(endpoint, "192.168.0.0", 16);
        }

        private static bool IsInRange(IpEndpoint endpoint, string baseAddress, int prefixLength)
        {
            IPAddress baseIp = IPAddress.Parse(baseAddress);
            int totalBits = AddressBitLength(baseIp.AddressFamily);
            BigInteger baseValue = new(baseIp.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
            BigInteger networkMask = ((BigInteger.One << totalBits) - 1) ^ ((BigInteger.One << (totalBits - prefixLength)) - 1);
            return (endpoint.Value & networkMask) == (baseValue & networkMask);
        }

        private sealed record FirewallSuccessor(string Successor, string SourceName, int LineNumber);

        private sealed class ResolvedPathEndpoint
        {
            public bool IsInternet { get; private init; }
            public string Value { get; private init; } = "";
            public PathAnalysisTableEntry? Entry { get; private init; }

            public static ResolvedPathEndpoint Internet(string value)
            {
                return new() { Value = value, IsInternet = true };
            }

            public static ResolvedPathEndpoint Network(string value, PathAnalysisTableEntry entry)
            {
                return new() { Value = value, Entry = entry };
            }
        }
    }

    public static class PathAnalysisTableStore
    {
        private static PathAnalysisTable? activeTable;

        public static PathAnalysisTable? ActiveTable => activeTable;

        /// <summary>
        /// Replaces the active TSQ path-analysis table snapshot.
        /// </summary>
        public static void Replace(PathAnalysisTable? table)
        {
            activeTable = table;
        }
    }

    public class PathAnalysis
    {
        /// <summary>
        /// Calculates gateway names for a single source-to-destination path.
        /// </summary>
        public static async Task<string> GetDeviceNamesForSinglePath(string source, string destination, ApiConnection apiConnection)
        {
            return await GetDeviceNamesForSinglePath(source, destination, apiConnection, PathAnalysisMode.GatewayRoutingTable);
        }

        /// <summary>
        /// Calculates gateway names for a single source-to-destination path using the selected mode.
        /// </summary>
        public static async Task<string> GetDeviceNamesForSinglePath(string source, string destination, ApiConnection apiConnection, PathAnalysisMode mode)
        {
            if (PathAnalysisTableStore.ActiveTable != null)
            {
                if (mode == PathAnalysisMode.StaticImport)
                {
                    return string.Join(", ", PathAnalysisTableStore.ActiveTable.AnalyzeSinglePath(source, destination));
                }
            }

            List<Device> deviceList = await AnalyzeSinglePathLegacy(source, destination, apiConnection);
            return string.Join(", ", deviceList.Select(device => device.Name ?? ""));
        }

        /// <summary>
        /// Calculates all matching devices for workflow request elements.
        /// </summary>
        public static async Task<List<Device>> GetAllDevices(List<WfReqElement> elements, ApiConnection apiConnection)
        {
            return await GetAllDevices(elements, apiConnection, PathAnalysisMode.GatewayRoutingTable);
        }

        /// <summary>
        /// Calculates all matching devices for workflow request elements using the selected mode.
        /// </summary>
        public static async Task<List<Device>> GetAllDevices(List<WfReqElement> elements, ApiConnection apiConnection, PathAnalysisMode mode)
        {
            PathAnalysisTable? activeTable = PathAnalysisTableStore.ActiveTable;
            if (activeTable != null && mode == PathAnalysisMode.StaticImport)
            {
                List<string> gatewayNames = AnalyzeRequestElements(elements, activeTable).GatewayNames;
                return await ResolveDevicesByName(gatewayNames, apiConnection, activeTable);
            }
            return await GetAllDevicesLegacy(elements, apiConnection);
        }

        /// <summary>
        /// Calculates all gateway names for a flow.access entry.
        /// </summary>
        public static List<string> GetGatewayNames(FlowAccess access, PathAnalysisTable? table = null)
        {
            PathAnalysisTable? activeTable = table ?? PathAnalysisTableStore.ActiveTable;
            return activeTable?.Analyze(GetFlowSources(access), GetFlowDestinations(access)).GatewayNames ?? [];
        }

        /// <summary>
        /// Calculates all gateway names for an imported firewall rule.
        /// </summary>
        public static List<string> GetGatewayNames(Rule rule, PathAnalysisTable? table = null)
        {
            PathAnalysisTable? activeTable = table ?? PathAnalysisTableStore.ActiveTable;
            return activeTable?.Analyze(GetRuleSources(rule), GetRuleDestinations(rule)).GatewayNames ?? [];
        }

        /// <summary>
        /// Calculates all gateway names for a modelling.connection entry.
        /// </summary>
        public static List<string> GetGatewayNames(ModellingConnection connection, PathAnalysisTable? table = null)
        {
            PathAnalysisTable? activeTable = table ?? PathAnalysisTableStore.ActiveTable;
            return activeTable?.Analyze(GetModellingSources(connection), GetModellingDestinations(connection)).GatewayNames ?? [];
        }

        private static PathAnalysisResult AnalyzeRequestElements(List<WfReqElement> elements, PathAnalysisTable table)
        {
            List<string> sources = [];
            List<string> destinations = [];
            foreach (WfReqElement element in elements)
            {
                if (element.Cidr?.CidrString == null || element.Cidr.CidrString == "")
                {
                    continue;
                }
                if (element.Field == ElemFieldType.source.ToString())
                {
                    sources.Add(element.Cidr.CidrString);
                }
                else if (element.Field == ElemFieldType.destination.ToString())
                {
                    destinations.Add(element.Cidr.CidrString);
                }
            }
            return table.Analyze(sources, destinations);
        }

        private static async Task<List<Device>> ResolveDevicesByName(List<string> gatewayNames, ApiConnection apiConnection, PathAnalysisTable table)
        {
            List<Device> devices = await apiConnection.SendQueryAsync<List<Device>>(DeviceQueries.getDeviceDetails);
            Dictionary<string, Device> deviceByName = devices
                .Where(device => !string.IsNullOrWhiteSpace(device.Name))
                .GroupBy(device => device.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            List<Device> result = [];
            foreach (string gatewayName in gatewayNames)
            {
                if (deviceByName.TryGetValue(gatewayName, out Device? device))
                {
                    result.Add(device);
                    continue;
                }
                int? mappedId = table.Entries.SelectMany(entry => entry.GatewayDeviceIds)
                    .FirstOrDefault(mapping => mapping.Key.Equals(gatewayName, StringComparison.OrdinalIgnoreCase)).Value;
                result.Add(new Device { Id = mappedId ?? 0, Name = gatewayName });
            }
            return result;
        }

        private static async Task<List<Device>> GetAllDevicesLegacy(List<WfReqElement> elements, ApiConnection apiConnection)
        {
            List<Device> deviceList = [];
            try
            {
                foreach (KeyValuePair<string, string> elementPair in AnalyzeElementsLegacy(elements))
                {
                    foreach (Device device in await AnalyzeSinglePathLegacy(elementPair.Key, elementPair.Value, apiConnection))
                    {
                        if (deviceList.FirstOrDefault(existingDevice => existingDevice.Id == device.Id) == null)
                        {
                            deviceList.Add(device);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.WriteError("Path Analysis", "error while analysing paths", exception);
            }
            return deviceList;
        }

        private static List<KeyValuePair<string, string>> AnalyzeElementsLegacy(List<WfReqElement> elements)
        {
            List<KeyValuePair<string, string>> elementPairs = [];
            List<string> sources = [];
            List<string> destinations = [];
            foreach (WfReqElement element in elements)
            {
                if (element.Field == ElemFieldType.source.ToString() && element.Cidr?.CidrString != null)
                {
                    sources.Add(element.Cidr.CidrString);
                }
                else if (element.Field == ElemFieldType.destination.ToString() && element.Cidr?.CidrString != null)
                {
                    destinations.Add(element.Cidr.CidrString);
                }
            }
            foreach (string source in sources)
            {
                foreach (string destination in destinations)
                {
                    elementPairs.Add(new KeyValuePair<string, string>(source, destination));
                }
            }
            return elementPairs;
        }

        private static async Task<List<Device>> AnalyzeSinglePathLegacy(string source, string destination, ApiConnection apiConnection)
        {
            List<Device> deviceList = [];
            try
            {
                IPAddressRange routingSource = IPAddressRange.Parse(source);
                IPAddressRange routingDestination = IPAddressRange.Parse(destination);
                var variables = new { src = routingSource.Begin.ToString(), dst = routingDestination.Begin.ToString() };
                deviceList = await apiConnection.SendQueryAsync<List<Device>>(NetworkAnalysisQueries.pathAnalysis, variables);
            }
            catch (Exception exception)
            {
                Log.WriteError("Path Analysis", "error while analysing path", exception);
            }
            return deviceList;
        }

        private static List<string> GetRuleSources(Rule rule)
        {
            return [.. rule.Froms.SelectMany(location => ExpandNetworkObject(location.Object))];
        }

        private static List<string> GetRuleDestinations(Rule rule)
        {
            return [.. rule.Tos.SelectMany(location => ExpandNetworkObject(location.Object))];
        }

        private static List<string> GetFlowSources(FlowAccess access)
        {
            List<string> sources = [.. access.Sources?.Select(source => FormatFlowObject(source.NwObject)) ?? []];
            sources.AddRange(access.SourceGroups?.SelectMany(group => ExpandFlowGroup(group.NwGroup)) ?? []);
            return [.. sources.Where(source => !string.IsNullOrWhiteSpace(source))];
        }

        private static List<string> GetFlowDestinations(FlowAccess access)
        {
            List<string> destinations = [.. access.Destinations?.Select(destination => FormatFlowObject(destination.NwObject)) ?? []];
            destinations.AddRange(access.DestinationGroups?.SelectMany(group => ExpandFlowGroup(group.NwGroup)) ?? []);
            return [.. destinations.Where(destination => !string.IsNullOrWhiteSpace(destination))];
        }

        private static IEnumerable<string> ExpandFlowGroup(FlowNwGroup group)
        {
            foreach (FlowNwGroupMember member in group.NwGroupMembers)
            {
                yield return FormatFlowObject(member.NwObject);
            }
            foreach (NetworkObject networkObject in group.Objects ?? [])
            {
                foreach (string endpoint in ExpandNetworkObject(networkObject))
                {
                    yield return endpoint;
                }
            }
        }

        private static string FormatFlowObject(FlowNwObject flowObject)
        {
            return FormatEndpoint(flowObject.IpStart, flowObject.IpEnd);
        }

        private static List<string> GetModellingSources(ModellingConnection connection)
        {
            List<string> sources = [.. connection.SourceAppServers.Select(wrapper => FormatAppServer(wrapper.Content))];
            sources.AddRange(connection.SourceAppRoles.SelectMany(wrapper => wrapper.Content.AppServers.Select(server => FormatAppServer(server.Content))));
            sources.AddRange(connection.SourceAreas.SelectMany(wrapper => wrapper.Content.IpData.Select(ip => FormatSubnet(ip.Content))));
            return [.. sources.Where(source => !string.IsNullOrWhiteSpace(source))];
        }

        private static List<string> GetModellingDestinations(ModellingConnection connection)
        {
            List<string> destinations = [.. connection.DestinationAppServers.Select(wrapper => FormatAppServer(wrapper.Content))];
            destinations.AddRange(connection.DestinationAppRoles.SelectMany(wrapper => wrapper.Content.AppServers.Select(server => FormatAppServer(server.Content))));
            destinations.AddRange(connection.DestinationAreas.SelectMany(wrapper => wrapper.Content.IpData.Select(ip => FormatSubnet(ip.Content))));
            return [.. destinations.Where(destination => !string.IsNullOrWhiteSpace(destination))];
        }

        private static string FormatAppServer(ModellingAppServer appServer)
        {
            return FormatEndpoint(appServer.Ip, appServer.IpEnd);
        }

        private static string FormatSubnet(NetworkSubnet subnet)
        {
            return FormatEndpoint(subnet.Ip, subnet.IpEnd);
        }

        private static IEnumerable<string> ExpandNetworkObject(NetworkObject networkObject)
        {
            if (!string.IsNullOrWhiteSpace(networkObject.IP))
            {
                yield return FormatEndpoint(networkObject.IP, networkObject.IpEnd);
            }
            foreach (GroupFlat<NetworkObject> flatMember in networkObject.ObjectGroupFlats)
            {
                if (flatMember.Object != null)
                {
                    foreach (string endpoint in ExpandNetworkObject(flatMember.Object))
                    {
                        yield return endpoint;
                    }
                }
            }
        }

        private static string FormatEndpoint(string? ipStart, string? ipEnd)
        {
            if (string.IsNullOrWhiteSpace(ipStart))
            {
                return "";
            }
            if (ipStart.Contains('/'))
            {
                return ipStart;
            }
            if (!string.IsNullOrWhiteSpace(ipEnd) && ipStart != ipEnd)
            {
                return ipStart;
            }
            return ipStart.Contains(':') ? $"{ipStart}/128" : $"{ipStart}/32";
        }
    }
}
