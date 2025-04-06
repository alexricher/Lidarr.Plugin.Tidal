using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using System.Net;

namespace NzbDrone.Core.Indexers.Tidal
{
    [ApiController]
    [Route("api/v1/tidal/release")]
    public class TidalReleaseController : ControllerBase
    {
        private readonly ITidalReleaseFinder _releaseFinder;
        private readonly IDownloadService _downloadService;
        private readonly Logger _logger;

        public TidalReleaseController(
            ITidalReleaseFinder releaseFinder,
            IDownloadService downloadService,
            Logger logger)
        {
            _releaseFinder = releaseFinder;
            _downloadService = downloadService;
            _logger = logger;
        }

        [HttpGet("{indexerId}/{guid}")]
        public async Task<IActionResult> GetRelease(int indexerId, string guid)
        {
            try
            {
                _logger.Debug($"Looking for release with indexerId: {indexerId}, guid: {guid} in extended cache");
                var remoteAlbum = await _releaseFinder.FindReleaseAsync(indexerId, guid);
                
                if (remoteAlbum == null)
                {
                    return NotFound("Release not found in extended cache");
                }
                
                // Return basic info about the found release
                return Ok(new
                {
                    Title = remoteAlbum.Release.Title,
                    Size = remoteAlbum.Release.Size,
                    DownloadUrl = remoteAlbum.Release.DownloadUrl,
                    DownloadProtocol = remoteAlbum.Release.DownloadProtocol,
                    Guid = remoteAlbum.Release.Guid,
                    IndexerId = remoteAlbum.Release.IndexerId
                });
            }
            catch (NzbDroneClientException ex)
            {
                return StatusCode((int)ex.StatusCode, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving release");
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }
        }
        
        [HttpPost("download/{indexerId}/{guid}")]
        public async Task<IActionResult> DownloadRelease(int indexerId, string guid, [FromQuery] int? downloadClientId = null)
        {
            try
            {
                _logger.Info($"Attempting to download release with indexerId: {indexerId}, guid: {guid} from extended cache");
                
                // Get the release from our extended cache
                var remoteAlbum = await _releaseFinder.FindReleaseAsync(indexerId, guid);
                
                if (remoteAlbum == null)
                {
                    return NotFound("Release not found in extended cache");
                }
                
                // Attempt to download the release
                try
                {
                    await _downloadService.DownloadReport(remoteAlbum, downloadClientId);
                    _logger.Info($"Successfully initiated download for release: {remoteAlbum.Release.Title}");
                    
                    return Ok(new
                    {
                        Title = remoteAlbum.Release.Title,
                        Message = "Download started successfully"
                    });
                }
                catch (ReleaseDownloadException ex)
                {
                    _logger.Error(ex, $"Error downloading release from indexer: {ex.Message}");
                    return StatusCode((int)HttpStatusCode.BadGateway, $"Error downloading release: {ex.Message}");
                }
                catch (DownloadClientUnavailableException ex)
                {
                    _logger.Error(ex, $"Download client unavailable: {ex.Message}");
                    return StatusCode((int)HttpStatusCode.ServiceUnavailable, $"Download client unavailable: {ex.Message}");
                }
                catch (DownloadClientException ex)
                {
                    _logger.Error(ex, $"Download client error: {ex.Message}");
                    return StatusCode((int)HttpStatusCode.InternalServerError, $"Download client error: {ex.Message}");
                }
            }
            catch (NzbDroneClientException ex)
            {
                return StatusCode((int)ex.StatusCode, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing download request");
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }
        }
    }
} 