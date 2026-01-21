using System;
using System.IO;
using System.Threading.Tasks;
using ChatServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatServer.Controllers
{
    [ApiController]
    [Route("api/uploads")]
    [Authorize] // Require login to upload
    public class UploadController : ControllerBase
    {
        private readonly UploadService _uploadService;

        public UploadController(UploadService uploadService)
        {
            _uploadService = uploadService;
        }

        [HttpPost("initiate")]
        public IActionResult Initiate([FromBody] InitiateUploadRequest request)
        {
            if (string.IsNullOrEmpty(request.FileName) || request.FileSize <= 0)
            {
                return BadRequest(new { error = "Invalid file info" });
            }

            // Max size check (Backend side double check) - 100MB
            if (request.FileSize > 100 * 1024 * 1024)
            {
                return BadRequest(new { error = "File too large (Max 100MB)" });
            }

            try
            {
                var sessionId = _uploadService.InitiateUpload(request.FileName, request.FileSize, request.ContentType);
                return Ok(new { sessionId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("chunk/{sessionId}")]
        public async Task<IActionResult> UploadChunk(string sessionId, [FromQuery] long offset)
        {
            try
            {
                var uploadedSize = await _uploadService.AppendChunkAsync(sessionId, Request.Body, offset);
                return Ok(new { uploadedSize });
            }
            catch (ArgumentException)
            {
                return NotFound(new { error = "Session not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("complete/{sessionId}")]
        public IActionResult Complete(string sessionId)
        {
            try
            {
                var url = _uploadService.CompleteUpload(sessionId);
                // Combine with base URL if needed, but relative is fine for now if served via StaticFiles
                // We need to ensure static files are enabled in Program.cs
                var fullUrl = $"{Request.Scheme}://{Request.Host}{url}";
                return Ok(new { url = fullUrl, status = "completed" });
            }
            catch (ArgumentException)
            {
                return NotFound(new { error = "Session not found" });
            }
        }

        [HttpGet("status/{sessionId}")]
        public IActionResult GetStatus(string sessionId)
        {
            var session = _uploadService.GetSession(sessionId);
            if (session == null) return NotFound(new { error = "Session not found" });

            return Ok(new
            {
                sessionId = session.SessionId,
                uploadedSize = session.UploadedSize,
                totalSize = session.TotalSize,
                status = session.IsCompleted ? "completed" : "uploading",
                url = session.IsCompleted ? $"{Request.Scheme}://{Request.Host}/uploads/{session.SafeFileName}" : null
            });
        }
        
    }

    public class InitiateUploadRequest
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
    }
}
