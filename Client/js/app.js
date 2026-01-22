// ===== LOGIN/REGISTER HANDLING (Auth API) =====
// Cấu hình SERVER_HOST cho LAN testing (sửa IP này thành IP máy chạy server)
window.SERVER_HOST = 'localhost'; // Đổi thành '10.178.14.217' để test LAN

const API_URL = `http://${window.SERVER_HOST}:5000/api/auth`;

document.addEventListener('DOMContentLoaded', () => {
    if (window.location.pathname.includes('index.html') || window.location.pathname === '/') {
        initLoginPage();
    } else if (window.location.pathname.includes('auth.html')) {
        initAuthPage();
    } else if (window.location.pathname.includes('chat.html')) {
        initChatPage();
    }
});

function initAuthPage() {
    const tabs = document.querySelectorAll('.tab');
    const loginForm = document.getElementById('loginForm');
    const registerForm = document.getElementById('registerForm');
    const messageEl = document.getElementById('message');

    // Tab switching
    tabs?.forEach(tab => {
        tab.addEventListener('click', () => {
            const tabName = tab.dataset.tab;
            tabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            
            if (tabName === 'login') {
                loginForm.classList.add('active');
                registerForm.classList.remove('active');
            } else {
                registerForm.classList.add('active');
                loginForm.classList.remove('active');
            }
            hideMessage();
        });
    });

    // Login submit
    loginForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const email = document.getElementById('loginEmail').value.trim();
        const password = document.getElementById('loginPassword').value;

        if (!email || !password) {
            showAuthMessage('Vui lòng điền đầy đủ thông tin.', 'error');
            return;
        }

        try {
            const response = await fetch(`${API_URL}/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password })
            });
            
            const data = await response.json();
            
            if (data.success) {
                localStorage.setItem('token', data.token);
                localStorage.setItem('user', JSON.stringify(data.user));
                localStorage.setItem('userId', data.user.id);
                localStorage.setItem('userName', data.user.displayName);
                showAuthMessage('Đăng nhập thành công!', 'success');
                setTimeout(() => window.location.href = 'chat.html', 1000);
            } else {
                showAuthMessage(data.message || 'Đăng nhập thất bại.', 'error');
            }
        } catch (error) {
            showAuthMessage('Không thể kết nối đến server.', 'error');
            console.error('Login error:', error);
        }
    });

    // Register submit
    registerForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const displayName = document.getElementById('registerName').value.trim();
        const email = document.getElementById('registerEmail').value.trim();
        const password = document.getElementById('registerPassword').value;
        const confirmPassword = document.getElementById('confirmPassword').value;

        if (!displayName || !email || !password) {
            showAuthMessage('Vui lòng điền đầy đủ thông tin.', 'error');
            return;
        }

        if (password !== confirmPassword) {
            showAuthMessage('Mật khẩu xác nhận không khớp.', 'error');
            return;
        }

        try {
            const response = await fetch(`${API_URL}/register`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ displayName, email, password })
            });
            
            const data = await response.json();
            
            if (data.success) {
                showAuthMessage('Đăng ký thành công! Đang chuyển...', 'success');
                setTimeout(() => {
                    tabs[0]?.click();
                    document.getElementById('loginEmail').value = email;
                }, 1500);
            } else {
                showAuthMessage(data.message || 'Đăng ký thất bại.', 'error');
            }
        } catch (error) {
            showAuthMessage('Không thể kết nối đến server.', 'error');
            console.error('Register error:', error);
        }
    });

    function showAuthMessage(text, type) {
        if (messageEl) {
            messageEl.textContent = text;
            messageEl.className = 'message ' + type;
        }
    }

    function hideMessage() {
        if (messageEl) {
            messageEl.className = 'message';
            messageEl.textContent = '';
        }
    }
}

function initLoginPage() {
    // Redirect to auth page
    window.location.href = 'auth.html';
}


// ===== CHAT PAGE INITIALIZATION// Global state
let currentConversationId = null;
// Expose for ui.js helpers (top-level `let` doesn't become window property)
window.currentConversationId = null;
let currentConversations = [];
let onlineUsers = new Set();
let userLastSeen = new Map(); // Store last seen timestamps for offline users

// ===== INITIALIZATION (Người 1) =====users

async function initChatPage() {
    // Check auth - dùng token thay vì userId
    const token = localStorage.getItem('token');
    const userId = localStorage.getItem('userId');
    if (!token || !userId) {
        window.location.href = 'auth.html';
        return;
    }

    // Update user info
    const userName = localStorage.getItem('userName') || 'User';
    document.getElementById('userName').textContent = userName;

    // Setup event listeners
    setupEventListeners();

    try {
        // Kết nối WebSocket với server thật
        await window.socketHandler.connect(userId);
        console.log('✅ Connected to server');

        // Load conversations từ server
        // Setup WebSocket event handlers
        setupWebSocketHandlers();
        
        // Fetch initial online users FIRST (before loading conversations)
        await loadOnlineUsers();
        
        // Then load conversations (will populate lastSeen for OFFLINE users only)
        await loadConversations();
        
        // Setup UI components
        setupSearchBox();
        setupFileUpload();
        
        showNotification('Đã kết nối server', 'success');
    } catch (error) {
        console.error('❌ Connection error:', error);
        showNotification('Không thể kết nối server. Vui lòng thử lại.', 'error');
    }
}

function setupEventListeners() {
    // Logout
    document.getElementById('btnLogout')?.addEventListener('click', () => {
        localStorage.clear();
        window.location.href = 'index.html';
    });

    // Create group button
    document.getElementById('btnCreateGroup')?.addEventListener('click', () => {
        openCreateGroupModal();
    });

    // File attachment
    const fileInput = document.getElementById('fileInput');
    const btnClosePreview = document.getElementById('btnClosePreview');

    fileInput?.addEventListener('change', handleFileSelection);
    btnClosePreview?.addEventListener('click', clearFileSelection);

    // Auto pause/resume upload on network changes
    window.addEventListener('offline', () => {
        for (const [clientMessageId] of pendingUploads) {
            if (typeof window.updateUploadProgressUI === 'function') {
                window.updateUploadProgressUI(clientMessageId, null, 'Mất mạng • Tạm dừng');
            }
        }
        showNotification('Mất kết nối mạng. Upload sẽ tạm dừng.', 'error');
    });

    window.addEventListener('online', () => {
        resumePendingUploads();
    });

    // Message send
    document.getElementById('btnSend')?.addEventListener('click', sendMessage);
    document.getElementById('messageInput')?.addEventListener('keypress', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    // Chat info toggle
    document.getElementById('btnChatInfo')?.addEventListener('click', () => {
        const panel = document.getElementById('rightPanel');
        if (panel) {
            const willOpen = panel.style.display === 'none';
            panel.style.display = willOpen ? 'flex' : 'none';
            if (willOpen) {
                refreshGroupInfoPanel();
            }
        }
    });

    // Close panel
    document.getElementById('btnClosePanel')?.addEventListener('click', () => {
        document.getElementById('rightPanel').style.display = 'none';
    });

    // Add member (in right panel)
    document.getElementById('btnAddMember')?.addEventListener('click', () => {
        openAddMemberModal();
    });

    // Setup add member modal once
    setupAddMemberModal();

    // Tab switching
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            // TODO: Filter conversations by tab
        });
    });

    // Setup search box
    setupSearchBox();
}

// ===== WEBSOCKET EVENT HANDLERS (Người 2) =====
function setupWebSocketHandlers() {
    // Handle incoming messages
    window.onMessageCreated = (payload) => {
        console.log('📨 Message received:', payload);
        console.log('📍 Current conversation:', currentConversationId);
        console.log('📍 Message conversation:', payload.conversationId);

        const resolveSenderId = (p) => {
            const raw = p?.senderId ?? p?.SenderId ?? p?.senderUserId ?? p?.SenderUserId ?? p?.fromUserId ?? p?.FromUserId ?? p?.userId ?? p?.UserId ?? p?.sender?.id ?? p?.sender?.Id ?? '';
            return String(raw || '').trim();
        };
        const resolveSenderName = (p) => {
            return p?.senderDisplayName || p?.SenderDisplayName || p?.senderName || p?.SenderName || p?.sender?.displayName || p?.sender?.DisplayName || resolveSenderId(p);
        };
        const senderId = resolveSenderId(payload);

        // If this is our own optimistic message, reconcile the pending UI element
        const currentUserId = String(localStorage.getItem('userId') || '').trim();
        if (payload && payload.clientMessageId && senderId && senderId === currentUserId) {
            console.log('🔄 Reconciling optimistic message:', payload.clientMessageId);
            const pendingEl = document.querySelector(`.message[data-client-id="${payload.clientMessageId}"]`);
            if (pendingEl) {
                pendingEl.dataset.id = payload.messageId;
                pendingEl.classList.remove('pending');

                // Remove upload progress UI if any
                if (typeof window.clearUploadProgressUI === 'function') {
                    window.clearUploadProgressUI(payload.clientMessageId);
                }

                const timeEl = pendingEl.querySelector('.message-time');
                if (timeEl) {
                    const timeText = payload.createdAt ? formatTime(payload.createdAt) : formatTime(new Date().toISOString());
                    timeEl.textContent = payload.seq ? `${timeText}` : timeText;
                }

                // If this message has attachment info, ensure UI shows it
                if (payload.fileUrl) {
                    const contentEl = pendingEl.querySelector('.message-content');
                    if (contentEl && !contentEl.querySelector('.message-file')) {
                        const safeUrl = payload.fileUrl;
                        let attachmentHTML = '';
                        if (payload.messageType === 'image') {
                            attachmentHTML = `
                                <div class="message-file message-image">
                                    <img src="${safeUrl}" alt="${escapeHtml(payload.fileName || 'Image')}" onclick="window.open('${safeUrl}', '_blank')">
                                    <div class="message-file-actions">
                                        <a href="${safeUrl}" class="file-download-btn" download>Tải xuống</a>
                                    </div>
                                </div>
                            `;
                        } else if (payload.messageType === 'video') {
                            attachmentHTML = `
                                <div class="message-file message-video">
                                    <video controls>
                                        <source src="${safeUrl}" type="${payload.fileType || 'video/mp4'}">
                                    </video>
                                    <div class="message-file-actions">
                                        <a href="${safeUrl}" class="file-download-btn" download>Tải xuống</a>
                                    </div>
                                </div>
                            `;
                        } else {
                            const uploadSvc = new UploadService();
                            const icon = uploadSvc.getFileIcon(payload.fileType || '');
                            const size = payload.fileSize ? uploadSvc.formatFileSize(payload.fileSize) : '';
                            attachmentHTML = `
                                <div class="message-file">
                                    <div class="message-file-item">
                                        <div class="file-main" onclick="window.open('${safeUrl}', '_blank')">
                                            <div class="file-icon">${icon}</div>
                                            <div class="file-info">
                                                <div class="file-name">${escapeHtml(payload.fileName || 'File')}</div>
                                                ${size ? `<div class=\"file-size\">${size}</div>` : ''}
                                            </div>
                                        </div>
                                        <a href="${safeUrl}" class="file-download-btn" download>
                                            Tải xuống
                                        </a>
                                    </div>
                                </div>
                            `;
                        }

                        // Remove duplicate text if it equals fileName
                        const textEl = contentEl.querySelector('.message-text');
                        if (textEl && payload.fileName && textEl.textContent && textEl.textContent.trim() === String(payload.fileName).trim()) {
                            textEl.remove();
                        }

                        const timeNode = contentEl.querySelector('.message-time');
                        if (timeNode) {
                            timeNode.insertAdjacentHTML('beforebegin', attachmentHTML);
                        } else {
                            contentEl.insertAdjacentHTML('beforeend', attachmentHTML);
                        }
                    }
                }
                console.log('✅ Optimistic message reconciled');
            } else {
                console.log('⚠️ Pending element not found, but this is our message - skipping display');
            }
            return; // CRITICAL: Always return early for our own messages to avoid duplicate
        }

        
        // Nếu đang mở conversation này, hiển thị message từ người khác
        if (payload.conversationId === currentConversationId) {
            console.log('✅ Same conversation, displaying message');
            // ui.js expects a single argument
            if (typeof window.displayMessage === 'function') {
                // Avoid duplicate if we already rendered optimistic version
                if (payload.clientMessageId) {
                    const alreadyRendered = document.querySelector(`.message[data-client-id="${payload.clientMessageId}"]`);
                    if (alreadyRendered) {
                        console.log('⚠️ Message already rendered (optimistic)');
                        return;
                    }
                }
                console.log('📝 Calling displayMessage...');
                window.displayMessage(payload);
            } else {
                console.error('❌ window.displayMessage not found!');
            }
        } else {
            console.log('⚠️ Different conversation, not displaying');
        }
        
        // Update conversation list
        updateConversationLastMessage(payload.conversationId, payload.content);
        
        // Show notification nếu không phải conversation hiện tại
        if (payload.conversationId !== currentConversationId) {
            const fromName = resolveSenderName(payload);
            showNotification(`Tin nhắn mới từ ${fromName}`, 'info');
        }
    };

    // Handle new conversation
    window.onConversationCreated = (payload) => {
        console.log('🆕 Conversation created:', payload);
        loadConversations(); // Reload conversation list
        const currentUserId = localStorage.getItem('userId');
        const title = payload?.title || 'cuộc trò chuyện';
        if (payload?.type === 'group') {
            if (payload?.createdBy && payload.createdBy === currentUserId) {
                showNotification(`Đã tạo nhóm: ${title}`, 'success');
            } else {
                showNotification(`Bạn được thêm vào nhóm: ${title}`, 'info');
            }
        } else {
            showNotification('Có cuộc trò chuyện mới', 'info');
        }
    };

    // Handle member added
    window.onMemberAdded = (payload) => {
        console.log('➕ Member added:', payload);
        try {
            const conversationId = payload?.conversationId;
            if (!conversationId) return;

            const conv = currentConversations.find(c => c.conversationId === conversationId);
            if (conv) {
                const u = payload.user || {};
                const userId = u.id || payload.userId;
                if (userId && Array.isArray(conv.members)) {
                    const exists = conv.members.some(m => m.id === userId);
                    if (!exists) {
                        const normalized = normalizeMember({
                            id: userId,
                            displayName: u.displayName || userId,
                            role: payload.role || 'member',
                            joinedAt: payload.joinedAt || new Date().toISOString(),
                            isOnline: !!u.isOnline,
                            lastSeenAt: u.lastSeenAt || null,
                            avatarUrl: u.avatarUrl || null
                        });
                        if (normalized) conv.members.push(normalized);
                    }
                }
                if (payload.membersVersion != null) {
                    conv.membersVersion = payload.membersVersion;
                }
            }

            // Update header count if current
            if (conversationId === currentConversationId) {
                const chatMembers = document.getElementById('chatMembers');
                if (chatMembers) {
                    const conv2 = currentConversations.find(c => c.conversationId === conversationId);
                    const memberCount = conv2?.members?.length || 0;
                    chatMembers.textContent = `${memberCount} thành viên`;
                }
            }

            if (isRightPanelOpen() && conversationId === currentConversationId) {
                refreshGroupInfoPanel();
            }

            const actorName = payload?.addedBy?.displayName || payload?.addedBy?.id;
            const addedName = payload?.user?.displayName || payload?.user?.id || payload?.userId;
            if (actorName && addedName) {
                showNotification(`${actorName} đã thêm ${addedName} vào nhóm`, 'info');
            }
        } catch (e) {
            console.error('❌ onMemberAdded handler error:', e);
        }
    };

    // Handle member removed
    window.onMemberRemoved = (payload) => {
        console.log('➖ Member removed:', payload);
        try {
            const conversationId = payload?.conversationId;
            const removedUserId = payload?.userId;
            if (!conversationId || !removedUserId) return;

            const conv = currentConversations.find(c => c.conversationId === conversationId);
            if (conv && Array.isArray(conv.members)) {
                conv.members = conv.members.filter(m => m.id !== removedUserId);
                if (payload.membersVersion != null) {
                    conv.membersVersion = payload.membersVersion;
                }
            }

            // Update header count if current
            if (conversationId === currentConversationId) {
                const chatMembers = document.getElementById('chatMembers');
                if (chatMembers) {
                    const conv2 = currentConversations.find(c => c.conversationId === conversationId);
                    const memberCount = conv2?.members?.length || 0;
                    chatMembers.textContent = `${memberCount} thành viên`;
                }
            }

            if (isRightPanelOpen() && conversationId === currentConversationId) {
                refreshGroupInfoPanel();
            }

            const actorName = payload?.removedBy?.displayName || payload?.removedBy?.id;
            if (actorName) {
                showNotification(`${actorName} đã xóa một thành viên khỏi nhóm`, 'info');
            }
        } catch (e) {
            console.error('❌ onMemberRemoved handler error:', e);
        }
    };

    // Handle kicked (current user removed)
    window.onKicked = (payload) => {
        try {
            const conversationId = payload?.conversationId;
            if (!conversationId) return;

            // Remove from local list
            currentConversations = currentConversations.filter(c => c.conversationId !== conversationId);
            displayConversations(currentConversations);

            // If currently open, close it
            if (currentConversationId === conversationId) {
                currentConversationId = null;
                window.currentConversationId = null;
                document.getElementById('activeChat').style.display = 'none';
                document.getElementById('emptyChat').style.display = 'flex';
                document.getElementById('rightPanel').style.display = 'none';
            }

            const actorName = payload?.removedBy?.displayName || payload?.removedBy?.id;
            showNotification(actorName ? `Bạn đã bị xóa khỏi nhóm bởi ${actorName}` : 'Bạn đã bị xóa khỏi nhóm', 'error');
        } catch (e) {
            console.error('❌ onKicked handler error:', e);
        }
    };

    // Handle user online
    window.onUserOnline = (payload) => {
        console.log('📶 User online event received:', payload);
        console.log('📶 Adding to onlineUsers:', payload.userId);
        if (payload && payload.userId) {
            onlineUsers.add(payload.userId);
            userLastSeen.delete(payload.userId); // Clear last seen when online
            console.log('📶 Online users now:', Array.from(onlineUsers));
            updateOnlineIndicators();
            // Show notification if not self
            const currentUserId = localStorage.getItem('userId');
            if (payload.userId !== currentUserId) {
                showNotification(`${payload.displayName || payload.userId} đang online`, 'info');
            }
        }
    };

    // Handle user offline
    window.onUserOffline = (payload) => {
        console.log('📴 User offline event received:', payload);
        console.log('📴 Removing from onlineUsers:', payload.userId);
        if (payload && payload.userId) {
            onlineUsers.delete(payload.userId);
            // Save last seen time
            if (payload.lastSeenAt) {
                userLastSeen.set(payload.userId, payload.lastSeenAt);
                console.log('📴 Saved last seen for', payload.userId, ':', payload.lastSeenAt);
            }
            console.log('📴 Online users now:', Array.from(onlineUsers));
            updateOnlineIndicators();
        }
    };

    // Handle reaction updates
    window.onReactionUpdated = (payload) => {
        try {
            const conversationId = payload?.conversationId || payload?.ConversationId;
            const messageId = payload?.messageId || payload?.MessageId;
            const emoji = payload?.emoji || payload?.Emoji;
            if (!conversationId || !messageId || !emoji) return;
            if (conversationId !== currentConversationId) return;
            if (typeof window.updateMessageReactionsUI === 'function') {
                window.updateMessageReactionsUI(messageId, emoji, 1);
            }
        } catch (e) {
            console.error('reaction_updated handler error:', e);
        }
    };
}

// ===== CONVERSATION MANAGEMENT (Người 2) =====
async function loadConversations() {
    try {
        console.log('🔄 Loading conversations...');
        const conversations = await window.socketHandler.getConversations();
        console.log('📋 Loaded conversations:', conversations);
        console.log('📊 Total conversations:', conversations.length);
        
        if (!Array.isArray(conversations)) {
            console.error('❌ Invalid conversations data:', conversations);
            throw new Error('Invalid conversations format');
        }
        
        currentConversations = conversations;
        
        // Populate lastSeenAt from conversation members
        populateLastSeenFromConversations(conversations);
        
        displayConversations(conversations);
        console.log('✅ Conversations displayed');
    } catch (error) {
        console.error('❌ Load conversations error:', error);
        console.error('Error details:', error.message, error.stack);
        showNotification('Không thể tải danh sách cuộc hội thoại', 'error');
    }
}

function displayConversations(conversations) {
    console.log('🎨 Displaying conversations:', conversations.length);
    const listElement = document.getElementById('conversationList');
    
    if (!listElement) {
        console.error('❌ conversationList element not found!');
        return;
    }

    if (conversations.length === 0) {
        console.log('⚠️ No conversations to display');
        listElement.innerHTML = '<p class="empty-state">Chưa có cuộc trò chuyện nào</p>';
        return;
    }

    console.log('📝 Rendering', conversations.length, 'conversations');
    listElement.innerHTML = conversations.map(conv => {
        console.log('Rendering conv:', conv.conversationId, conv.title);

        const title = escapeHtml(conv.title || 'Chat');
        const timeText = formatTime(conv.updatedAt || conv.createdAt);
        const previewText = escapeHtml(
            conv.lastMessagePreview ||
            (conv.type === 'group' ? '👥 Nhóm chat' : 'Trò chuyện trực tiếp')
        );
        
        // Determine if other user is online (for direct chats)
        let otherUserId = null;
        const currentUserId = localStorage.getItem('userId');
        if (conv.type === 'direct' && conv.members && conv.members.length === 2) {
            const otherMember = conv.members.find(m => m.id !== currentUserId && m.id !== currentUserId);
            otherUserId = otherMember?.id || conv.members.find(m => m.id !== currentUserId)?.id;
        }
        const isOnline = otherUserId && onlineUsers.has(otherUserId);

        return `
        <div class="conversation-item" data-id="${conv.conversationId}" data-other-user="${otherUserId || ''}" onclick="openConversation('${conv.conversationId}')">
            <div class="avatar-wrapper">
                <img src="assets/images/default-avatar.svg" alt="Avatar" class="avatar">
                ${conv.type === 'direct' ? `<span class="${isOnline ? 'online-indicator' : 'offline-indicator'}" data-user-status="${otherUserId || ''}"></span>` : ''}
            </div>
            <div class="conversation-info">
                <div class="conversation-header">
                    <span class="conversation-title">${title}</span>
                    <span class="conversation-time">${timeText}</span>
                </div>
                <div class="conversation-preview">
                    <span class="last-message">${previewText}</span>
                </div>
            </div>
        </div>
    `}).join('');
    console.log('✅ HTML rendered, innerHTML length:', listElement.innerHTML.length);
}

// ===== ONLINE STATUS FUNCTIONS =====
async function loadOnlineUsers() {
    try {
        const users = await window.socketHandler.getOnlineUsers();
        // socket-real.js returns the users array directly
        if (users && Array.isArray(users)) {
            onlineUsers.clear();
            users.forEach(user => {
                if (user.userId && user.isOnline) {
                    onlineUsers.add(user.userId);
                    // Clear lastSeenAt for online users
                    userLastSeen.delete(user.userId);
                }
            });
            console.log('📶 Loaded online users:', Array.from(onlineUsers));
            console.log('💾 userLastSeen after load:', Array.from(userLastSeen.keys()));
            updateOnlineIndicators();
        }
    } catch (error) {
        console.error('❌ Load online users error:', error);
    }
}

// Populate lastSeenAt from conversations when they load
function populateLastSeenFromConversations(conversations) {
    const currentUserId = localStorage.getItem('userId');
    
    conversations.forEach(conv => {
        if (conv.type === 'direct' && conv.members) {
            conv.members.forEach(member => {
                // Skip current user
                if (member.id === currentUserId) return;
                
                // If user is not online and has lastSeenAt, save it
                if (!onlineUsers.has(member.id) && member.lastSeenAt) {
                    userLastSeen.set(member.id, member.lastSeenAt);
                    console.log('💾 Saved lastSeenAt for', member.displayName || member.id, ':', member.lastSeenAt);
                }
            });
        }
    });
}

function updateOnlineIndicators() {
    // Update all online indicators in conversation list
    document.querySelectorAll('[data-user-status]').forEach(indicator => {
        const userId = indicator.getAttribute('data-user-status');
        if (userId) {
            const isOnline = onlineUsers.has(userId);
            indicator.className = isOnline ? 'online-indicator' : 'offline-indicator';
        }
    });
    
    // Also update chat header if direct chat is open
    if (currentConversationId) {
        const currentConv = currentConversations.find(c => c.conversationId === currentConversationId);
        if (currentConv && currentConv.type === 'direct') {
            const currentUserId = localStorage.getItem('userId');
            const otherUserId = currentConv.members?.find(m => m.id !== currentUserId)?.id;
            if (otherUserId) {
                const chatMembersEl = document.getElementById('chatMembers');
                if (chatMembersEl) {
                    const isOnline = onlineUsers.has(otherUserId);
                    if (isOnline) {
                        chatMembersEl.textContent = '🟢 Online';
                    } else {
                        // Show last seen time if available
                        const lastSeen = userLastSeen.get(otherUserId);
                        if (lastSeen) {
                            const timeAgo = getTimeAgo(lastSeen);
                            chatMembersEl.textContent = `⚫ Hoạt động ${timeAgo}`;
                        } else {
                            chatMembersEl.textContent = '⚫ Offline';
                        }
                    }
                }
            }
        }
    }
}

// Helper function to get "time ago" text
function getTimeAgo(timestamp) {
    const now = new Date();
    const then = new Date(timestamp);
    const diffMs = now - then;
    const diffMinutes = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMinutes / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMinutes < 1) return 'vừa xong';
    if (diffMinutes < 60) return `${diffMinutes} phút trước`;
    if (diffHours < 24) return `${diffHours} giờ trước`;
    if (diffDays < 7) return `${diffDays} ngày trước`;
    return formatTime(timestamp); // Fallback to absolute time
}

async function openConversation(conversationId) {
    currentConversationId = conversationId;
    window.currentConversationId = conversationId;
    
    const conv = currentConversations.find(c => c.conversationId === conversationId);
    if (!conv) return;

    // Update active state
    document.querySelectorAll('.conversation-item').forEach(item => {
        item.classList.remove('active');
    });
    const activeItem = document.querySelector(`.conversation-item[data-id="${conversationId}"]`);
    if (activeItem) {
        activeItem.classList.add('active');
    }

    // Show chat area
    document.getElementById('emptyChat').style.display = 'none';
    document.getElementById('activeChat').style.display = 'flex';

    // Update chat header
    const chatTitle = document.getElementById('chatTitle');
    const chatMembers = document.getElementById('chatMembers');
    
    if (chatTitle) {
        chatTitle.textContent = conv.title || 'Chat';
    }
    
    if (chatMembers) {
        if (conv.type === 'group') {
            const memberCount = conv.members?.length || 0;
            chatMembers.textContent = `${memberCount} thành viên`;
        } else if (conv.type === 'direct') {
            // For direct chat, show online status
            const currentUserId = localStorage.getItem('userId');
            const otherMember = conv.members?.find(m => m.id !== currentUserId);
            
            if (otherMember) {
                const isOnline = onlineUsers.has(otherMember.id);
                if (isOnline) {
                    chatMembers.textContent = '🟢 Online';
                } else {
                    // Show last seen if available
                    const lastSeen = userLastSeen.get(otherMember.id);
                    if (lastSeen) {
                        const timeAgo = getTimeAgo(lastSeen);
                        chatMembers.textContent = `⚫ Hoạt động ${timeAgo}`;
                    } else {
                        chatMembers.textContent = '⚫ Offline';
                    }
                }
            } else {
                chatMembers.textContent = 'Trực tiếp';
            }
        }
    }

    // If right panel is open, keep it in sync
    if (isRightPanelOpen()) {
        refreshGroupInfoPanel();
    }

    // Load messages
    try {
        const messages = await window.socketHandler.getMessages(conversationId);
        console.log('📨 Loaded messages:', messages);
        renderMessages(messages);
    } catch (error) {
        console.error('❌ Load messages error:', error);
        showNotification('Không thể tải tin nhắn', 'error');
    }
}

function renderMessages(messages) {
    const messagesArea = document.getElementById('messagesArea');
    if (!messagesArea) return;

    // Clear then re-render using ui.js so attachments (image/video/file) show after reload
    messagesArea.innerHTML = '';

    const list = Array.isArray(messages) ? messages.slice().reverse() : [];
    if (typeof window.displayMessage === 'function') {
        for (const msg of list) {
            window.displayMessage(msg);
        }
    } else {
        // Fallback: if ui.js not loaded for some reason
        const currentUserId = String(localStorage.getItem('userId') || '').trim();
        messagesArea.innerHTML = list.map(msg => {
            const senderIdRaw = msg?.senderId ?? msg?.SenderId ?? msg?.senderUserId ?? msg?.SenderUserId ?? msg?.fromUserId ?? msg?.FromUserId ?? msg?.userId ?? msg?.UserId ?? msg?.sender?.id ?? msg?.sender?.Id ?? '';
            const senderId = String(senderIdRaw || '').trim();
            const isOwn = !!senderId && !!currentUserId && senderId === currentUserId;
            const time = formatTime(msg.createdAt);
            const senderName = msg.senderDisplayName || msg.SenderDisplayName || msg.senderName || msg.SenderName || msg.sender?.displayName || msg.sender?.DisplayName || senderId;
            return `
                <div class="message ${isOwn ? 'own' : 'other'}">
                    ${!isOwn ? '<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">' : ''}
                    <div class="message-content">
                        ${!isOwn ? `<div class="message-sender">${escapeHtml(senderName)}</div>` : ''}
                        ${msg.content ? `<div class="message-text">${escapeHtml(msg.content)}</div>` : ''}
                        <div class="message-time">${time} • Seq: ${msg.seq}</div>
                    </div>
                </div>
            `;
        }).join('');
        messagesArea.scrollTop = messagesArea.scrollHeight;
    }
}

// ===== GROUP INFO PANEL (Members) =====
function normalizeMember(m) {
    if (!m) return null;
    const id = m.id || m.userId || m.UserId || '';
    if (!id) return null;
    const roleRaw = m.role || m.Role || 'member';
    return {
        id: String(id),
        displayName: m.displayName || m.DisplayName || m.name || m.Name || String(id),
        role: String(roleRaw || 'member').toLowerCase(),
        joinedAt: m.joinedAt || m.JoinedAt || null,
        isOnline: !!(m.isOnline ?? m.IsOnline),
        lastSeenAt: m.lastSeenAt || m.LastSeenAt || null,
        avatarUrl: m.avatarUrl || m.AvatarUrl || null
    };
}

function getRoleLabel(role) {
    const r = String(role || 'member').toLowerCase();
    if (r === 'owner') return 'Quản trị viên • người lập nhóm';
    if (r === 'admin') return 'Quản trị viên';
    return 'Thành viên';
}

function getRoleRank(role) {
    const r = String(role || 'member').toLowerCase();
    if (r === 'owner') return 0;
    if (r === 'admin') return 1;
    return 2;
}

function isRightPanelOpen() {
    const panel = document.getElementById('rightPanel');
    if (!panel) return false;
    return panel.style.display !== 'none' && panel.style.display !== '';
}

async function refreshGroupInfoPanel() {
    try {
        const panel = document.getElementById('rightPanel');
        if (!panel) return;

        const conversationId = currentConversationId;
        const conv = currentConversations.find(c => c.conversationId === conversationId);

        // Nếu không chọn conversation hoặc không phải group thì ẩn panel
        if (!conversationId || !conv || conv.type !== 'group') {
            panel.style.display = 'none';
            return;
        }

        // Header info
        const groupNameEl = document.getElementById('groupName');
        if (groupNameEl) groupNameEl.textContent = conv.title || 'Nhóm';

        const memberCountEl = document.getElementById('memberCount');
        const membersListEl = document.getElementById('membersList');
        if (!memberCountEl || !membersListEl) return;

        membersListEl.innerHTML = '<div class="member-search-empty">Đang tải...</div>';

        let members = [];
        let membersVersion = conv.membersVersion;
        if (window.socketHandler?.getMembers) {
            const res = await window.socketHandler.getMembers(conversationId);
            members = res?.members || [];
            membersVersion = res?.membersVersion ?? membersVersion;
        } else {
            members = conv.members || [];
        }

        // Update local cache (normalize to avoid role/permission bugs)
        conv.members = (Array.isArray(members) ? members : [])
            .map(normalizeMember)
            .filter(Boolean);
        if (membersVersion != null) conv.membersVersion = membersVersion;

        memberCountEl.textContent = String(conv.members.length);

        // Permission
        const currentUserId = localStorage.getItem('userId');
        const me = conv.members.find(m => String(m.id) === String(currentUserId));
        const myRole = String(me?.role || 'member').toLowerCase();

        const inviteMode = String(conv.inviteMode || conv.InviteMode || 'public').trim().toLowerCase();
        const canAddMember = !!me && (inviteMode === 'public' || myRole === 'owner');

        // Add member button enable/disable
        const btnAddMember = document.getElementById('btnAddMember');
        if (btnAddMember) {
            btnAddMember.disabled = !canAddMember;
            btnAddMember.title = btnAddMember.disabled
                ? (inviteMode === 'private'
                    ? 'Chỉ người lập nhóm mới có quyền thêm thành viên (nhóm riêng tư)'
                    : 'Bạn không có quyền thêm thành viên')
                : '';
        }

        const sortedMembers = conv.members
            .slice()
            .sort((a, b) => {
                const ra = getRoleRank(a.role);
                const rb = getRoleRank(b.role);
                if (ra !== rb) return ra - rb;
                return String(a.displayName || a.id).localeCompare(String(b.displayName || b.id), 'vi');
            });

        membersListEl.innerHTML = sortedMembers.map(m => {
            const role = String(m.role || 'member').toLowerCase();
            const isOnline = !!m.isOnline;
            const statusText = isOnline ? '🟢 Online' : '⚫ Offline';
            const roleLabel = getRoleLabel(role);
            const canKick = canKickMember(myRole, role, currentUserId, m.id);
            const safeName = escapeHtml(m.displayName || m.id);
            const safeRoleLabel = escapeHtml(roleLabel);

            return `
                <div class="member-item">
                    <img src="assets/images/default-avatar.svg" alt="Avatar" class="avatar">
                    <div class="member-info">
                        <div class="member-name">${safeName}</div>
                        <div class="member-role">${statusText} • ${safeRoleLabel}</div>
                    </div>
                    <div class="member-actions">
                        ${canKick ? `<button class="btn-secondary" onclick="removeMemberFromGroup('${m.id}')">Xóa</button>` : ''}
                    </div>
                </div>
            `;
        }).join('');
    } catch (error) {
        console.error('❌ refreshGroupInfoPanel error:', error);
        const membersListEl = document.getElementById('membersList');
        if (membersListEl) {
            membersListEl.innerHTML = '<div class="member-search-empty">Không thể tải danh sách thành viên</div>';
        }
    }
}

function canKickMember(myRole, targetRole, myUserId, targetUserId) {
    if (!myUserId || !targetUserId) return false;
    if (myUserId === targetUserId) return false;
    if (targetRole === 'owner') return false;
    return String(myRole || '').toLowerCase() === 'owner';
}

async function removeMemberFromGroup(targetUserId) {
    if (!currentConversationId || !targetUserId) return;
    const ok = confirm('Bạn có chắc muốn xóa thành viên này khỏi nhóm?');
    if (!ok) return;
    try {
        await window.socketHandler.removeMember(currentConversationId, targetUserId);
        // Realtime event sẽ update UI; refresh nhẹ để chắc chắn
        if (isRightPanelOpen()) {
            refreshGroupInfoPanel();
        }
    } catch (error) {
        console.error('❌ removeMemberFromGroup error:', error);
        showNotification(error?.message || 'Không thể xóa thành viên', 'error');
    }
}

// expose for inline onclick
window.removeMemberFromGroup = removeMemberFromGroup;

// ===== ADD MEMBER MODAL =====
let addMemberSelectedUser = null;
const addMemberCandidatesById = new Map();

function setupAddMemberModal() {
    const modal = document.getElementById('addMemberModal');
    if (!modal) return;

    const closeBtn = document.getElementById('closeAddMemberModal');
    const cancelBtn = document.getElementById('cancelAddMemberModal');
    const confirmBtn = document.getElementById('confirmAddMember');
    const input = document.getElementById('addMemberSearchInput');
    const results = document.getElementById('addMemberSearchResults');

    if (closeBtn) closeBtn.onclick = closeAddMemberModal;
    if (cancelBtn) cancelBtn.onclick = closeAddMemberModal;
    if (confirmBtn) confirmBtn.onclick = confirmAddMember;

    if (input && results) {
        const handleSearch = debounce(async () => {
            const q = input.value.trim();
            addMemberSelectedUser = null;
            addMemberCandidatesById.clear();
            if (confirmBtn) confirmBtn.disabled = true;

            if (q.length < 2) {
                results.innerHTML = '<div class="member-search-empty">Nhập tên hoặc email để tìm kiếm</div>';
                return;
            }

            results.innerHTML = '<div class="member-search-empty">Đang tìm kiếm...</div>';
            const users = await searchUsers(q);
            displayAddMemberSearchResults(users);
        }, 300);

        input.addEventListener('input', handleSearch);
    }
}

function openAddMemberModal() {
    const modal = document.getElementById('addMemberModal');
    if (!modal) return;

    const conv = currentConversations.find(c => c.conversationId === currentConversationId);
    if (!conv || conv.type !== 'group') {
        showNotification('Chỉ có thể thêm thành viên trong nhóm', 'warning');
        return;
    }

    // Check permission locally (server vẫn kiểm tra)
    const currentUserId = localStorage.getItem('userId');
    const me = conv.members?.map(normalizeMember).filter(Boolean).find(m => String(m.id) === String(currentUserId));
    const myRole = String(me?.role || 'member').toLowerCase();
    const inviteMode = String(conv.inviteMode || conv.InviteMode || 'public').trim().toLowerCase();
    const canAddMember = !!me && (inviteMode === 'public' || myRole === 'owner');
    if (!canAddMember) {
        showNotification(inviteMode === 'private'
            ? 'Nhóm riêng tư: chỉ người lập nhóm mới có thể thêm thành viên'
            : 'Bạn không có quyền thêm thành viên', 'error');
        return;
    }

    addMemberSelectedUser = null;
    addMemberCandidatesById.clear();
    const input = document.getElementById('addMemberSearchInput');
    const results = document.getElementById('addMemberSearchResults');
    const confirmBtn = document.getElementById('confirmAddMember');
    if (input) input.value = '';
    if (results) results.innerHTML = '<div class="member-search-empty">Nhập tên hoặc email để tìm kiếm</div>';
    if (confirmBtn) confirmBtn.disabled = true;

    modal.classList.add('show');
    modal.style.display = 'flex';
}

function closeAddMemberModal() {
    const modal = document.getElementById('addMemberModal');
    if (!modal) return;
    modal.classList.remove('show');
    modal.style.display = 'none';
    addMemberSelectedUser = null;
}

function displayAddMemberSearchResults(users) {
    const results = document.getElementById('addMemberSearchResults');
    if (!results) return;

    const currentUserId = localStorage.getItem('userId');
    const conv = currentConversations.find(c => c.conversationId === currentConversationId);
    const existingIds = new Set((conv?.members || []).map(m => m.id));

    const filtered = (users || [])
        .filter(u => u && u.id && u.id !== currentUserId)
        .filter(u => !existingIds.has(u.id));

    if (filtered.length === 0) {
        results.innerHTML = '<div class="member-search-empty">Không tìm thấy người dùng có thể thêm</div>';
        return;
    }

    addMemberCandidatesById.clear();
    filtered.forEach(u => addMemberCandidatesById.set(String(u.id), String(u.displayName || u.id)));

    results.innerHTML = filtered.map(u => {
        const safeName = escapeHtml(u.displayName || u.id);
        return `
            <div class="member-search-item" onclick="selectAddMemberCandidate('${u.id}')">
                <input type="radio" name="addMemberCandidate" onclick="event.stopPropagation(); selectAddMemberCandidate('${u.id}')">
                <img src="${u.avatarUrl || 'assets/images/default-avatar.svg'}" alt="Avatar" class="avatar">
                <div class="member-info">
                    <span class="member-name">${safeName}</span>
                    <span class="member-status">${u.isOnline ? '🟢 Online' : '⚫ Offline'}</span>
                </div>
            </div>
        `;
    }).join('');
}

function selectAddMemberCandidate(userId) {
    const displayName = addMemberCandidatesById.get(String(userId)) || String(userId);
    addMemberSelectedUser = { id: String(userId), displayName };
    const confirmBtn = document.getElementById('confirmAddMember');
    if (confirmBtn) confirmBtn.disabled = false;

    // update radio state
    const results = document.getElementById('addMemberSearchResults');
    if (results) {
        const radios = results.querySelectorAll('input[type="radio"]');
        radios.forEach(r => {
            const item = r.closest('.member-search-item');
            const onclick = item?.getAttribute('onclick') || '';
            r.checked = onclick.includes(`'${userId}'`);
        });
    }
}

// expose for inline onclick
window.selectAddMemberCandidate = selectAddMemberCandidate;

async function confirmAddMember() {
    if (!currentConversationId || !addMemberSelectedUser?.id) return;
    try {
        await window.socketHandler.addMember(currentConversationId, addMemberSelectedUser.id);
        closeAddMemberModal();
        // Realtime event sẽ update UI; refresh nhẹ để chắc chắn
        if (isRightPanelOpen()) {
            refreshGroupInfoPanel();
        }
    } catch (error) {
        console.error('❌ confirmAddMember error:', error);
        showNotification(error?.message || 'Không thể thêm thành viên', 'error');
    }
}

// ===== SEND MESSAGE (Người 2) =====
// Global variables for file uploads
let selectedFiles = [];
// uploadService is defined later as const
const pendingUploads = new Map(); // clientMessageId -> { file, conversationId, content, messageType, token, lastProgress }

async function resumePendingUploads() {
    if (pendingUploads.size === 0) return;

    const entries = Array.from(pendingUploads.entries());
    for (const [clientMessageId, job] of entries) {
        try {
            if (!uploadService) uploadService = new UploadService();

            if (typeof window.updateUploadProgressUI === 'function') {
                window.updateUploadProgressUI(clientMessageId, job.lastProgress ?? 0, 'Đang gửi');
            }

            const result = await uploadService.uploadResumable(
                job.file,
                job.token,
                (progress) => {
                    job.lastProgress = progress;
                    if (typeof window.updateUploadProgressUI === 'function') {
                        window.updateUploadProgressUI(clientMessageId, progress, 'Đang gửi');
                    }
                },
                (state) => {
                    if (state?.status === 'paused' && typeof window.updateUploadProgressUI === 'function') {
                        window.updateUploadProgressUI(clientMessageId, job.lastProgress ?? 0, 'Tạm dừng');
                    }
                }
            );

            await window.socketHandler.sendMessage(
                job.conversationId,
                job.content || '',
                job.messageType,
                clientMessageId,
                result.url,
                job.file.name,
                job.file.type,
                job.file.size
            );

            // Wait for server broadcast to reconcile UI
            pendingUploads.delete(clientMessageId);
        } catch {
            // Keep pending. Will retry when online again.
            if (typeof window.updateUploadProgressUI === 'function') {
                window.updateUploadProgressUI(clientMessageId, null, 'Tạm dừng');
            }
        }
    }
}

// Xử lý file selection
function handleFileSelection(event) {
    const files = Array.from(event.target.files);
    if (files.length === 0) return;

    // Khởi tạo UploadService an toàn (vì handleFileSelection chạy trước sendMessage)
    if (!uploadService) {
        if (typeof UploadService !== 'function') {
            showNotification('Không tải được chức năng upload (UploadService).', 'error');
            return;
        }
        uploadService = new UploadService();
    }

    // Validate và thêm vào danh sách
    files.forEach(file => {
        try {
            uploadService.validateFile(file);
            selectedFiles.push(file);
        } catch (error) {
            showNotification(error.message, 'error');
        }
    });

    // Hiển thị preview
    displayFilePreview();

    // Đẩy tên file vào ô nhập tin nhắn (CHỈ TÊN FILE)
    const input = document.getElementById('messageInput');
    if (input) {
        const names = files.map(f => f.name).join(', ');
        input.value = names;
        input.focus();
    }

    event.target.value = ''; // Reset input
}

// Hiển thị file preview
async function displayFilePreview() {
    const previewArea = document.getElementById('filePreviewArea');
    const previewList = document.getElementById('filePreviewList');
    
    if (selectedFiles.length === 0) {
        previewArea.style.display = 'none';
        return;
    }

    previewArea.style.display = 'block';
    previewList.innerHTML = '';

    for (let i = 0; i < selectedFiles.length; i++) {
        const file = selectedFiles[i];
        const previewUrl = await uploadService.createFilePreview(file);
        
        const itemEl = document.createElement('div');
        itemEl.className = 'file-preview-item';
        itemEl.innerHTML = `
            <div class="file-preview-icon">
                ${previewUrl ? 
                    `<img src="${previewUrl}" alt="${file.name}">` : 
                    uploadService.getFileIcon(file.type)
                }
            </div>
            <div class="file-preview-info">
                <div class="file-preview-name">${escapeHtml(file.name)}</div>
                <div class="file-preview-size">${uploadService.formatFileSize(file.size)}</div>
            </div>
            <button class="btn-remove-file" onclick="removeFile(${i})">✕</button>
        `;
        
        previewList.appendChild(itemEl);
    }
}

// Xóa file khỏi selection
function removeFile(index) {
    selectedFiles.splice(index, 1);
    displayFilePreview();
}

// Clear toàn bộ file selection
function clearFileSelection() {
    selectedFiles = [];
    displayFilePreview();
}

async function sendMessage() {
    const input = document.getElementById('messageInput');
    if (!input) return;

    const content = input.value.trim();
    
    // Phải có content hoặc file
    if (!content && selectedFiles.length === 0) return;
    if (!currentConversationId) return;

    // Khởi tạo UploadService an toàn
    if (!uploadService) {
        if (typeof UploadService !== 'function') {
            showNotification('Không tải được chức năng upload (UploadService).', 'error');
            return;
        }
        uploadService = new UploadService();
    }

    try {
        const token = localStorage.getItem('token');

        // Gửi tin nhắn
        const clientMessageId = generateUUID();
        const userId = localStorage.getItem('userId');
        const senderDisplayName = localStorage.getItem('userName') || 'User';

        // Nếu có file: tạo tin nhắn optimistic + upload resumable + progress
        if (selectedFiles.length > 0) {
            for (const file of selectedFiles) {
                const fileClientId = generateUUID();

                let messageType = 'file';
                if (file.type.startsWith('image/')) messageType = 'image';
                else if (file.type.startsWith('video/')) messageType = 'video';

                const optimisticPayload = {
                    messageId: `pending:${fileClientId}`,
                    clientMessageId: fileClientId,
                    conversationId: currentConversationId,
                    senderId: userId,
                    senderDisplayName,
                    messageType,
                    content: content || file.name,
                    fileUrl: null,
                    fileName: file.name,
                    fileType: file.type,
                    fileSize: file.size,
                    uploadProgress: 0,
                    uploadStatus: 'uploading',
                    uploadStatusText: 'Đang gửi',
                    seq: null,
                    createdAt: new Date().toISOString()
                };

                if (typeof window.displayMessage === 'function') {
                    window.displayMessage(optimisticPayload);
                }

                pendingUploads.set(fileClientId, {
                    file,
                    conversationId: currentConversationId,
                    content: content || '',
                    messageType,
                    token,
                    lastProgress: 0
                });

                try {
                    const result = await uploadService.uploadResumable(
                        file,
                        token,
                        (progress) => {
                            const job = pendingUploads.get(fileClientId);
                            if (job) job.lastProgress = progress;
                            if (typeof window.updateUploadProgressUI === 'function') {
                                window.updateUploadProgressUI(fileClientId, progress, 'Đang gửi');
                            }
                        },
                        (state) => {
                            if (state?.status === 'paused' && typeof window.updateUploadProgressUI === 'function') {
                                const job = pendingUploads.get(fileClientId);
                                window.updateUploadProgressUI(fileClientId, job?.lastProgress ?? 0, 'Tạm dừng');
                            }
                        }
                    );

                    await window.socketHandler.sendMessage(
                        currentConversationId,
                        content || '',
                        messageType,
                        fileClientId,
                        result.url,
                        file.name,
                        file.type,
                        file.size
                    );

                    pendingUploads.delete(fileClientId);
                } catch (error) {
                    console.error('Upload error:', error);
                    const job = pendingUploads.get(fileClientId);
                    if (typeof window.updateUploadProgressUI === 'function') {
                        window.updateUploadProgressUI(fileClientId, job?.lastProgress ?? 0, 'Tạm dừng');
                    }
                    showNotification(`Upload tạm dừng: ${file.name}. Khi có mạng sẽ tự gửi tiếp.`, 'error');
                }
            }
        } else {
            // Tin nhắn text thông thường
            const optimisticPayload = {
                messageId: `pending:${clientMessageId}`,
                clientMessageId,
                conversationId: currentConversationId,
                senderId: userId,
                senderDisplayName,
                messageType: 'text',
                content,
                seq: null,
                createdAt: new Date().toISOString()
            };
            
            if (typeof window.displayMessage === 'function') {
                window.displayMessage(optimisticPayload);
            }
            
            await window.socketHandler.sendMessage(currentConversationId, content, 'text', clientMessageId);
        }

        // Clear input
        input.value = '';
        clearFileSelection();
        
        console.log('✅ Message sent');
    } catch (error) {
        console.error('❌ Send message error:', error);
        showNotification('Không thể gửi tin nhắn', 'error');
    }
}


// ===== CREATE GROUP MODAL (Người 2) =====
function openCreateGroupModal() {
    const modal = document.getElementById('createGroupModal');
    if (!modal) return;

    modal.classList.add('show');
    modal.style.display = 'flex';

    // Reset form
    document.getElementById('groupNameInput').value = '';
    selectedMembers = [];
    updateSelectedMembersUI();

    // Setup member search
    setupMemberSearch();

    // Handlers (avoid stacking listeners each time modal opens)
    const closeBtn = document.getElementById('closeGroupModal');
    const cancelBtn = document.getElementById('cancelGroupModal');
    const confirmBtn = document.getElementById('confirmCreateGroup');
    if (closeBtn) closeBtn.onclick = closeCreateGroupModal;
    if (cancelBtn) cancelBtn.onclick = closeCreateGroupModal;
    if (confirmBtn) confirmBtn.onclick = createGroup;
}

function closeCreateGroupModal() {
    const modal = document.getElementById('createGroupModal');
    if (modal) {
        modal.classList.remove('show');
        modal.style.display = 'none';
    }
    document.getElementById('groupNameInput').value = '';
}

function createGroup() {
    const groupName = document.getElementById('groupNameInput').value.trim();
    if (!groupName) {
        showNotification('Vui lòng nhập tên nhóm', 'error');
        return;
    }

    if (selectedMembers.length === 0) {
        showNotification('Vui lòng chọn ít nhất 1 thành viên', 'error');
        return;
    }

    console.log('👥 Creating group:', groupName, 'with members:', selectedMembers);

    // Send create_group via WebSocket
    const currentUserId = localStorage.getItem('userId');
    if (!currentUserId) {
        showNotification('Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn', 'error');
        return;
    }

    // Sanitize memberIds: remove null/empty + unique
    const rawMemberIds = [currentUserId, ...selectedMembers.map(m => m?.id)];
    const memberIds = Array.from(new Set(rawMemberIds.filter(id => typeof id === 'string' && id.trim().length > 0)));
    if (memberIds.length < 2) {
        showNotification('Danh sách thành viên không hợp lệ', 'error');
        return;
    }
    
    // Use createGroup instead of createConversation
    window.socketHandler.createGroup(groupName, memberIds)
        .then((result) => {
            console.log('✅ Group created:', result);
            showNotification('Đã tạo nhóm thành công', 'success');
            closeCreateGroupModal();
            loadConversations();
        })
        .catch(error => {
            console.error('❌ Create group error:', error);
            showNotification('Không thể tạo nhóm', 'error');
        });
}

// ===== UTILITY FUNCTIONS =====
function generateUUID() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        const r = Math.random() * 16 | 0;
        const v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

// ===== HELPER FUNCTIONS =====
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatTime(isoString) {
    const date = new Date(isoString);
    return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

function updateConversationLastMessage(conversationId, content) {
    const conv = currentConversations.find(c => c.conversationId === conversationId);
    if (conv) {
        conv.lastMessagePreview = content;
        conv.updatedAt = new Date().toISOString();
        displayConversations(currentConversations);
    }
}

// Note: showNotification is defined in ui.js

// ===== USER SEARCH FUNCTIONS =====
let searchTimeout = null;
let selectedMembers = [];

// Debounce function
function debounce(func, delay) {
    return function(...args) {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(() => func.apply(this, args), delay);
    };
}

// Search users via API
async function searchUsers(query) {
    if (!query || query.trim().length < 2) {
        return [];
    }

    try {
        const url = `http://${window.SERVER_HOST}:5000/api/users/search?q=${encodeURIComponent(query)}&limit=10`;
        console.log('🌐 Calling API:', url);
        
        const response = await fetch(url);
        console.log('📡 API response status:', response.status);
        
        if (!response.ok) {
            throw new Error('Search failed');
        }
        const users = await response.json();
        console.log('👥 Users found:', users.length);
        return users || [];
    } catch (error) {
        console.error('❌ Search error:', error);
        return [];
    }
}


// Upload Service Instance
const uploadService = new UploadService();

function setupFileUpload() {
    const fileInput = document.getElementById('fileInput');
    const btnAttachment = document.getElementById('btnAttachment');

    if (!fileInput) return;

    fileInput.addEventListener('change', async (e) => {
        const files = Array.from(e.target.files);
        if (files.length === 0) return;

        // Reset input value to allow selecting same file again
        e.target.value = '';

        for (const file of files) {
            await handleFileUpload(file);
        }
    });
}

async function handleFileUpload(file) {
    if (!currentConversationId) {
        showNotification('Vui lòng chọn cuộc hội thoại trước khi gửi file', 'warning');
        return;
    }

    // Create a temporary message ID
    const tempId = `temp-${Date.now()}`;
    
    // Optimistic UI: Show uploading message
    // TODO: Add UI for uploading state if needed
    showNotification(`Đang tải lên: ${file.name}...`, 'info');

    try {
        // Validate file
        uploadService.validateFile(file);

        // Get token
        const token = localStorage.getItem('token');
        if (!token) throw new Error('No auth token');

        // Upload file
        const result = await uploadService.upload(file, token, (progress) => {
            console.log(`Upload progress for ${file.name}: ${progress}%`);
        });

        console.log('✅ Upload completed:', result);

        // Send message with file info
        // sendMessage(conversationId, content, messageType, clientMessageId, fileUrl, fileName, fileType, fileSize)
        await window.socketHandler.sendMessage(
            currentConversationId,
            result.fileName || file.name, // Content is filename
            getMsgTypeFromFile(file.type),
            null, // clientMessageId (auto generated)
            result.url,
            result.fileName || file.name,
            file.type,
            file.size
        );

    } catch (error) {
        console.error('❌ Upload failed:', error);
        showNotification(`Lỗi gửi file: ${error.message}`, 'error');
    }
}

// Helper to determine message type from MIME type
function getMsgTypeFromFile(mimeType) {
    console.log('📂 Detecting type for:', mimeType);
    if (!mimeType) return 'file';
    if (mimeType.startsWith('image/')) return 'image';
    if (mimeType.startsWith('video/')) return 'video';
    return 'file';
}

// Setup search box in sidebar
function setupSearchBox() {
    const searchInput = document.getElementById('searchInput');
    const searchResults = document.getElementById('searchResults');
    const btnSearch = document.getElementById('btnSearch');
    
    console.log('🔍 Setup search box:', { searchInput: !!searchInput, searchResults: !!searchResults, btnSearch: !!btnSearch });
    
    if (!searchInput || !searchResults) {
        console.error('❌ Search box elements not found!');
        return;
    }

    // Handle search function
    const performSearch = async () => {
        const query = searchInput.value.trim();
        console.log('🔍 Performing search for:', query);
        
        if (query.length < 2) {
            console.log('⚠️ Query too short, hiding results');
            searchResults.classList.remove('show');
            return;
        }

        // Show loading
        searchResults.innerHTML = '<div class="search-loading">Đang tìm kiếm...</div>';
        searchResults.classList.add('show');
        console.log('⏳ Showing loading state');

        // Search users
        const users = await searchUsers(query);
        console.log('✅ Search results:', users);
        displaySearchResults(users);
    };

    // Handle search input with debounce
    const handleSearch = debounce(performSearch, 300);

    searchInput.addEventListener('input', handleSearch);
    console.log('✅ Input event listener added');

    // Search button click
    if (btnSearch) {
        btnSearch.addEventListener('click', (e) => {
            console.log('🖱️ Search button clicked');
            e.preventDefault();
            e.stopPropagation();
            performSearch();
        });
        console.log('✅ Button click listener added');
    }

    // Enter key to search
    searchInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            console.log('⌨️ Enter key pressed');
            e.preventDefault();
            performSearch();
        }
    });
    console.log('✅ Enter key listener added');

    // Close search results when clicking outside
    document.addEventListener('click', (e) => {
        if (!searchInput.contains(e.target) && !searchResults.contains(e.target) && !btnSearch?.contains(e.target)) {
            searchResults.classList.remove('show');
        }
    });

    // Show results when focusing search input (if there's content)
    searchInput.addEventListener('focus', () => {
        if (searchInput.value.trim().length >= 2 && searchResults.innerHTML) {
            searchResults.classList.add('show');
        }
    });
}

