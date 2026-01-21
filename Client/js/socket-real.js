// ===== WEBSOCKET HANDLER - Kết nối server thật =====
class SocketHandler {
    constructor() {
        this.ws = null;
        this.userId = null;
        this.connectionId = null;
        this.requestId = 0;
        this.pendingRequests = new Map(); // Lưu callback cho từng request
        this.isConnected = false;
    }

    connect(userId) {
        return new Promise((resolve, reject) => {
            try {
                // Kết nối WebSocket - dùng IP của server (có thể sửa thành IP LAN)
                const serverHost = window.SERVER_HOST || 'localhost';
                this.ws = new WebSocket(`ws://${serverHost}:5000/ws`);
                
                this.ws.onopen = () => {
                    console.log('✅ WebSocket connected');
                    this.isConnected = true;
                    
                    // Auth ngay sau khi connect
                    this.send('auth', { userId })
                        .then(response => {
                            this.userId = response.payload.userId;
                            this.connectionId = response.payload.connectionId;
                            console.log(`✅ Authenticated as ${this.userId}`);
                            resolve();
                        })
                        .catch(reject);
                };

                this.ws.onmessage = (event) => {
                    console.log('📨 Raw message:', event.data);
                    const message = JSON.parse(event.data);
                    console.log('📦 Parsed message:', message);
                    console.log('📦 Message type:', message.type, 'Type:', message.Type);
                    this.handleMessage(message);
                };

                this.ws.onerror = (error) => {
                    console.error('❌ WebSocket error:', error);
                    this.isConnected = false;
                    reject(error);
                };

                this.ws.onclose = () => {
                    console.log('🔌 WebSocket disconnected');
                    this.isConnected = false;
                };
            } catch (error) {
                console.error('❌ Connection error:', error);
                reject(error);
            }
        });
    }

