using System.Text.Json;
using ChatServer.Services;

namespace ChatServer.WebSockets.Handlers
{
    /// <summary>
    /// Conversation Event Handlers - Người 2
    /// Xử lý: create_group, add_member, remove_member, get_conversations
    /// </summary>
    public static class ConversationHandlers
    {
        public static async Task<WsResponse> HandleCreateGroupAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<CreateGroupPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (payload == null || string.IsNullOrEmpty(payload.Title))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // Thêm creator vào member list
                var allMemberIds = new List<string>(payload.MemberIds ?? new List<string>());
                if (!allMemberIds.Contains(userId))
                {
                    allMemberIds.Add(userId);
                }

                // Tạo group
                var conversation = await conversationService.CreateGroupConversationAsync(
                    userId,
                    payload.Title,
                    allMemberIds
                );

                // Lấy danh sách members
                var members = await conversationService.GetMembersAsync(conversation.Id);

                var groupCreated = new
                {
                    conversationId = conversation.Id,
                    title = conversation.Title,
                    type = conversation.Type,
                    members = members.Select(m => new
                    {
                        userId = m.UserId,
                        role = m.Role,
                        joinedAt = m.JoinedAt
                    }).ToList(),
                    createdAt = conversation.CreatedAt
                };

                // Broadcast group_created tới tất cả members
                await connectionManager.BroadcastToUsersAsync(allMemberIds, new WsResponse
                {
                    Type = "conversation_created",
                    Payload = groupCreated
                });

                return new WsResponse { Type = "create_group_ok", RequestId = message.RequestId, Payload = groupCreated };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleAddMemberAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<AddMemberPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (payload == null || string.IsNullOrEmpty(payload.ConversationId) || string.IsNullOrEmpty(payload.UserId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // Kiểm tra quyền (chỉ owner/admin mới add được)
                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null || (requester.Role != "owner" && requester.Role != "admin"))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không có quyền thêm thành viên" } };
                }

                // Thêm member
                await conversationService.AddMemberAsync(payload.ConversationId, payload.UserId, "member");

                var memberAdded = new
                {
                    conversationId = payload.ConversationId,
                    userId = payload.UserId,
                    role = "member",
                    joinedAt = DateTime.UtcNow
                };

                // Broadcast member_added
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var memberIds = members.Select(m => m.UserId).ToList();
                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "member_added",
                    Payload = memberAdded
                });

                return new WsResponse { Type = "add_member_ok", RequestId = message.RequestId, Payload = memberAdded };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleRemoveMemberAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<RemoveMemberPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (payload == null || string.IsNullOrEmpty(payload.ConversationId) || string.IsNullOrEmpty(payload.UserId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // Kiểm tra quyền
                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null || (requester.Role != "owner" && requester.Role != "admin"))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không có quyền xóa thành viên" } };
                }

                // Xóa member
                await conversationService.RemoveMemberAsync(payload.ConversationId, payload.UserId);

                var memberRemoved = new
                {
                    conversationId = payload.ConversationId,
                    userId = payload.UserId
                };

                // Broadcast member_removed
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var memberIds = members.Select(m => m.UserId).ToList();
                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "member_removed",
                    Payload = memberRemoved
                });

                return new WsResponse { Type = "remove_member_ok", RequestId = message.RequestId, Payload = memberRemoved };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleGetConversationsAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            UserService userService)
        {
            try
            {
                var conversations = await conversationService.GetUserConversationsAsync(userId);
                
                var conversationList = new List<object>();
                foreach (var c in conversations)
                {
                    // Lấy members
                    var members = await conversationService.GetMembersAsync(c.Id);
                    
                    // Xác định title cho direct chat
                    string displayTitle = c.Title;
                    if (c.Type == "direct")
                    {
                        // Tìm user còn lại (không phải current user)
                        var otherMember = members.FirstOrDefault(m => m.UserId != userId);
                        if (otherMember != null)
                        {
                            var otherUser = await userService.GetUserByIdAsync(otherMember.UserId);
                            displayTitle = otherUser?.DisplayName ?? "User";
                        }
                    }
                    
                    // Lấy thông tin members với displayName
                    var memberList = new List<object>();
                    foreach (var m in members)
                    {
                        var user = await userService.GetUserByIdAsync(m.UserId);
                        memberList.Add(new
                        {
                            id = m.UserId,
                            displayName = user?.DisplayName ?? m.UserId,
                            role = m.Role,
                            joinedAt = m.JoinedAt
                        });
                    }
                    
                    conversationList.Add(new
                    {
                        conversationId = c.Id,
                        title = displayTitle,
                        type = c.Type,
                        members = memberList,
                        createdAt = c.CreatedAt,
                        updatedAt = c.UpdatedAt
                    });
                }

                return new WsResponse
                {
                    Type = "conversations",
                    RequestId = message.RequestId,
                    Payload = new
                    {
                        conversations = conversationList
                    }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }
    }

    public class CreateGroupPayload
    {
        public string Title { get; set; } = "";
        public List<string>? MemberIds { get; set; }
    }

    public class AddMemberPayload
    {
        public string ConversationId { get; set; } = "";
        public string UserId { get; set; } = "";
    }

    public class RemoveMemberPayload
    {
        public string ConversationId { get; set; } = "";
        public string UserId { get; set; } = "";
    }
}