// Display search results
function displaySearchResults(users) {
    const searchResults = document.getElementById('searchResults');
    if (!searchResults) return;

    const currentUserId = localStorage.getItem('userId');

    if (users.length === 0) {
        searchResults.innerHTML = '<div class="search-no-results">Không tìm thấy người dùng</div>';
        return;
    }

    // Filter out current user
    const filteredUsers = users.filter(u => u.id !== currentUserId);

    if (filteredUsers.length === 0) {
        searchResults.innerHTML = '<div class="search-no-results">Không tìm thấy người dùng khác</div>';
        return;
    }

    searchResults.innerHTML = filteredUsers.map(user => `
        <div class="search-result-item" onclick="createOrOpenDirectChat('${user.id}', '${escapeHtml(user.displayName)}')">
            <div class="avatar-wrapper">
                <img src="${user.avatarUrl || 'assets/images/default-avatar.svg'}" alt="Avatar" class="avatar">
                ${user.isOnline ? '<span class="online-indicator"></span>' : '<span class="offline-indicator"></span>'}
            </div>
            <div class="user-info">
                <span class="user-name">${escapeHtml(user.displayName)}</span>
                <span class="user-status">${user.isOnline ? '🟢 Online' : '⚫ Offline'}</span>
            </div>
        </div>
    `).join('');
}

