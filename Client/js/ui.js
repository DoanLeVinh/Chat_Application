// ===== UI INTERACTION HANDLER (Người 2) =====

function updateUploadProgressUI(clientMessageId, progress, statusText = '') {
    if (!clientMessageId) return;
    const el = document.querySelector(`.message[data-client-id="${clientMessageId}"]`);
    if (!el) return;

    const wrap = el.querySelector('.upload-progress');
    if (!wrap) return;

    const fill = wrap.querySelector('.upload-progress-fill');
    if (fill && typeof progress === 'number') {
        const safe = Math.max(0, Math.min(100, progress));
        fill.style.width = `${safe}%`;
    }

    const text = wrap.querySelector('.upload-progress-text');
    if (text) {
        const pct = typeof progress === 'number' ? `${Math.floor(progress)}%` : '';
        text.textContent = statusText ? `${statusText} ${pct}`.trim() : pct;
    }
}

function clearUploadProgressUI(clientMessageId) {
    if (!clientMessageId) return;
    const el = document.querySelector(`.message[data-client-id="${clientMessageId}"]`);
    if (!el) return;
    const wrap = el.querySelector('.upload-progress');
    if (wrap) wrap.remove();
}

// Expose for app.js
window.updateUploadProgressUI = updateUploadProgressUI;
window.clearUploadProgressUI = clearUploadProgressUI;

