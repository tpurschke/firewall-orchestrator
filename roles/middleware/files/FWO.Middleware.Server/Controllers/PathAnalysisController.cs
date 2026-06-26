using FWO.Api.Client;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data;
using FWO.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FWO.Middleware.Server.Controllers
{
    /// <summary>
    /// Controller for path-analysis imports.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PathAnalysisController(ApiConnection apiConnection, GlobalConfig globalConfig) : ControllerBase
    {
        private readonly ApiConnection apiConnection = apiConnection;
        private readonly GlobalConfig globalConfig = globalConfig;

        /// <summary>
        /// Imports converted TSQ path-analysis data.
        /// </summary>
        [HttpPost("Import")]
        [Authorize(Roles = $"{Roles.Admin}, {Roles.Importer}")]
        public async Task<ActionResult<PathAnalysisImportResult>> Import([FromBody] PathAnalysisImportParameters parameters)
        {
            if (parameters.Entries.Count == 0)
            {
                return BadRequest("No path-analysis entries supplied.");
            }

            try
            {
                PathAnalysisDataImport import = new(apiConnection, globalConfig);
                return Ok(await import.Import(parameters));
            }
            catch (Exception exception)
            {
                string errorText = $"Path-analysis import failed: {exception.Message}";
                Log.WriteError("Path Analysis Import", errorText, exception);
                return StatusCode(500, errorText);
            }
        }
    }
}