// Create or open direct chat
async function createOrOpenDirectChat(otherUserId, otherUserName) {
    const searchInput = document.getElementById('searchInput');
    const searchResults = document.getElementById('searchResults');
    
    console.log('💬 Creating/opening direct chat with:', otherUserId, otherUserName);
    
    // Hide search results
    if (searchResults) searchResults.classList.remove('show');
    if (searchInput) searchInput.value = '';

    const currentUserId = localStorage.getItem('userId');

    // Check if conversation already exists
    const existingConv = currentConversations.find(conv => 
        conv.type === 'direct' && 
        conv.members && 
        conv.members.some(m => m.id === otherUserId)
    );

    if (existingConv) {
        // Open existing conversation
        console.log('✅ Found existing conversation:', existingConv.conversationId);
        openConversation(existingConv.conversationId);
        showNotification(`Đã mở chat với ${otherUserName}`, 'success');
        return;
    }

    // Create new direct conversation
    try {
        console.log('🆕 Creating new direct chat with:', otherUserId);
        
        // Use createDirect instead of createConversation
        const result = await window.socketHandler.createDirect(otherUserId);
        console.log('✅ Created conversation:', result);
        
        showNotification(`Đã tạo chat với ${otherUserName}`, 'success');
        
        // Reload conversations
        await loadConversations();
        
        // Open the new conversation
        const newConv = currentConversations.find(conv => 
            conv.type === 'direct' && 
            conv.members && 
            conv.members.some(m => m.id === otherUserId)
        );
        
        if (newConv) {
            console.log('✅ Opening new conversation:', newConv.conversationId);
            openConversation(newConv.conversationId);
        }
    } catch (error) {
        console.error('❌ Create conversation error:', error);
        showNotification('Không thể tạo cuộc trò chuyện', 'error');
    }
}

