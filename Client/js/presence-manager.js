class PresenceManager {
    constructor(userId, socket) {
        this.userId = userId;
        this.socket = socket;
        this.status = 'offline';
        this.customStatus = '';
        this.heartbeatInterval = 15000; // 15 giây
        this.heartbeatTimer = null;
        this.lastHeartbeatTime = null;
        this.conversations = new Set();
        
        this.initialize();
        console.log(`✅ PresenceManager initialized for user: ${userId}`);
    }
    
    initialize() {
        // Start heartbeat
        this.startHeartbeat();
        
        // Listen for presence updates
        this.socket.on('presence_update', (data) => {
            this.handlePresenceUpdate(data);
        });
        
        // Listen for batch presence
        this.socket.on('presence_batch', (data) => {
            this.handleBatchPresence(data);
        });
        
        // Track tab visibility
        document.addEventListener('visibilitychange', () => {
            this.handleVisibilityChange();
        });
        
        // Track window focus
        window.addEventListener('focus', () => {
            this.setStatus('online');
        });
        
        window.addEventListener('blur', () => {
            this.setStatus('away');
        });
    }
    
    startHeartbeat() {
        if (this.heartbeatTimer) {
            clearInterval(this.heartbeatTimer);
        }
        
        this.heartbeatTimer = setInterval(() => {
            this.sendHeartbeat();
        }, this.heartbeatInterval);
        
        // Send initial heartbeat
        this.sendHeartbeat();
        console.log('💓 Heartbeat started');
    }
    
    sendHeartbeat() {
        if (this.socket && this.socket.readyState === WebSocket.OPEN) {
            const heartbeat = {
                type: 'heartbeat',
                data: {
                    userId: this.userId,
                    timestamp: Date.now()
                }
            };
            
            this.socket.send(JSON.stringify(heartbeat));
            this.lastHeartbeatTime = Date.now();
            
            // Update UI
            this.updatePresenceIndicator('online');
            
            console.log('💓 Heartbeat sent');
        }
    }
    
    setStatus(status, customStatus = '') {
        if (this.status === status && this.customStatus === customStatus) {
            return;
        }
        
        this.status = status;
        this.customStatus = customStatus;
        
        // Send to server
        const update = {
            type: 'presence_update',
            data: {
                userId: this.userId,
                status: status,
                customStatus: customStatus,
                timestamp: Date.now()
            }
        };
        
        if (this.socket && this.socket.readyState === WebSocket.OPEN) {
            this.socket.send(JSON.stringify(update));
        }
        
        // Update local UI
        this.updatePresenceIndicator(status);
        
        console.log(`👤 Presence updated: ${status} ${customStatus ? '(' + customStatus + ')' : ''}`);
    }
    
    updatePresenceIndicator(status) {
        this.status = status;
        
        // Update own indicator
        const ownIndicator = document.getElementById('own-presence');
        if (ownIndicator) {
            ownIndicator.className = `presence-indicator ${status}`;
            ownIndicator.title = `${status.charAt(0).toUpperCase() + status.slice(1)}${this.customStatus ? ': ' + this.customStatus : ''}`;
            
            // Update status text
            const statusText = document.getElementById('status-text');
            if (statusText) {
                statusText.textContent = status.charAt(0).toUpperCase() + status.slice(1);
            }
        }
        
        // Update tab title
        if (status === 'online') {
            document.title = document.title.replace(/^\([^)]+\)\s*/, '');
        } else {
            const statusText = `(${status})`;
            if (!document.title.startsWith(statusText)) {
                document.title = `${statusText} ${document.title.replace(/^\([^)]+\)\s*/, '')}`;
            }
        }
    }
    
    handlePresenceUpdate(data) {
        const { userId, status, lastSeen, customStatus } = data;
        
        // Don't update own presence from server (we update locally)
        if (userId === this.userId) {
            return;
        }
        
        console.log(`📢 Presence update: ${userId} is now ${status}`);
        
        // Update in user list
        this.updateUserPresenceUI(userId, status, lastSeen, customStatus);
        
        // Update in active conversation
        this.updateConversationPresence(userId, status);
    }
    
    handleBatchPresence(data) {
        data.presences.forEach(presence => {
            const { userId, status, lastSeen, customStatus } = presence;
            
            if (userId !== this.userId) {
                this.updateUserPresenceUI(userId, status, lastSeen, customStatus);
            }
        });
    }
    
    updateUserPresenceUI(userId, status, lastSeen, customStatus = '') {
        // Update in user list
        const userElements = document.querySelectorAll(`[data-user-id="${userId}"]`);
        userElements.forEach(element => {
            const indicator = element.querySelector('.presence-indicator');
            if (indicator) {
                indicator.className = `presence-indicator ${status}`;
                indicator.title = `${status.charAt(0).toUpperCase() + status.slice(1)}${customStatus ? ': ' + customStatus : ''}\nLast seen: ${new Date(lastSeen).toLocaleString()}`;
            }
            
            // Update last seen text if exists
            const lastSeenElement = element.querySelector('.last-seen');
            if (lastSeenElement) {
                lastSeenElement.textContent = this.formatLastSeen(lastSeen);
            }
        });
        
        // Update in messages
        const messageElements = document.querySelectorAll(`[data-sender-id="${userId}"]`);
        messageElements.forEach(element => {
            const presenceDot = element.querySelector('.message-presence');
            if (presenceDot) {
                presenceDot.className = `message-presence ${status}`;
            }
        });
    }
    
    updateConversationPresence(userId, status) {
        const activeConversation = document.querySelector('.conversation.active');
        if (activeConversation) {
            const memberElement = activeConversation.querySelector(`[data-member-id="${userId}"]`);
            if (memberElement) {
                const indicator = memberElement.querySelector('.member-presence');
                if (indicator) {
                    indicator.className = `member-presence ${status}`;
                }
            }
        }
    }
    
    joinConversation(conversationId) {
        if (this.conversations.has(conversationId)) {
            return;
        }
        
        this.conversations.add(conversationId);
        
        const joinRequest = {
            type: 'join_conversation',
            data: {
                userId: this.userId,
                conversationId: conversationId
            }
        };
        
        if (this.socket && this.socket.readyState === WebSocket.OPEN) {
            this.socket.send(JSON.stringify(joinRequest));
        }
        
        console.log(`✅ Joined conversation: ${conversationId}`);
    }
    
    leaveConversation(conversationId) {
        this.conversations.delete(conversationId);
        console.log(`🚪 Left conversation: ${conversationId}`);
    }
    
    getBatchPresence(userIds) {
        const request = {
            type: 'get_presence',
            data: {
                userIds: userIds
            }
        };
        
        if (this.socket && this.socket.readyState === WebSocket.OPEN) {
            this.socket.send(JSON.stringify(request));
        }
    }
    
    handleVisibilityChange() {
        if (document.hidden) {
            // Tab không active
            this.setStatus('away', 'Away');
            console.log('👁️ Tab hidden, status changed to away');
        } else {
            // Tab active trở lại
            this.setStatus('online');
            this.sendHeartbeat(); // Gửi heartbeat ngay lập tức
            console.log('👁️ Tab visible, status changed to online');
        }
    }
    
    formatLastSeen(timestamp) {
        const now = new Date();
        const lastSeen = new Date(timestamp);
        const diffMs = now - lastSeen;
        const diffMins = Math.floor(diffMs / 60000);
        
        if (diffMins < 1) return 'Just now';
        if (diffMins < 60) return `${diffMins}m ago`;
        
        const diffHours = Math.floor(diffMins / 60);
        if (diffHours < 24) return `${diffHours}h ago`;
        
        const diffDays = Math.floor(diffHours / 24);
        if (diffDays < 7) return `${diffDays}d ago`;
        
        return lastSeen.toLocaleDateString();
    }
    
    disconnect() {
        if (this.heartbeatTimer) {
            clearInterval(this.heartbeatTimer);
            this.heartbeatTimer = null;
        }
        
        this.setStatus('offline');
        this.conversations.clear();
        
        console.log('🔌 PresenceManager disconnected');
    }
}