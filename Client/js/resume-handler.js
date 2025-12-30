class ResumeHandler {
    constructor(userId) {
        this.userId = userId;
        this.resumeToken = localStorage.getItem('resume_token');
        this.sessionId = localStorage.getItem('session_id');
        this.deviceId = this.getOrCreateDeviceId();
        this.lastSequenceByConversation = this.loadSequenceState();
        this.isResuming = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;
        this.reconnectDelay = 1000; // 1 giây
        this.missedMessagesQueue = [];
        
        console.log(`✅ ResumeHandler initialized for user: ${userId}`);
        console.log(`   Device ID: ${this.deviceId}`);
        console.log(`   Resume Token: ${this.resumeToken ? 'Present' : 'Not found'}`);
    }
    
    getOrCreateDeviceId() {
        let deviceId = localStorage.getItem('device_id');
        if (!deviceId) {
            deviceId = 'web_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
            localStorage.setItem('device_id', deviceId);
        }
        return deviceId;
    }
    
    loadSequenceState() {
        try {
            const saved = localStorage.getItem('last_sequence_by_conversation');
            return saved ? JSON.parse(saved) : {};
        } catch (e) {
            console.error('❌ Error loading sequence state:', e);
            return {};
        }
    }
    
    saveSequenceState() {
        localStorage.setItem(
            'last_sequence_by_conversation',
            JSON.stringify(this.lastSequenceByConversation)
        );
    }
    
    updateSequence(conversationId, sequence) {
        const currentSeq = this.lastSequenceByConversation[conversationId] || 0;
        if (sequence > currentSeq) {
            this.lastSequenceByConversation[conversationId] = sequence;
            this.saveSequenceState();
            console.log(`📝 Updated sequence for ${conversationId}: ${sequence}`);
        }
    }
    
    async attemptResume(socket) {
        if (!this.resumeToken || this.isResuming) {
            console.log('⚠️ Cannot resume: no token or already resuming');
            return false;
        }
        
        this.isResuming = true;
        this.showNotification('Attempting to resume connection...', 'info');
        console.log('🔄 Attempting to resume connection...');
        
        return new Promise((resolve) => {
            // Set timeout cho resume attempt
            const resumeTimeout = setTimeout(() => {
                this.handleResumeTimeout(socket);
                resolve(false);
            }, 10000);
            
            // Handler cho resume response
            const resumeHandler = (event) => {
                try {
                    const data = JSON.parse(event.data);
                    clearTimeout(resumeTimeout);
                    
                    if (data.type === 'resume_success') {
                        this.handleResumeSuccess(data.data, socket);
                        resolve(true);
                    } else if (data.type === 'resume_error') {
                        this.handleResumeError(data.data);
                        resolve(false);
                    }
                } catch (e) {
                    console.error('❌ Error parsing resume response:', e);
                }
            };
            
            // Listen for resume response
            socket.addEventListener('message', resumeHandler);
            
            // Send resume request
            const resumeRequest = {
                type: 'resume',
                data: {
                    userId: this.userId,
                    resumeToken: this.resumeToken,
                    deviceId: this.deviceId,
                    sinceSeqByConversation: this.lastSequenceByConversation
                }
            };
            
            socket.send(JSON.stringify(resumeRequest));
            console.log('📨 Sent resume request');
        });
    }
    
    handleResumeSuccess(data, socket) {
        console.log('✅ Resume successful:', data);
        
        // Save new resume token
        if (data.resumeToken) {
            this.resumeToken = data.resumeToken;
            localStorage.setItem('resume_token', data.resumeToken);
            console.log('💾 Saved new resume token');
        }
        
        // Reset reconnect attempts
        this.reconnectAttempts = 0;
        this.isResuming = false;
        
        // Show success notification
        this.showNotification(`Connection resumed successfully. Received ${data.messagesReceived || 0} missed messages.`, 'success');
        
        console.log(`🔄 Resume completed. Messages received: ${data.messagesReceived || 0}`);
    }
    
    handleResumeError(data) {
        console.error('❌ Resume failed:', data);
        
        // Clear invalid token
        this.resumeToken = null;
        localStorage.removeItem('resume_token');
        localStorage.removeItem('session_id');
        
        this.isResuming = false;
        
        // Show error notification
        this.showNotification(
            'Could not resume connection. Please refresh the page.',
            'error'
        );
        
        console.log('🧹 Cleared invalid resume tokens');
    }
    
    handleResumeTimeout(socket) {
        console.warn('⏰ Resume attempt timed out');
        this.isResuming = false;
        
        // Force reconnection
        if (socket && socket.readyState === WebSocket.OPEN) {
            socket.close();
        }
        this.attemptReconnect();
    }
    
    handleServerGoingDown(data) {
        console.log('⚠️ Server going down:', data);
        
        this.showNotification(
            `Server is going down for maintenance. Will reconnect in ${data.reconnectAfter} seconds.`,
            'warning'
        );
        
        // Schedule reconnect
        setTimeout(() => {
            this.attemptReconnect();
        }, data.reconnectAfter * 1000);
        
        console.log(`⏰ Scheduled reconnect in ${data.reconnectAfter} seconds`);
    }
    
    handleConnectionClosed(data) {
        console.log('🔌 Connection closed by server:', data);
        
        if (data.code === 1000) { // Normal closure
            this.showNotification(
                `Connection closed: ${data.reason}`,
                'info'
            );
        } else {
            this.showNotification(
                'Connection lost. Attempting to reconnect...',
                'warning'
            );
            this.attemptReconnect();
        }
    }
    
    attemptReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            this.showNotification(
                'Failed to reconnect after multiple attempts. Please refresh the page.',
                'error'
            );
            console.log('❌ Max reconnect attempts reached');
            return;
        }
        
        this.reconnectAttempts++;
        
        // Exponential backoff
        const delay = this.reconnectDelay * Math.pow(2, this.reconnectAttempts - 1);
        
        this.showNotification(
            `Attempting to reconnect (${this.reconnectAttempts}/${this.maxReconnectAttempts})...`,
            'info'
        );
        
        console.log(`🔄 Reconnect attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts} in ${delay}ms`);
        
        setTimeout(() => {
            if (window.connectToServer) {
                window.connectToServer();
            } else {
                console.error('❌ connectToServer function not found');
            }
        }, delay);
    }
    
    showNotification(message, type = 'info') {
        // Create notification element
        const notification = document.createElement('div');
        notification.className = `notification ${type}`;
        notification.innerHTML = `
            <div class="notification-content">${message}</div>
            <button class="notification-close">&times;</button>
        `;
        
        // Add to container
        const container = document.getElementById('notifications') || this.createNotificationContainer();
        container.appendChild(notification);
        
        // Auto remove after 5 seconds
        setTimeout(() => {
            if (notification.parentNode) {
                notification.remove();
            }
        }, 5000);
        
        // Close button handler
        notification.querySelector('.notification-close').addEventListener('click', () => {
            notification.remove();
        });
        
        console.log(`📢 Notification: ${type} - ${message}`);
    }
    
    createNotificationContainer() {
        const container = document.createElement('div');
        container.id = 'notifications';
        container.className = 'notification-container';
        document.body.appendChild(container);
        return container;
    }
    
    showLoadMoreOption() {
        const loadMoreBtn = document.createElement('button');
        loadMoreBtn.className = 'load-more-btn';
        loadMoreBtn.textContent = 'Load older messages';
        loadMoreBtn.onclick = () => {
            // TODO: Implement load more messages
            loadMoreBtn.remove();
        };
        
        const messageContainer = document.querySelector('.messages-container');
        if (messageContainer) {
            messageContainer.prepend(loadMoreBtn);
        }
    }
    
    // Lưu session khi authenticate thành công
    saveSession(sessionId, resumeToken) {
        this.sessionId = sessionId;
        this.resumeToken = resumeToken;
        
        localStorage.setItem('session_id', sessionId);
        localStorage.setItem('resume_token', resumeToken);
        
        console.log(`💾 Session saved: ${sessionId}`);
    }
    
    // Xử lý incoming message và update sequence
    handleIncomingMessage(message) {
        if (message.conversationId && message.sequence) {
            this.updateSequence(message.conversationId, message.sequence);
        }
    }
    
    // Clear all stored data
    clearAllData() {
        localStorage.removeItem('session_id');
        localStorage.removeItem('resume_token');
        localStorage.removeItem('last_sequence_by_conversation');
        localStorage.removeItem('device_id');
        
        this.sessionId = null;
        this.resumeToken = null;
        this.lastSequenceByConversation = {};
        this.reconnectAttempts = 0;
        
        console.log('🧹 All resume data cleared');
    }
}