// Setup member search in create group modal
function setupMemberSearch() {
    const memberSearchInput = document.getElementById('memberSearchInput');
    const memberSearchResults = document.getElementById('memberSearchResults');
    
    if (!memberSearchInput || !memberSearchResults) return;

    // Handle search input with debounce
    const handleMemberSearch = debounce(async () => {
        const query = memberSearchInput.value.trim();
        
        if (query.length < 2) {
            memberSearchResults.innerHTML = '<div class="member-search-empty">Nhập tên hoặc email để tìm kiếm</div>';
            return;
        }

        // Show loading
        memberSearchResults.innerHTML = '<div class="member-search-empty">Đang tìm kiếm...</div>';

        // Search users
        const users = await searchUsers(query);
        displayMemberSearchResults(users);
    }, 300);

    memberSearchInput.addEventListener('input', handleMemberSearch);
}

// Display member search results in modal
function displayMemberSearchResults(users) {
    const memberSearchResults = document.getElementById('memberSearchResults');
    if (!memberSearchResults) return;

    const currentUserId = localStorage.getItem('userId');

    if (users.length === 0) {
        memberSearchResults.innerHTML = '<div class="member-search-empty">Không tìm thấy người dùng</div>';
        return;
    }

    // Filter out current user
    const filteredUsers = users.filter(u => u.id !== currentUserId);

    if (filteredUsers.length === 0) {
        memberSearchResults.innerHTML = '<div class="member-search-empty">Không tìm thấy người dùng khác</div>';
        return;
    }

    memberSearchResults.innerHTML = filteredUsers.map(user => {
        const isSelected = selectedMembers.some(m => m.id === user.id);
        return `
            <div class="member-search-item" onclick="toggleMemberSelection('${user.id}', '${escapeHtml(user.displayName)}', '${user.avatarUrl || 'assets/images/default-avatar.svg'}')">
                <input type="checkbox" ${isSelected ? 'checked' : ''} onclick="event.stopPropagation(); toggleMemberSelection('${user.id}', '${escapeHtml(user.displayName)}', '${user.avatarUrl || 'assets/images/default-avatar.svg'}')">
                <img src="${user.avatarUrl || 'assets/images/default-avatar.svg'}" alt="Avatar" class="avatar">
                <div class="member-info">
                    <span class="member-name">${escapeHtml(user.displayName)}</span>
                    <span class="member-status">${user.isOnline ? '🟢 Online' : '⚫ Offline'}</span>
                </div>
            </div>
        `;
    }).join('');
}

