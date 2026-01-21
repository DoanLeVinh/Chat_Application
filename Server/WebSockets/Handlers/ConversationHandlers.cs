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
        /// <summary>
        /// Handle create_direct - Tạo hoặc lấy direct conversation
        /// </summary>
        public static async Task<WsResponse> HandleCreateDirectAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<CreateDirectPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (payload == null || string.IsNullOrEmpty(payload.OtherUserId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // Dùng GetOrCreateDirectConversationAsync - tự động handle duplicate
                var conversation = await conversationService.GetOrCreateDirectConversationAsync(userId, payload.OtherUserId);

                // Lấy danh sách members
                var members = await conversationService.GetMembersAsync(conversation.Id);
                var safeMembers = members
                    .Where(m => !string.IsNullOrWhiteSpace(m.UserId))
                    .ToList();
                
                // Lấy thông tin user để set title
                var otherUser = await userService.GetUserByIdAsync(payload.OtherUserId);
                var displayTitle = otherUser?.DisplayName ?? "User";

                var directCreated = new
                {
                    conversationId = conversation.Id,
                    title = displayTitle,
                    type = conversation.Type,
                    members = safeMembers.Select(m => new
                    {
                        userId = m.UserId,
                        role = m.Role,
                        joinedAt = m.JoinedAt
                    }).ToList(),
                    createdAt = conversation.CreatedAt
                };

                // Broadcast conversation_created tới cả 2 users
                var memberIds = safeMembers.Select(m => m.UserId).Distinct().ToList();
                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "conversation_created",
                    Payload = directCreated
                });

                return new WsResponse { Type = "create_direct_ok", RequestId = message.RequestId, Payload = directCreated };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

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

                // Thêm creator vào member list + loại bỏ trùng
                var allMemberIds = new List<string>(payload.MemberIds ?? new List<string>());
                if (!allMemberIds.Contains(userId))
                {
                    allMemberIds.Add(userId);
                }
                allMemberIds = allMemberIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();

                if (allMemberIds.Count < 2)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Member list is invalid" } };
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
                    createdBy = conversation.CreatedBy,
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
            WsConnectionManager connectionManager,
            UserService userService)
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

                var conversation = await conversationService.GetConversationByIdAsync(payload.ConversationId);
                if (conversation == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Conversation không tồn tại" } };
                }
                if (conversation.Type != "group")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ áp dụng cho nhóm" } };
                }

                var inviteMode = (conversation.InviteMode ?? "public").Trim().ToLowerInvariant();

                // Kiểm tra là member và quyền theo inviteMode
                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Bạn không phải thành viên nhóm" } };
                }

                // private: chỉ owner được thêm trực tiếp; thành viên khác phải gửi invite_member để chờ duyệt
                if (inviteMode == "private" && requester.Role != "owner")
                {
                    return new WsResponse
                    {
                        Type = "error",
                        RequestId = message.RequestId,
                        Payload = new { error = "Nhóm đang ở chế độ riêng tư. Hãy gửi lời mời để quản trị viên duyệt." }
                    };
                }

                // Không add user đã là member
                var existingTarget = await conversationService.GetMemberAsync(payload.ConversationId, payload.UserId);
                if (existingTarget != null)
                {
                    return new WsResponse { Type = "add_member_ok", RequestId = message.RequestId, Payload = new { conversationId = payload.ConversationId, userId = payload.UserId, alreadyMember = true } };
                }

                // Thêm member
                var added = await conversationService.AddMemberAsync(payload.ConversationId, payload.UserId, "member", incrementVersion: true);
                if (added == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể thêm thành viên" } };
                }

                // Lấy info user/actor để broadcast UI friendly
                var actor = await userService.GetUserByIdAsync(userId);
                var addedUser = await userService.GetUserByIdAsync(payload.UserId);
                var membersVersion = (await conversationService.GetConversationByIdAsync(payload.ConversationId))?.MembersVersion ?? 0;

                var memberAdded = new
                {
                    conversationId = payload.ConversationId,
                    user = new
                    {
                        id = payload.UserId,
                        displayName = addedUser?.DisplayName ?? payload.UserId,
                        avatarUrl = addedUser?.AvatarUrl,
                        isOnline = addedUser?.IsOnline ?? false,
                        lastSeenAt = addedUser?.LastSeenAt
                    },
                    role = added.Role,
                    joinedAt = added.JoinedAt,
                    addedBy = new
                    {
                        id = userId,
                        displayName = actor?.DisplayName ?? userId
                    },
                    membersVersion
                };

                // Broadcast member_added đến toàn bộ members (kể cả người mới)
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var safeMembers = members
                    .Where(m => !string.IsNullOrWhiteSpace(m.UserId))
                    .ToList();
                var memberIds = safeMembers
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToList();
                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "member_added",
                    Payload = memberAdded
                });

                // Gửi thêm conversation_created cho member mới để họ thấy nhóm xuất hiện trong list ngay
                var displayTitle = conversation.Title;
                var statusMap = await userService.GetUsersStatusAsync(memberIds);
                var memberList = safeMembers.Select(m => new
                {
                    id = m.UserId,
                    displayName = statusMap.TryGetValue(m.UserId, out var s) ? s.DisplayName : m.UserId,
                    role = m.Role,
                    joinedAt = m.JoinedAt,
                    isOnline = statusMap.TryGetValue(m.UserId, out var s2) && s2.IsOnline,
                    lastSeenAt = statusMap.TryGetValue(m.UserId, out var s3) ? s3.LastSeenAt : null
                }).ToList();

                await connectionManager.BroadcastToUsersAsync(new List<string> { payload.UserId }, new WsResponse
                {
                    Type = "conversation_created",
                    Payload = new
                    {
                        conversationId = conversation.Id,
                        title = displayTitle,
                        type = conversation.Type,
                        createdBy = conversation.CreatedBy,
                        members = memberList,
                        createdAt = conversation.CreatedAt,
                        updatedAt = conversation.UpdatedAt,
                        inviteMode = conversation.InviteMode
                    }
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
            WsConnectionManager connectionManager,
            UserService userService)
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

                var conversation = await conversationService.GetConversationByIdAsync(payload.ConversationId);
                if (conversation == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Conversation không tồn tại" } };
                }
                if (conversation.Type != "group")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ áp dụng cho nhóm" } };
                }

                // Kiểm tra quyền
                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null || requester.Role != "owner")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ quản trị viên (người lập nhóm) mới có quyền xóa thành viên" } };
                }

                var target = await conversationService.GetMemberAsync(payload.ConversationId, payload.UserId);
                if (target == null)
                {
                    return new WsResponse { Type = "remove_member_ok", RequestId = message.RequestId, Payload = new { conversationId = payload.ConversationId, userId = payload.UserId, notMember = true } };
                }

                // Rule: không ai kick owner
                if (target.Role == "owner")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể xóa chủ nhóm" } };
                }
                if (payload.UserId == userId)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể tự xóa mình (hãy dùng rời nhóm nếu cần)" } };
                }

                // Xóa member
                var removed = await conversationService.RemoveMemberAsync(payload.ConversationId, payload.UserId, incrementVersion: true);
                if (!removed)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể xóa thành viên" } };
                }

                var actor = await userService.GetUserByIdAsync(userId);
                var membersVersion = (await conversationService.GetConversationByIdAsync(payload.ConversationId))?.MembersVersion ?? 0;

                var memberRemoved = new
                {
                    conversationId = payload.ConversationId,
                    userId = payload.UserId,
                    removedBy = new
                    {
                        id = userId,
                        displayName = actor?.DisplayName ?? userId
                    },
                    membersVersion
                };

                // Broadcast member_removed
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var memberIds = members
                    .Select(m => m.UserId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();
                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "member_removed",
                    Payload = memberRemoved
                });

                // Notify trực tiếp user bị kick để client xoá conversation ngay
                await connectionManager.BroadcastToUsersAsync(new List<string> { payload.UserId }, new WsResponse
                {
                    Type = "kicked",
                    Payload = new
                    {
                        conversationId = payload.ConversationId,
                        removedBy = new { id = userId, displayName = actor?.DisplayName ?? userId },
                        at = DateTime.UtcNow
                    }
                });

                return new WsResponse { Type = "remove_member_ok", RequestId = message.RequestId, Payload = memberRemoved };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleGetMembersAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<GetMembersPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload == null || string.IsNullOrWhiteSpace(payload.ConversationId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                var isMember = await conversationService.IsMemberAsync(payload.ConversationId, userId);
                if (!isMember)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không có quyền xem thành viên" } };
                }

                var conversation = await conversationService.GetConversationByIdAsync(payload.ConversationId);
                if (conversation == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Conversation không tồn tại" } };
                }

                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var safeMembers = members
                    .Where(m => !string.IsNullOrWhiteSpace(m.UserId))
                    .ToList();
                var memberIds = safeMembers
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToList();
                var statusMap = await userService.GetUsersStatusAsync(memberIds);

                var list = safeMembers.Select(m => new
                {
                    id = m.UserId,
                    displayName = statusMap.TryGetValue(m.UserId, out var s) ? s.DisplayName : m.UserId,
                    role = m.Role,
                    joinedAt = m.JoinedAt,
                    isOnline = statusMap.TryGetValue(m.UserId, out var s2) && s2.IsOnline,
                    lastSeenAt = statusMap.TryGetValue(m.UserId, out var s3) ? s3.LastSeenAt : null
                }).ToList();

                return new WsResponse
                {
                    Type = "get_members_ok",
                    RequestId = message.RequestId,
                    Payload = new
                    {
                        conversationId = payload.ConversationId,
                        members = list,
                        membersVersion = conversation.MembersVersion
                    }
                };
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
                    if (c == null || string.IsNullOrWhiteSpace(c.Id))
                    {
                        continue;
                    }

                    // Lấy members
                    var members = await conversationService.GetMembersAsync(c.Id);
                    var safeMembers = members
                        .Where(m => !string.IsNullOrWhiteSpace(m.UserId))
                        .ToList();
                    
                    // Xác định title cho direct chat
                    string displayTitle = c.Title ?? "Chat";
                    if (c.Type == "direct")
                    {
                        // Tìm user còn lại (không phải current user)
                        var otherMember = safeMembers.FirstOrDefault(m => m.UserId != userId);
                        if (otherMember != null)
                        {
                            var otherUser = await userService.GetUserByIdAsync(otherMember.UserId);
                            displayTitle = otherUser?.DisplayName ?? "User";
                        }
                    }
                    
                    var memberIds = safeMembers
                        .Select(m => m.UserId)
                        .Distinct()
                        .ToList();
                    var statusMap = await userService.GetUsersStatusAsync(memberIds);
                    var memberList = safeMembers.Select(m => new
                    {
                        id = m.UserId,
                        displayName = statusMap.TryGetValue(m.UserId, out var s) ? s.DisplayName : m.UserId,
                        role = m.Role,
                        joinedAt = m.JoinedAt,
                        isOnline = statusMap.TryGetValue(m.UserId, out var s2) && s2.IsOnline,
                        lastSeenAt = statusMap.TryGetValue(m.UserId, out var s3) ? s3.LastSeenAt : null
                    }).ToList();
                    
                    conversationList.Add(new
                    {
                        conversationId = c.Id,
                        title = displayTitle,
                        type = c.Type,
                        members = memberList,
                        createdAt = c.CreatedAt,
                        updatedAt = c.UpdatedAt,
                        membersVersion = c.MembersVersion,
                        inviteMode = c.InviteMode
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
                // Log full stacktrace để truy ra chính xác chỗ nổ (ArgumentNullException key)
                Console.WriteLine($"❌ get_conversations error for user {userId}, requestId={message.RequestId}: {ex}");
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleSetInviteModeAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<SetInviteModePayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload == null || string.IsNullOrWhiteSpace(payload.ConversationId) || string.IsNullOrWhiteSpace(payload.InviteMode))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                var conversation = await conversationService.GetConversationByIdAsync(payload.ConversationId);
                if (conversation == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Conversation không tồn tại" } };
                }
                if (conversation.Type != "group")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ áp dụng cho nhóm" } };
                }

                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null || requester.Role != "owner")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ quản trị viên (người lập nhóm) mới được đổi chế độ" } };
                }

                var mode = payload.InviteMode.Trim().ToLowerInvariant();
                if (mode != "public" && mode != "private")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "InviteMode không hợp lệ" } };
                }

                var ok = await conversationService.SetInviteModeAsync(payload.ConversationId, mode);
                if (!ok)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể cập nhật chế độ" } };
                }

                // Broadcast to all members
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var memberIds = members
                    .Select(m => m.UserId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();

                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "invite_mode_updated",
                    Payload = new { conversationId = payload.ConversationId, inviteMode = mode }
                });

                return new WsResponse { Type = "set_invite_mode_ok", RequestId = message.RequestId, Payload = new { conversationId = payload.ConversationId, inviteMode = mode } };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleInviteMemberAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<InviteMemberPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload == null || string.IsNullOrWhiteSpace(payload.ConversationId) || string.IsNullOrWhiteSpace(payload.UserId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                if (payload.UserId == userId)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể tự mời chính mình" } };
                }

                var conversation = await conversationService.GetConversationByIdAsync(payload.ConversationId);
                if (conversation == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Conversation không tồn tại" } };
                }
                if (conversation.Type != "group")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ áp dụng cho nhóm" } };
                }

                var inviteMode = (conversation.InviteMode ?? "public").Trim().ToLowerInvariant();
                if (inviteMode != "private")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Nhóm đang ở chế độ công khai. Hãy thêm trực tiếp." } };
                }

                // requester must be a member
                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Bạn không phải thành viên nhóm" } };
                }

                // already member?
                var existingTarget = await conversationService.GetMemberAsync(payload.ConversationId, payload.UserId);
                if (existingTarget != null)
                {
                    return new WsResponse { Type = "invite_member_ok", RequestId = message.RequestId, Payload = new { conversationId = payload.ConversationId, userId = payload.UserId, alreadyMember = true } };
                }

                var invite = await conversationService.CreateInviteAsync(payload.ConversationId, payload.UserId, userId);
                if (invite == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể tạo lời mời" } };
                }

                var inviter = await userService.GetUserByIdAsync(userId);
                var invited = await userService.GetUserByIdAsync(payload.UserId);

                var invitePayload = new
                {
                    inviteId = invite.Id,
                    conversationId = payload.ConversationId,
                    invitedUser = new
                    {
                        id = payload.UserId,
                        displayName = invited?.DisplayName ?? payload.UserId,
                        avatarUrl = invited?.AvatarUrl,
                        isOnline = invited?.IsOnline ?? false,
                        lastSeenAt = invited?.LastSeenAt
                    },
                    invitedBy = new
                    {
                        id = userId,
                        displayName = inviter?.DisplayName ?? userId
                    },
                    status = invite.Status,
                    createdAt = invite.CreatedAt
                };

                // Notify owners (approvers)
                var members = await conversationService.GetMembersAsync(payload.ConversationId);
                var ownerIds = members
                    .Where(m => m.Role == "owner" && !string.IsNullOrWhiteSpace(m.UserId))
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToList();

                if (ownerIds.Count > 0)
                {
                    await connectionManager.BroadcastToUsersAsync(ownerIds, new WsResponse
                    {
                        Type = "invite_created",
                        Payload = invitePayload
                    });
                }

                return new WsResponse { Type = "invite_member_ok", RequestId = message.RequestId, Payload = invitePayload };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleGetPendingInvitesAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<GetPendingInvitesPayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload == null || string.IsNullOrWhiteSpace(payload.ConversationId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                var requester = await conversationService.GetMemberAsync(payload.ConversationId, userId);
                if (requester == null || requester.Role != "owner")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ quản trị viên (người lập nhóm) mới được xem danh sách chờ duyệt" } };
                }

                var pending = await conversationService.GetPendingInvitesAsync(payload.ConversationId);
                var invitedIds = pending.Select(i => i.InvitedUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                var invitedByIds = pending.Select(i => i.InvitedByUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

                var statusMap = await userService.GetUsersStatusAsync(invitedIds.Concat(invitedByIds).Distinct().ToList());

                var list = pending.Select(i => new
                {
                    inviteId = i.Id,
                    conversationId = i.ConversationId,
                    invitedUser = new
                    {
                        id = i.InvitedUserId,
                        displayName = statusMap.TryGetValue(i.InvitedUserId, out var s) ? s.DisplayName : i.InvitedUserId,
                        isOnline = statusMap.TryGetValue(i.InvitedUserId, out var s2) && s2.IsOnline,
                        lastSeenAt = statusMap.TryGetValue(i.InvitedUserId, out var s3) ? s3.LastSeenAt : null
                    },
                    invitedBy = new
                    {
                        id = i.InvitedByUserId,
                        displayName = statusMap.TryGetValue(i.InvitedByUserId, out var b) ? b.DisplayName : i.InvitedByUserId
                    },
                    status = i.Status,
                    createdAt = i.CreatedAt
                }).ToList();

                return new WsResponse
                {
                    Type = "get_pending_invites_ok",
                    RequestId = message.RequestId,
                    Payload = new { conversationId = payload.ConversationId, invites = list }
                };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleApproveInviteAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ApproveRejectInvitePayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload == null || string.IsNullOrWhiteSpace(payload.InviteId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                var invite = await conversationService.ApproveInviteAsync(payload.InviteId, userId);
                if (invite == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không tìm thấy lời mời hoặc đã được xử lý" } };
                }

                // Permission: only owner
                var requester = await conversationService.GetMemberAsync(invite.ConversationId, userId);
                if (requester == null || requester.Role != "owner")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ quản trị viên (người lập nhóm) mới được duyệt" } };
                }

                var conversation = await conversationService.GetConversationByIdAsync(invite.ConversationId);
                if (conversation == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Conversation không tồn tại" } };
                }

                // Add member
                var added = await conversationService.AddMemberAsync(invite.ConversationId, invite.InvitedUserId, "member", incrementVersion: true);
                if (added == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không thể thêm thành viên" } };
                }

                var actor = await userService.GetUserByIdAsync(userId);
                var addedUser = await userService.GetUserByIdAsync(invite.InvitedUserId);
                var membersVersion = (await conversationService.GetConversationByIdAsync(invite.ConversationId))?.MembersVersion ?? 0;

                var memberAdded = new
                {
                    conversationId = invite.ConversationId,
                    user = new
                    {
                        id = invite.InvitedUserId,
                        displayName = addedUser?.DisplayName ?? invite.InvitedUserId,
                        avatarUrl = addedUser?.AvatarUrl,
                        isOnline = addedUser?.IsOnline ?? false,
                        lastSeenAt = addedUser?.LastSeenAt
                    },
                    role = added.Role,
                    joinedAt = added.JoinedAt,
                    addedBy = new
                    {
                        id = userId,
                        displayName = actor?.DisplayName ?? userId
                    },
                    membersVersion
                };

                var members = await conversationService.GetMembersAsync(invite.ConversationId);
                var safeMembers = members.Where(m => !string.IsNullOrWhiteSpace(m.UserId)).ToList();
                var memberIds = safeMembers.Select(m => m.UserId).Distinct().ToList();

                await connectionManager.BroadcastToUsersAsync(memberIds, new WsResponse
                {
                    Type = "member_added",
                    Payload = memberAdded
                });

                // conversation_created to newly added member
                var statusMap = await userService.GetUsersStatusAsync(memberIds);
                var memberList = safeMembers.Select(m => new
                {
                    id = m.UserId,
                    displayName = statusMap.TryGetValue(m.UserId, out var s) ? s.DisplayName : m.UserId,
                    role = m.Role,
                    joinedAt = m.JoinedAt,
                    isOnline = statusMap.TryGetValue(m.UserId, out var s2) && s2.IsOnline,
                    lastSeenAt = statusMap.TryGetValue(m.UserId, out var s3) ? s3.LastSeenAt : null
                }).ToList();

                await connectionManager.BroadcastToUsersAsync(new List<string> { invite.InvitedUserId }, new WsResponse
                {
                    Type = "conversation_created",
                    Payload = new
                    {
                        conversationId = conversation.Id,
                        title = conversation.Title,
                        type = conversation.Type,
                        createdBy = conversation.CreatedBy,
                        members = memberList,
                        createdAt = conversation.CreatedAt,
                        updatedAt = conversation.UpdatedAt,
                        inviteMode = conversation.InviteMode
                    }
                });

                // notify inviter and invitee
                await connectionManager.BroadcastToUsersAsync(new List<string> { invite.InvitedByUserId, invite.InvitedUserId }.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(), new WsResponse
                {
                    Type = "invite_approved",
                    Payload = new { inviteId = invite.Id, conversationId = invite.ConversationId, approvedBy = new { id = userId, displayName = actor?.DisplayName ?? userId } }
                });

                return new WsResponse { Type = "approve_invite_ok", RequestId = message.RequestId, Payload = new { inviteId = invite.Id, conversationId = invite.ConversationId } };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }

        public static async Task<WsResponse> HandleRejectInviteAsync(
            WsMessage message,
            string userId,
            ConversationService conversationService,
            WsConnectionManager connectionManager,
            UserService userService)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ApproveRejectInvitePayload>(
                    message.Payload.ToString() ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload == null || string.IsNullOrWhiteSpace(payload.InviteId))
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Invalid payload" } };
                }

                // We need invite to check conversation and notify users; do a best-effort lookup by reading pending list
                // Approve/Reject methods will return updated doc only if pending.
                var rejected = await conversationService.RejectInviteAsync(payload.InviteId, userId);
                if (rejected == null)
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Không tìm thấy lời mời hoặc đã được xử lý" } };
                }

                var requester = await conversationService.GetMemberAsync(rejected.ConversationId, userId);
                if (requester == null || requester.Role != "owner")
                {
                    return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = "Chỉ quản trị viên (người lập nhóm) mới được từ chối" } };
                }

                var actor = await userService.GetUserByIdAsync(userId);
                await connectionManager.BroadcastToUsersAsync(new List<string> { rejected.InvitedByUserId, rejected.InvitedUserId }.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(), new WsResponse
                {
                    Type = "invite_rejected",
                    Payload = new { inviteId = rejected.Id, conversationId = rejected.ConversationId, rejectedBy = new { id = userId, displayName = actor?.DisplayName ?? userId } }
                });

                return new WsResponse { Type = "reject_invite_ok", RequestId = message.RequestId, Payload = new { inviteId = rejected.Id, conversationId = rejected.ConversationId } };
            }
            catch (Exception ex)
            {
                return new WsResponse { Type = "error", RequestId = message.RequestId, Payload = new { error = ex.Message } };
            }
        }
    }

    public class CreateDirectPayload
    {
        public string OtherUserId { get; set; } = "";
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

    public class GetMembersPayload
    {
        public string ConversationId { get; set; } = "";
    }

    public class SetInviteModePayload
    {
        public string ConversationId { get; set; } = "";
        public string InviteMode { get; set; } = "";
    }

    public class InviteMemberPayload
    {
        public string ConversationId { get; set; } = "";
        public string UserId { get; set; } = "";
    }

    public class GetPendingInvitesPayload
    {
        public string ConversationId { get; set; } = "";
    }

    public class ApproveRejectInvitePayload
    {
        public string InviteId { get; set; } = "";
    }
}
