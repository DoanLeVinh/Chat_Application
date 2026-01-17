using ChatServer.Services;
using ChatServer.Models;
using Microsoft.AspNetCore.Mvc;
using ChatServer.Database;
using MongoDB.Driver;

namespace ChatServer.Controllers
{
    [ApiController]
    [Route("api/conversations")]
    public class ConversationsController : ControllerBase
    {
        private readonly ConversationService _conversationService;
        private readonly MongoDBContext _db;   

        public ConversationsController(ConversationService conversationService, MongoDBContext db)
        {
            _conversationService = conversationService;
            _db = db;
        }

        /// <summary>
        /// Tạo hoặc lấy direct conversation (1-1)
        /// </summary>
        [HttpPost("direct")]
        public async Task<IActionResult> CreateDirect(
            [FromBody] CreateDirectConversationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentUserId) ||
                string.IsNullOrWhiteSpace(request.OtherUserId))
            {
                return BadRequest(new { message = "currentUserId & otherUserId are required" });
            }

            var conversation =
                await _conversationService.GetOrCreateDirectConversationAsync(
                    request.CurrentUserId,
                    request.OtherUserId
                );

            return Ok(conversation);
        }
        [HttpGet("{conversationId}/pinned")]
        public async Task<IActionResult> GetPinnedMessages(string conversationId)
        {
            var pins = await _db.PinnedMessages
                .Find(p => p.ConversationId == conversationId)
                .SortByDescending(p => p.PinnedAt)
                .ToListAsync();

            return Ok(pins);
        }

    }

    public class CreateDirectConversationRequest
    {
        public string CurrentUserId { get; set; } = string.Empty;
        public string OtherUserId { get; set; } = string.Empty;
    }
}