// Display incoming message from WebSocket
function displayMessage(message) {
    const messagesArea = document.getElementById('messagesArea');
    if (!messagesArea) return;

    const currentUserId = localStorage.getItem('userId') || localStorage.getItem('mockUserId');
    const isOwn = message.senderId === currentUserId;
    const senderName = message.senderDisplayName || message.senderName || message.senderId || 'User';

    const clientIdAttr = message.clientMessageId ? ` data-client-id="${message.clientMessageId}"` : '';
    const timeText = message.seq
        ? `${formatTime(message.createdAt)}`
        : formatTime(message.createdAt);
    
    // Helper: resolve relative /uploads/... to server absolute URL
    const resolveFileUrl = (url) => {
        if (!url) return '';
        if (url.startsWith('http://') || url.startsWith('https://')) return url;
        if (url.startsWith('/')) {
            try {
                const svc = new UploadService();
                const origin = new URL(svc.baseUrl).origin;
                return origin + url;
            } catch {
                return 'http://localhost:5000' + url;
            }
        }
        return url;
    };

    // Render file attachment nếu có
    let fileHTML = '';
    if (message.fileUrl) {
        const fileUrl = resolveFileUrl(message.fileUrl);
        // Normalized type check (backend often sends raw MIME, or we inferred it)
        const msgType = message.messageType || message.fileType || '';
        
        if (msgType === 'image' || msgType.startsWith('image/')) {
            fileHTML = `
                <div class="message-file message-image">
                    <img src="${fileUrl}" alt="${escapeHtml(message.fileName || 'Image')}" 
                         onclick="window.open('${fileUrl}', '_blank')">
                </div>
            `;
        } else if (msgType === 'video' || msgType.startsWith('video/')) {
            fileHTML = `
                <div class="message-file message-video">
                    <video controls>
                        <source src="${fileUrl}" type="${message.fileType || 'video/mp4'}">
                        Trình duyệt không hỗ trợ video.
                    </video>
                </div>
            `;
        } else {
            const uploadService = new UploadService();
            const icon = uploadService.getFileIcon(message.fileType || '');
            const size = message.fileSize ? uploadService.formatFileSize(message.fileSize) : '';
            
            fileHTML = `
                <div class="message-file">
                    <div class="message-file-item" onclick="window.open('${fileUrl}', '_blank')">
                        <div class="file-icon">${icon}</div>
                        <div class="file-info">
                            <div class="file-name">${escapeHtml(message.fileName || 'File')}</div>
                            ${size ? `<div class="file-size">${size}</div>` : ''}
                        </div>
                    </div>
                </div>
            `;
        }
    }

    // Nếu đang upload file (optimistic) -> show progress bar
    const hasUploadProgress = typeof message.uploadProgress === 'number' && message.uploadProgress >= 0 && message.uploadProgress < 100;
    const uploadStatusText = message.uploadStatusText || (message.uploadStatus === 'paused' ? 'Tạm dừng' : (message.uploadStatus === 'uploading' ? 'Đang gửi' : ''));
    const progressHTML = hasUploadProgress ? `
        <div class="upload-progress" role="progressbar" aria-valuenow="${Math.floor(message.uploadProgress)}" aria-valuemin="0" aria-valuemax="100">
            <div class="upload-progress-bar">
                <div class="upload-progress-fill" style="width: ${Math.max(0, Math.min(100, message.uploadProgress))}%;"></div>
            </div>
            <div class="upload-progress-text">${escapeHtml(uploadStatusText)} ${Math.floor(message.uploadProgress)}%</div>
        </div>
    ` : '';

    // Nếu là tin nhắn đính kèm và content chỉ là tên file thì không render phần text để tránh bị lặp
    const shouldRenderText = !!(message.content && String(message.content).trim())
        && !(message.fileUrl && message.fileName && String(message.content).trim() === String(message.fileName).trim());

    const renderReactions = (reactions) => {
        const list = Array.isArray(reactions) ? reactions : [];
        if (list.length === 0) return '';
        return list
            .filter(r => r && r.emoji)
            .map(r => {
                const count = Number(r.count || 0);
                return `<span class="reaction-chip" data-emoji="${escapeHtml(String(r.emoji))}">${escapeHtml(String(r.emoji))}${count > 1 ? ` <b>${count}</b>` : ''}</span>`;
            })
            .join('');
    };
    
    const messageHTML = `
        <div class="message ${isOwn ? 'own' : ''}" data-id="${message.messageId}" data-conversation-id="${escapeHtml(message.conversationId || '')}"${clientIdAttr}>
            ${!isOwn ? `<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">` : ''}
            <div class="message-content">
                ${!isOwn ? `<div class="message-sender">${escapeHtml(senderName)}</div>` : ''}
                ${shouldRenderText ? `<div class="message-text">${escapeHtml(message.content)}</div>` : ''}
                ${fileHTML}
                ${progressHTML}
                <div class="message-reactions">${renderReactions(message.reactions)}</div>
                <div class="message-time">${timeText}</div>
            </div>
            ${isOwn ? `<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">` : ''}
        </div>
    `;
    
    messagesArea.insertAdjacentHTML('beforeend', messageHTML);
    messagesArea.scrollTop = messagesArea.scrollHeight;

    // Update conversation last message
    const lastMsg = message.content || `📎 ${message.fileName || 'File'}`;
    updateConversationLastMessage(message.conversationId, lastMsg);
}

// Ensure app.js can reliably call window.displayMessage (history render after reload)
window.displayMessage = displayMessage;

// ===== REACTIONS UI (Long-press + dblclick) =====
function ensureReactionPicker() {
    if (document.getElementById('reactionPicker')) return;

    const picker = document.createElement('div');
    picker.id = 'reactionPicker';
    picker.className = 'reaction-picker';
    picker.style.display = 'none';
    picker.innerHTML = `
        <button type="button" class="reaction-btn" data-emoji="❤️">❤️</button>
        <button type="button" class="reaction-btn" data-emoji="👍">👍</button>
        <button type="button" class="reaction-btn" data-emoji="😂">😂</button>
        <button type="button" class="reaction-btn" data-emoji="😮">😮</button>
        <button type="button" class="reaction-btn" data-emoji="😢">😢</button>
        <button type="button" class="reaction-btn" data-emoji="😡">😡</button>
    `;
    document.body.appendChild(picker);

    const backdrop = document.createElement('div');
    backdrop.id = 'reactionPickerBackdrop';
    backdrop.className = 'reaction-picker-backdrop';
    backdrop.style.display = 'none';
    backdrop.addEventListener('click', hideReactionPicker);
    document.body.appendChild(backdrop);

    picker.addEventListener('click', async (e) => {
        const btn = e.target.closest('.reaction-btn');
        if (!btn) return;
        const emoji = btn.dataset.emoji;
        const target = picker.__targetMessageEl;
        hideReactionPicker();
        if (!target || !emoji) return;

        const messageId = target.dataset.id;
        const conversationId = target.dataset.conversationId || window.currentConversationId;
        if (!conversationId || !messageId || String(messageId).startsWith('pending:')) return;

        try {
            if (window.socketHandler?.addReaction) {
                await window.socketHandler.addReaction(conversationId, messageId, emoji);
            } else if (window.socketHandler?.send) {
                await window.socketHandler.send('add_reaction', { conversationId, messageId, emoji });
            }
        } catch (err) {
            console.error('❌ Add reaction error:', err);
            showNotification('Không thể thả cảm xúc', 'error');
        }
    });
}

function showReactionPickerForMessage(messageEl, clientX, clientY) {
    ensureReactionPicker();
    const picker = document.getElementById('reactionPicker');
    const backdrop = document.getElementById('reactionPickerBackdrop');
    if (!picker || !backdrop) return;

    picker.__targetMessageEl = messageEl;
    backdrop.style.display = 'block';
    picker.style.display = 'flex';

    // Position near pointer, keep inside viewport
    const margin = 10;
    const rect = picker.getBoundingClientRect();
    let left = clientX - rect.width / 2;
    let top = clientY - rect.height - 12;
    left = Math.max(margin, Math.min(window.innerWidth - rect.width - margin, left));
    top = Math.max(margin, Math.min(window.innerHeight - rect.height - margin, top));
    picker.style.left = `${left}px`;
    picker.style.top = `${top}px`;
}

function hideReactionPicker() {
    const picker = document.getElementById('reactionPicker');
    const backdrop = document.getElementById('reactionPickerBackdrop');
    if (picker) {
        picker.style.display = 'none';
        picker.__targetMessageEl = null;
    }
    if (backdrop) backdrop.style.display = 'none';
}

function updateMessageReactionsUI(messageId, emoji, delta = 1) {
    if (!messageId || !emoji) return;
    const el = document.querySelector(`.message[data-id="${CSS.escape(String(messageId))}"]`);
    if (!el) return;
    const container = el.querySelector('.message-reactions');
    if (!container) return;

    const safeEmoji = String(emoji);
    let chip = container.querySelector(`.reaction-chip[data-emoji="${CSS.escape(safeEmoji)}"]`);
    if (!chip) {
        chip = document.createElement('span');
        chip.className = 'reaction-chip';
        chip.dataset.emoji = safeEmoji;
        chip.innerHTML = `${escapeHtml(safeEmoji)} <b>1</b>`;
        container.appendChild(chip);
        return;
    }

    const b = chip.querySelector('b');
    const current = b ? Number(b.textContent) : 1;
    const next = Math.max(1, current + Number(delta || 0));
    if (b) {
        b.textContent = String(next);
    } else if (next > 1) {
        chip.insertAdjacentHTML('beforeend', ` <b>${next}</b>`);
    }
}

window.updateMessageReactionsUI = updateMessageReactionsUI;

function initReactionGestures() {
    const messagesArea = document.getElementById('messagesArea');
    if (!messagesArea) return;

    // Bind globally once (chat page can rerender messagesArea content)
    if (document.__reactionGesturesBound) return;
    document.__reactionGesturesBound = true;

    let timer = null;
    let target = null;
    let start = null;
    const LONG_PRESS_MS = 450;
    const MOVE_TOLERANCE = 24;

    const clear = () => {
        if (timer) clearTimeout(timer);
        timer = null;
        target = null;
        start = null;
    };

    document.addEventListener('pointerdown', (e) => {
        const msgEl = e.target.closest('.message');
        if (!msgEl) return;
        if (!messagesArea.contains(msgEl)) return;
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        if (String(msgEl.dataset.id || '').startsWith('pending:')) return;

        clear();
        target = msgEl;
        start = { x: e.clientX, y: e.clientY };

        timer = setTimeout(() => {
            // Use last known pointer position
            const x = start?.x ?? e.clientX;
            const y = start?.y ?? e.clientY;
            showReactionPickerForMessage(msgEl, x, y);
            clear();
        }, LONG_PRESS_MS);
    }, { passive: true, capture: true });

    document.addEventListener('pointermove', (e) => {
        if (!timer || !start) return;
        const dx = e.clientX - start.x;
        const dy = e.clientY - start.y;
        if (Math.hypot(dx, dy) > MOVE_TOLERANCE) {
            clear();
        }
    }, { passive: true, capture: true });

    ['pointerup', 'pointercancel', 'pointerleave'].forEach(evt => {
        document.addEventListener(evt, () => {
            clear();
        }, { passive: true, capture: true });
    });

    document.addEventListener('dblclick', async (e) => {
        const msgEl = e.target.closest('.message');
        if (!msgEl) return;
        if (!messagesArea.contains(msgEl)) return;
        clear();
        hideReactionPicker();
        const messageId = msgEl.dataset.id;
        const conversationId = msgEl.dataset.conversationId || window.currentConversationId;
        if (!conversationId || !messageId || String(messageId).startsWith('pending:')) return;

        try {
            if (window.socketHandler?.addReaction) {
                await window.socketHandler.addReaction(conversationId, messageId, '❤️');
            } else if (window.socketHandler?.send) {
                await window.socketHandler.send('add_reaction', { conversationId, messageId, emoji: '❤️' });
            }
        } catch (err) {
            console.error('❌ Double-click reaction error:', err);
        }
    }, true);
}

// Expose + run once after load
window.initReactionGestures = initReactionGestures;
window.addEventListener('load', initReactionGestures);

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

    .message-reactions {
        display: flex;
        gap: 6px;
        flex-wrap: wrap;
        margin-top: 6px;
        min-height: 18px;
    }

    .reaction-chip {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        border-radius: 999px;
        background: rgba(0,0,0,0.06);
        font-size: 13px;
        line-height: 18px;
        user-select: none;
    }

    .reaction-chip b {
        font-size: 12px;
        font-weight: 700;
    }

    .reaction-picker-backdrop {
        position: fixed;
        inset: 0;
        background: transparent;
        z-index: 20000;
    }

    .reaction-picker {
        position: fixed;
        z-index: 20001;
        display: flex;
        gap: 6px;
        padding: 8px;
        border-radius: 999px;
        background: #fff;
        box-shadow: 0 10px 30px rgba(0,0,0,0.18);
        border: 1px solid rgba(0,0,0,0.08);
    }

    .reaction-picker .reaction-btn {
        width: 34px;
        height: 34px;
        border: none;
        background: transparent;
        border-radius: 999px;
        cursor: pointer;
        font-size: 20px;
        line-height: 34px;
        transition: transform 120ms ease, background 120ms ease;
    }

    .reaction-picker .reaction-btn:hover {
        transform: translateY(-1px) scale(1.05);
        background: rgba(0,0,0,0.06);
    }
`;
document.head.appendChild(style);

const btnSticker = document.getElementById("btnSticker");
const stickerPicker = document.getElementById("stickerPicker");

btnSticker.disabled = false;

btnSticker.onclick = () => {
    stickerPicker.style.display =
        stickerPicker.style.display === "none" ? "block" : "none";
};
async function loadStickers() {
    const res = await fetch("http://localhost:5000/api/stickers");
    const stickers = await res.json();

    const grid = document.getElementById("stickerGrid");
    grid.innerHTML = "";

    stickers.forEach(sticker => {
        const img = document.createElement("img");

        img.src = "http://localhost:5000" + sticker.imageUrl; // ✅ QUAN TRỌNG
        img.title = sticker.code;
        img.className = "sticker-item";

        img.onclick = () => sendSticker(sticker.code);

        grid.appendChild(img);
    });
}

loadStickers();
