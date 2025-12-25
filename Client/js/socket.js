// ===== SOCKET CONNECTION HANDLER (Người 2 - Message events) =====
// NOTE: Người 1 sẽ implement auth, Người 3 sẽ implement reconnect/resume

class SocketHandler {
    constructor(serverUrl) {
        this.serverUrl = serverUrl;
        this.socket = null;
        this.isConnected = false;
        this.messageHandlers = {};
        this.requestCallbacks = new Map();
    }
    
    connect() {
        try {
            // TODO: Người 1 - Implement WebSocket with auth token
            // TODO: Người 3 - Implement reconnect logic
            
            console.log('Connecting to WebSocket...', this.serverUrl);
            
            // Mock connection for now
            this.isConnected = true;
            this.setupEventHandlers();
            
            console.log('✅ WebSocket connected (mock mode)');
        } catch (error) {
            console.error('WebSocket connection error:', error);
            this.isConnected = false;
        }
    }
    
    disconnect() {
        if (this.socket) {
            this.socket.close();
            this.socket = null;
        }
        this.isConnected = false;
        console.log('WebSocket disconnected');
    }
    
    setupEventHandlers() {
        // TODO: Người 1 - Real WebSocket event handlers
        
        // Register message type handlers (Người 2)
        this.on('message_created', this.handleMessageCreated.bind(this));
        this.on('conversation_created', this.handleConversationCreated.bind(this));
        this.on('member_added', this.handleMemberAdded.bind(this));
        this.on('member_removed', this.handleMemberRemoved.bind(this));
        
        // TODO: Người 3 - presence_update, resume handlers
        // TODO: Người 4 - reaction_updated, pin_updated handlers
    }
    
    // ===== SEND EVENTS (Người 2) =====
    
    sendMessage(conversationId, content, messageType = 'text') {
        const clientMessageId = this.generateUUID();
        const requestId = this.generateUUID();
        
        const event = {
            type: 'send_message',
            requestId: requestId,
            payload: {
                conversationId: conversationId,
                clientMessageId: clientMessageId,
                messageType: messageType,
                content: content
            }
        };
        
        console.log('📤 Sending message:', event);
        
        // TODO: Người 1 - Real WebSocket send
        // this.socket.send(JSON.stringify(event));
        
        // Mock: Return promise
        return new Promise((resolve, reject) => {
            this.requestCallbacks.set(requestId, { resolve, reject });
            
            // Mock response after delay
            setTimeout(() => {
                const callback = this.requestCallbacks.get(requestId);
                if (callback) {
                    callback.resolve({
                        messageId: 'msg-' + Date.now(),
                        seq: Math.floor(Math.random() * 1000)
                    });
                    this.requestCallbacks.delete(requestId);
                }
            }, 100);
        });
    }
    
    createGroup(title, memberIds = []) {
        const requestId = this.generateUUID();
        
        const event = {
            type: 'create_group',
            requestId: requestId,
            payload: {
                title: title,
                memberIds: memberIds
            }
        };
        
        console.log('📤 Creating group:', event);
        
        // TODO: Người 1 - Real WebSocket send
        
        return new Promise((resolve, reject) => {
            this.requestCallbacks.set(requestId, { resolve, reject });
        });
    }
    
    addMember(conversationId, userId) {
        const requestId = this.generateUUID();
        
        const event = {
            type: 'add_member',
            requestId: requestId,
            payload: {
                conversationId: conversationId,
                userId: userId
            }
        };
        
        console.log('📤 Adding member:', event);
        
        // TODO: Người 1 - Real WebSocket send
        
        return new Promise((resolve, reject) => {
            this.requestCallbacks.set(requestId, { resolve, reject });
        });
    }
    
    removeMember(conversationId, userId) {
        const requestId = this.generateUUID();
        
        const event = {
            type: 'remove_member',
            requestId: requestId,
            payload: {
                conversationId: conversationId,
                userId: userId
            }
        };
        
        console.log('📤 Removing member:', event);
        
        // TODO: Người 1 - Real WebSocket send
        
        return new Promise((resolve, reject) => {
            this.requestCallbacks.set(requestId, { resolve, reject });
        });
    }
    
    // ===== RECEIVE EVENTS (Người 2) =====
    
    handleMessageCreated(payload) {
        console.log('📥 Message created:', payload);
        
        // Display message in UI
        if (typeof displayMessage === 'function') {
            displayMessage({
                messageId: payload.messageId,
                conversationId: payload.conversationId,
                senderId: payload.senderId,
                content: payload.content,
                createdAt: payload.createdAt,
                seq: payload.seq
            });
        }
        
        // Play notification sound (TODO: Người 3 - Presence)
        // this.playNotificationSound();
    }
    
    handleConversationCreated(payload) {
        console.log('📥 Conversation created:', payload);
        
        // Reload conversation list
        if (typeof loadConversations === 'function') {
            loadConversations();
        }
        
        if (typeof showSuccess === 'function') {
            showSuccess('Đã tạo cuộc trò chuyện mới');
        }
    }
    
    handleMemberAdded(payload) {
        console.log('📥 Member added:', payload);
        
        // Reload members if viewing this conversation
        if (currentConversationId === payload.conversationId) {
            // TODO: Reload member list
        }
        
        if (typeof showNotification === 'function') {
            showNotification('Thành viên mới đã được thêm vào nhóm');
        }
    }
    
    handleMemberRemoved(payload) {
        console.log('📥 Member removed:', payload);
        
        // Reload members if viewing this conversation
        if (currentConversationId === payload.conversationId) {
            // TODO: Reload member list
        }
        
        if (typeof showNotification === 'function') {
            showNotification('Thành viên đã rời khỏi nhóm');
        }
    }
    
    // ===== EVENT HANDLER REGISTRATION =====
    
    on(eventType, handler) {
        if (!this.messageHandlers[eventType]) {
            this.messageHandlers[eventType] = [];
        }
        this.messageHandlers[eventType].push(handler);
    }
    
    onMessage(callback) {
        this.on('message_created', callback);
    }
    
    // ===== UTILITIES =====
    
    generateUUID() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
}

// Global socket instance
let socketHandler = null;

// Initialize socket (called from app.js)
function initWebSocket() {
    // TODO: Người 1 - Get server URL from config
    const serverUrl = 'ws://localhost:5000/ws'; // Mock URL
    
    socketHandler = new SocketHandler(serverUrl);
    socketHandler.connect();
    
    return socketHandler;
}

// Export for use in app.js
if (typeof window !== 'undefined') {
    window.socketHandler = socketHandler;
    window.initWebSocket = initWebSocket;
}