    send(type, payload) {
        return new Promise((resolve, reject) => {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
                reject(new Error('WebSocket not connected'));
                return;
            }

            const requestId = `req-${++this.requestId}`;
            const message = { type, requestId, payload };

            // Lưu callback
            this.pendingRequests.set(requestId, { resolve, reject });

            // Gửi message
            this.ws.send(JSON.stringify(message));
            console.log('📤 Sent:', message);

            // Timeout 30s
            setTimeout(() => {
                if (this.pendingRequests.has(requestId)) {
                    this.pendingRequests.delete(requestId);
                    reject(new Error('Request timeout'));
                }
            }, 30000);
        });
    }

    handleMessage(message) {
        // Normalize properties - backend might send PascalCase or camelCase
        const normalizedMessage = {
            type: message.type || message.Type,
            requestId: message.requestId || message.RequestId,
            payload: message.payload || message.Payload || {}
        };

        console.log('🔄 Normalized message:', normalizedMessage);

        // Xử lý response cho request
        if (normalizedMessage.requestId && this.pendingRequests.has(normalizedMessage.requestId)) {
            const { resolve, reject } = this.pendingRequests.get(normalizedMessage.requestId);
            this.pendingRequests.delete(normalizedMessage.requestId);

            if (normalizedMessage.type === 'error' || normalizedMessage.type === 'auth_error') {
                console.error('❌ Server error:', normalizedMessage.type, normalizedMessage.payload);
                reject(new Error(normalizedMessage.payload.error || 'Unknown error'));
            } else {
                resolve(normalizedMessage);
            }
            return;
        }

        // Xử lý broadcast events
        switch (normalizedMessage.type) {
            case 'message_created':
                console.log('✅ Calling onMessageCreated');
                this.onMessageCreated(normalizedMessage.payload);
                break;
            case 'conversation_created':
                this.onConversationCreated(normalizedMessage.payload);
                break;
            case 'member_added':
                this.onMemberAdded(normalizedMessage.payload);
                break;
            case 'member_removed':
                this.onMemberRemoved(normalizedMessage.payload);
                break;
            case 'user_online':
                console.log('✅ Received user_online event:', normalizedMessage.payload);
                this.onUserOnline(normalizedMessage.payload);
                break;
            case 'user_offline':
                console.log('✅ Received user_offline event:', normalizedMessage.payload);
                this.onUserOffline(normalizedMessage.payload);
                break;
            case 'reaction_updated':
                this.onReactionUpdated(normalizedMessage.payload);
                break;
            default:
                console.log('Unhandled message type:', normalizedMessage.type);
        }
    }

    // API Methods
    async getConversations() {
        const response = await this.send('get_conversations', {});
        return response.payload.conversations;
    }

    async getMessages(conversationId, limit = 50, beforeSeq = null) {
        const response = await this.send('get_messages', { conversationId, limit, beforeSeq });
        return response.payload.messages;
    }

    async sendMessage(conversationId, content, messageType = 'text', clientMessageId = null, fileUrl = null, fileName = null, fileType = null, fileSize = null) {
        const effectiveClientMessageId = clientMessageId || `msg-${Date.now()}-${Math.random()}`;
        const payload = {
            conversationId,
            content,
            messageType,
            clientMessageId: effectiveClientMessageId
        };
        
        // Thêm thông tin file nếu có
        if (fileUrl) {
            payload.fileUrl = fileUrl;
            payload.fileName = fileName;
            payload.fileType = fileType;
            payload.fileSize = fileSize;
        }
        
        const response = await this.send('send_message', payload);
        return { ...response.payload, clientMessageId: effectiveClientMessageId };
    }

    async createDirect(otherUserId) {
        const response = await this.send('create_direct', { otherUserId });
        return response.payload;
    }

    // Send first message to a user without needing conversationId
    async sendDirectMessage(otherUserId, content, messageType = 'text', clientMessageId = null, fileUrl = null, fileName = null, fileType = null, fileSize = null) {
        const effectiveClientMessageId = clientMessageId || `msg-${Date.now()}-${Math.random()}`;
        const payload = {
            conversationId: '',
            otherUserId,
            content,
            messageType,
            clientMessageId: effectiveClientMessageId
        };
        
        // Thêm thông tin file nếu có
        if (fileUrl) {
            payload.fileUrl = fileUrl;
            payload.fileName = fileName;
            payload.fileType = fileType;
            payload.fileSize = fileSize;
        }
        
        const response = await this.send('send_message', payload);
        return { ...response.payload, clientMessageId: effectiveClientMessageId };
    }

    async createGroup(title, memberIds = []) {
        const response = await this.send('create_group', { title, memberIds });
        return response.payload;
    }

    async addMember(conversationId, userId) {
        const response = await this.send('add_member', { conversationId, userId });
        return response.payload;
    }

    async removeMember(conversationId, userId) {
        const response = await this.send('remove_member', { conversationId, userId });
        return response.payload;
    }

    async addReaction(conversationId, messageId, emoji) {
        const response = await this.send('add_reaction', { conversationId, messageId, emoji });
        return response.payload;
    }

    // Event Handlers (sẽ được gán từ app.js)
    onMessageCreated(payload) {
        if (window.onMessageCreated) {
            window.onMessageCreated(payload);
        }
    }

    onConversationCreated(payload) {
        if (window.onConversationCreated) {
            window.onConversationCreated(payload);
        }
    }

    onMemberAdded(payload) {
        if (window.onMemberAdded) {
            window.onMemberAdded(payload);
        }
    }

    onMemberRemoved(payload) {
        if (window.onMemberRemoved) {
            window.onMemberRemoved(payload);
        }
    }

    onUserOnline(payload) {
        // Normalize payload properties (backend sends PascalCase)
        const normalizedPayload = {
            userId: payload.userId || payload.UserId,
            displayName: payload.displayName || payload.DisplayName,
            isOnline: payload.isOnline !== undefined ? payload.isOnline : payload.IsOnline,
            lastSeenAt: payload.lastSeenAt || payload.LastSeenAt
        };
        
        console.log('🔄 Normalized user online payload:', normalizedPayload);
        
        if (window.onUserOnline) {
            window.onUserOnline(normalizedPayload);
        }
    }

    onUserOffline(payload) {
        // Normalize payload properties (backend sends PascalCase)
        const normalizedPayload = {
            userId: payload.userId || payload.UserId,
            displayName: payload.displayName || payload.DisplayName,
            isOnline: payload.isOnline !== undefined ? payload.isOnline : payload.IsOnline,
            lastSeenAt: payload.lastSeenAt || payload.LastSeenAt
        };
        
        console.log('🔄 Normalized user offline payload:', normalizedPayload);
        
        if (window.onUserOffline) {
            window.onUserOffline(normalizedPayload);
        }
    }

    onReactionUpdated(payload) {
        if (window.onReactionUpdated) {
            window.onReactionUpdated(payload);
        }
    }

    // Get online users
    async getOnlineUsers() {
        const response = await this.send('get_online_users', {});
        return response.payload.users || [];
    }

    disconnect() {
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
        this.isConnected = false;
    }

}

// Global instance
window.socketHandler = new SocketHandler();

function reactMessage(conversationId, messageId, emoji) {
    // Backward-compatible helper
    if (window.socketHandler && typeof window.socketHandler.addReaction === 'function') {
        return window.socketHandler.addReaction(conversationId, messageId, emoji);
    }
    if (window.socketHandler && typeof window.socketHandler.send === 'function') {
        return window.socketHandler.send('add_reaction', { conversationId, messageId, emoji });
    }
}
window.reactMessage = reactMessage;

function sendSticker(code) {
    if (!window.currentConversationId) return;
    if (!window.socketHandler || typeof window.socketHandler.sendMessage !== 'function') return;

    const clientMessageId = "st_" + Date.now();
    // Server expects content to be sticker code
    window.socketHandler.sendMessage(window.currentConversationId, code, 'sticker', clientMessageId);
    const picker = document.getElementById("stickerPicker");
    if (picker) picker.style.display = "none";
}

