// ===== UI INTERACTION HANDLER (Người 2) =====

// Display incoming message from WebSocket
function displayMessage(message) {
    const messagesArea = document.getElementById('messagesArea');
    if (!messagesArea) return;

    const currentUserId = localStorage.getItem('userId') || localStorage.getItem('mockUserId');
    const isOwn = message.senderId === currentUserId;
    const senderName = message.senderDisplayName || message.senderName || message.senderId || 'User';

    const clientIdAttr = message.clientMessageId ? ` data-client-id="${message.clientMessageId}"` : '';
    const timeText = message.seq
        ? `${formatTime(message.createdAt)} • Seq: ${message.seq}`
        : formatTime(message.createdAt);
    
    const messageHTML = `
        <div class="message ${isOwn ? 'own' : ''}" data-id="${message.messageId}"${clientIdAttr}>
            ${!isOwn ? `<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">` : ''}
            <div class="message-content">
                ${!isOwn ? `<div class="message-sender">${escapeHtml(senderName)}</div>` : ''}
                <div class="message-text">${escapeHtml(message.content)}</div>
                <div class="message-time">${timeText}</div>
            </div>
            ${isOwn ? `<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">` : ''}
        </div>
    `;
    
    messagesArea.insertAdjacentHTML('beforeend', messageHTML);
    messagesArea.scrollTop = messagesArea.scrollHeight;

    // Update conversation last message
    updateConversationLastMessage(message.conversationId, message.content);
}

// Update conversation list when new message arrives
function updateConversationLastMessage(conversationId, lastMessage) {
    const convItem = document.querySelector(`.conversation-item[data-id="${conversationId}"]`);
    if (!convItem) return;

    const lastMsgEl = convItem.querySelector('.conversation-last-message');
    if (lastMsgEl) {
        lastMsgEl.textContent = lastMessage.substring(0, 30) + (lastMessage.length > 30 ? '...' : '');
    }

    const timeEl = convItem.querySelector('.conversation-time');
    if (timeEl) {
        timeEl.textContent = formatTime(new Date());
    }

    // Move to top
    const parent = convItem.parentElement;
    parent.insertBefore(convItem, parent.firstChild);
}

// Show notification (TODO: Người 3 - Integrate with presence)
function showNotification(text, type = 'info') {
    // Simple notification
    const notification = document.createElement('div');
    notification.className = `notification notification-${type}`;
    notification.textContent = text;
    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        background: ${type === 'error' ? '#fa383e' : '#0084ff'};
        color: white;
        padding: 12px 20px;
        border-radius: 8px;
        box-shadow: 0 4px 12px rgba(0,0,0,0.15);
        z-index: 10000;
        animation: slideIn 0.3s ease;
    `;
    
    document.body.appendChild(notification);
    
    setTimeout(() => {
        notification.style.animation = 'slideOut 0.3s ease';
        setTimeout(() => notification.remove(), 300);
    }, 3000);
}

// Show typing indicator (TODO: Người 3 - Presence)
function showTypingIndicator(conversationId, userName) {
    const messagesArea = document.getElementById('messagesArea');
    if (!messagesArea || currentConversationId !== conversationId) return;

    // Remove existing typing indicator
    const existing = messagesArea.querySelector('.typing-indicator');
    if (existing) existing.remove();

    const typingHTML = `
        <div class="typing-indicator">
            <div class="typing-dots">
                <span>${userName} đang nhập</span>
                <span class="dot"></span>
                <span class="dot"></span>
                <span class="dot"></span>
            </div>
        </div>
    `;
    
    messagesArea.insertAdjacentHTML('beforeend', typingHTML);
    messagesArea.scrollTop = messagesArea.scrollHeight;
}

function hideTypingIndicator() {
    const typing = document.querySelector('.typing-indicator');
    if (typing) typing.remove();
}

// Update online status (TODO: Người 3 - Presence)
function updateUserStatus(userId, isOnline) {
    console.log('User status:', userId, isOnline ? 'online' : 'offline');
    // TODO: Update UI to show online/offline status
}

// Show/hide loading spinner
function showLoading() {
    const spinner = document.createElement('div');
    spinner.id = 'loading-spinner';
    spinner.style.cssText = `
        position: fixed;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        z-index: 10000;
    `;
    spinner.innerHTML = `
        <div style="
            border: 4px solid #f3f3f3;
            border-top: 4px solid #0084ff;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
        "></div>
    `;
    document.body.appendChild(spinner);
}

function hideLoading() {
    const spinner = document.getElementById('loading-spinner');
    if (spinner) spinner.remove();
}

// Format time
function formatTime(dateTime) {
    const date = typeof dateTime === 'string' ? new Date(dateTime) : dateTime;
    return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

// Escape HTML
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Show error message
function showError(message) {
    showNotification(message, 'error');
}

// Show success message
function showSuccess(message) {
    showNotification(message, 'success');
}

// Add CSS for animations
const style = document.createElement('style');
style.textContent = `
    @keyframes slideIn {
        from {
            transform: translateX(100%);
            opacity: 0;
        }
        to {
            transform: translateX(0);
            opacity: 1;
        }
    }
    
    @keyframes slideOut {
        from {
            transform: translateX(0);
            opacity: 1;
        }
        to {
            transform: translateX(100%);
            opacity: 0;
        }
    }
    
    @keyframes spin {
        0% { transform: rotate(0deg); }
        100% { transform: rotate(360deg); }
    }
    
    .typing-indicator {
        display: flex;
        align-items: center;
        padding: 10px;
        margin: 5px 0;
    }
    
    .typing-dots {
        display: flex;
        align-items: center;
        gap: 5px;
        color: var(--text-secondary);
        font-size: 13px;
    }
    
    .typing-dots .dot {
        width: 6px;
        height: 6px;
        background: var(--text-secondary);
        border-radius: 50%;
        animation: typing 1.4s infinite;
    }
    
    .typing-dots .dot:nth-child(2) {
        animation-delay: 0.2s;
    }
    
    .typing-dots .dot:nth-child(3) {
        animation-delay: 0.4s;
    }
    
    @keyframes typing {
        0%, 60%, 100% {
            opacity: 0.3;
            transform: translateY(0);
        }
        30% {
            opacity: 1;
            transform: translateY(-10px);
        }
    }
    
    .unread-badge {
        background: var(--primary-color);
        color: white;
        border-radius: 12px;
        padding: 2px 8px;
        font-size: 11px;
        font-weight: 600;
        min-width: 20px;
        text-align: center;
        display: inline-block;
        margin-top: 4px;
    }
`;
document.head.appendChild(style);
