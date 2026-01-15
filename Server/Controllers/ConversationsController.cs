using ChatServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatServer.Controllers
{
    [ApiController]
    [Route("api/conversations")]
    public class ConversationsController : ControllerBase
    {
        private readonly ConversationService _conversationService;

        public ConversationsController(ConversationService conversationService)
        {
            _conversationService = conversationService;
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
    }

    public class CreateDirectConversationRequest
    {
        public string CurrentUserId { get; set; } = string.Empty;
        public string OtherUserId { get; set; } = string.Empty;
    }
}
