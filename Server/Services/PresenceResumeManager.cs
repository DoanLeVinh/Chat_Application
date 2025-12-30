using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChatServer.Models.Presence;

namespace ChatServer.Services
{
    public class PresenceResumeManager : WsConnectionManager
    {
        // Quản lý trạng thái client
        private readonly ConcurrentDictionary<string, ClientState> _clientStates = new();
        
        // Cửa sổ reconnect: connectionId -> thời điểm hết hạn
        private readonly ConcurrentDictionary<string, DateTime> _reconnectWindows = new();
        
        // Graceful shutdown flag
        private bool _isShuttingDown = false;
        private readonly object _shutdownLock = new();

        // Thêm kết nối với trạng thái
        public string AddConnectionWithState(WebSocket socket, string userId, string sessionId)
        {
            var connectionId = base.AddConnection(socket, userId);
            
            var clientState = new ClientState
            {
                ConnectionId = connectionId,
                UserId = userId,
                SessionId = sessionId,
                ConnectedAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                Status = "online"
            };
            
            _clientStates[connectionId] = clientState;
            _reconnectWindows[connectionId] = DateTime.UtcNow.AddMinutes(5); // Cửa sổ 5 phút
            
            return connectionId;
        }

        // Ghi đè RemoveConnection để lưu trạng thái cho resume
        public new void RemoveConnection(string connectionId)
        {
            if (_clientStates.TryRemove(connectionId, out var state))
            {
                state.DisconnectedAt = DateTime.UtcNow;
                state.Status = "offline";
            }
            
            _reconnectWindows.TryRemove(connectionId, out _);
            base.RemoveConnection(connectionId);
        }

        // Lấy trạng thái client
        public ClientState? GetClientState(string connectionId)
        {
            _clientStates.TryGetValue(connectionId, out var state);
            return state;
        }

        // Kiểm tra xem connection có thể resume không
        public bool CanResume(string connectionId)
        {
            if (_reconnectWindows.TryGetValue(connectionId, out var expiry))
            {
                return DateTime.UtcNow <= expiry;
            }
            return false;
        }

        // Cập nhật heartbeat
        public async Task UpdateHeartbeat(string connectionId)
        {
            if (_clientStates.TryGetValue(connectionId, out var state))
            {
                state.LastHeartbeat = DateTime.UtcNow;
                state.Status = "online";
            }
        }

        // Cập nhật seq cuối cùng đã ack
        public void UpdateLastAckedSeq(string connectionId, string conversationId, long seq)
        {
            if (_clientStates.TryGetValue(connectionId, out var state))
            {
                state.LastAckedSeqByConversation[conversationId] = seq;
            }
        }

        // Lấy danh sách user online trong conversation
        public List<string> GetOnlineUsersInConversation(string conversationId)
        {
            return _clientStates.Values
                .Where(s => s.Status == "online" && s.SubscribedConversations.Contains(conversationId))
                .Select(s => s.UserId)
                .ToList();
        }

        // Broadcast thông báo server sắp shutdown
        public async Task BroadcastServerGoingDownAsync(int secondsUntilShutdown)
        {
            lock (_shutdownLock)
            {
                _isShuttingDown = true;
            }
            
            var shutdownEvent = new
            {
                type = "server_going_down",
                payload = new
                {
                    reason = "server_maintenance",
                    secondsUntilShutdown,
                    recommendedReconnectTime = DateTime.UtcNow.AddSeconds(secondsUntilShutdown + 30)
                }
            };
            
            // Gửi đến tất cả client đang kết nối
            foreach (var state in _clientStates.Values)
            {
                var socket = GetSocketByConnectionId(state.ConnectionId);
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(shutdownEvent);
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await socket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error sending shutdown notice: {ex.Message}");
                    }
                }
            }
        }

        // Helper method để lấy socket từ connectionId
        private WebSocket? GetSocketByConnectionId(string connectionId)
        {
            // Cần truy cập protected field _connections từ lớp cha, nếu không được thì cần điều chỉnh
            // Giả sử có method public để lấy socket
            return base.GetSocket(connectionId); // Giả định có method GetSocket trong WsConnectionManager
        }

        public bool IsShuttingDown()
        {
            lock (_shutdownLock)
            {
                return _isShuttingDown;
            }
        }
    }
}