// Toggle member selection
function toggleMemberSelection(userId, displayName, avatarUrl) {
    const index = selectedMembers.findIndex(m => m.id === userId);
    
    if (index >= 0) {
        // Remove from selection
        selectedMembers.splice(index, 1);
    } else {
        // Add to selection
        selectedMembers.push({ id: userId, displayName, avatarUrl });
    }
    
    updateSelectedMembersUI();
    
    // Update checkbox state
    const memberSearchResults = document.getElementById('memberSearchResults');
    if (memberSearchResults) {
        const checkboxes = memberSearchResults.querySelectorAll('input[type="checkbox"]');
        checkboxes.forEach(cb => {
            const item = cb.closest('.member-search-item');
            if (item) {
                const itemUserId = item.getAttribute('onclick').match(/'([^']+)'/)[1];
                cb.checked = selectedMembers.some(m => m.id === itemUserId);
            }
        });
    }
}

// Update selected members UI
function updateSelectedMembersUI() {
    const container = document.getElementById('selectedMembersContainer');
    const list = document.getElementById('selectedMembersList');
    const count = document.getElementById('selectedCount');
    
    if (!container || !list || !count) return;

    if (selectedMembers.length === 0) {
        container.style.display = 'none';
        return;
    }

    container.style.display = 'block';
    count.textContent = selectedMembers.length;

    list.innerHTML = selectedMembers.map(member => `
        <div class="selected-member-tag">
            <span>${escapeHtml(member.displayName)}</span>
            <button class="remove-btn" onclick="toggleMemberSelection('${member.id}', '${escapeHtml(member.displayName)}', '${member.avatarUrl}')">✕</button>
        </div>
    `).join('');
}

console.log('Chat Application loaded');
