// ===== LOGIN/REGISTER HANDLING (Auth API) =====
const API_URL = 'http://localhost:5000/api/auth';

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


// ===== CHAT PAGE INITIALIZATION =====
let currentConversationId = null;
let currentConversations = [];

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
        await loadConversations();

        // Setup WebSocket event handlers
        setupWebSocketHandlers();
        
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
            panel.style.display = panel.style.display === 'none' ? 'flex' : 'none';
        }
    });

    // Close panel
    document.getElementById('btnClosePanel')?.addEventListener('click', () => {
        document.getElementById('rightPanel').style.display = 'none';
    });

    // Tab switching
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            // TODO: Filter conversations by tab
        });
    });
}

// ===== WEBSOCKET EVENT HANDLERS (Người 2) =====
function setupWebSocketHandlers() {
    // Handle incoming messages
    window.onMessageCreated = (payload) => {
        console.log('📨 Message received:', payload);

        // If this is our own optimistic message, reconcile the pending UI element
        const currentUserId = localStorage.getItem('userId');
        if (payload && payload.clientMessageId && payload.senderId === currentUserId) {
            const pendingEl = document.querySelector(`.message[data-client-id="${payload.clientMessageId}"]`);
            if (pendingEl) {
                pendingEl.dataset.id = payload.messageId;
                pendingEl.classList.remove('pending');

                const timeEl = pendingEl.querySelector('.message-time');
                if (timeEl) {
                    const timeText = payload.createdAt ? formatTime(payload.createdAt) : formatTime(new Date().toISOString());
                    timeEl.textContent = payload.seq ? `${timeText} • Seq: ${payload.seq}` : timeText;
                }
            }
        }
        
        // Nếu đang mở conversation này, hiển thị message
        if (payload.conversationId === currentConversationId) {
            // ui.js expects a single argument
            if (typeof window.displayMessage === 'function') {
                // Avoid duplicate if we already rendered optimistic version
                if (payload.clientMessageId) {
                    const alreadyRendered = document.querySelector(`.message[data-client-id="${payload.clientMessageId}"]`);
                    if (alreadyRendered) {
                        return;
                    }
                }
                window.displayMessage(payload);
            }
        }
        
        // Update conversation list
        updateConversationLastMessage(payload.conversationId, payload.content);
        
        // Show notification nếu không phải conversation hiện tại
        if (payload.conversationId !== currentConversationId) {
            const fromName = payload.senderDisplayName || payload.senderId;
            showNotification(`Tin nhắn mới từ ${fromName}`, 'info');
        }
    };

    // Handle new conversation
    window.onConversationCreated = (payload) => {
        console.log('🆕 Conversation created:', payload);
        loadConversations(); // Reload conversation list
        showNotification('Tạo nhóm thành công', 'success');
    };

    // Handle member added
    window.onMemberAdded = (payload) => {
        console.log('➕ Member added:', payload);
        if (payload.conversationId === currentConversationId) {
            // TODO: Update member list in right panel
        }
    };

    // Handle member removed
    window.onMemberRemoved = (payload) => {
        console.log('➖ Member removed:', payload);
        if (payload.conversationId === currentConversationId) {
            // TODO: Update member list in right panel
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

        return `
        <div class="conversation-item" data-id="${conv.conversationId}" onclick="openConversation('${conv.conversationId}')">
            <img src="assets/images/default-avatar.svg" alt="Avatar" class="avatar">
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

async function openConversation(conversationId) {
    currentConversationId = conversationId;
    
    const conv = currentConversations.find(c => c.conversationId === conversationId);
    if (!conv) return;

    // Update active state
    document.querySelectorAll('.conversation-item').forEach(item => {
        item.classList.toggle('active', item.dataset.id === conversationId);
    });

    // Show chat area
    document.getElementById('emptyChat').style.display = 'none';
    document.getElementById('activeChat').style.display = 'flex';

    // Update chat header
    document.getElementById('chatTitle').textContent = conv.title || 'Chat';
    document.getElementById('chatMembers').textContent = 
        conv.type === 'group' ? 'Nhóm' : 'Trực tiếp';

    // Load messages từ server
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

    const currentUserId = localStorage.getItem('userId');

    messagesArea.innerHTML = messages.reverse().map(msg => {
        const isOwn = msg.senderId === currentUserId;
        const time = formatTime(msg.createdAt);
        const senderName = msg.senderDisplayName || msg.senderId;
        
        return `
            <div class="message ${isOwn ? 'own' : 'other'}">
                ${!isOwn ? '<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">' : ''}
                <div class="message-content">
                    ${!isOwn ? `<div class="message-sender">${escapeHtml(senderName)}</div>` : ''}
                    <div class="message-text">${escapeHtml(msg.content)}</div>
                    <div class="message-time">${time} • Seq: ${msg.seq}</div>
                </div>
                ${isOwn ? '<img src="assets/images/default-avatar.svg" class="avatar" alt="Avatar">' : ''}
            </div>
        `;
    }).join('');

    // Scroll to bottom
    messagesArea.scrollTop = messagesArea.scrollHeight;
}

// ===== SEND MESSAGE (Người 2) =====
async function sendMessage() {
    const input = document.getElementById('messageInput');
    if (!input) return;

    const content = input.value.trim();
    if (!content || !currentConversationId) return;

    // Clear input ngay để UX tốt hơn
    input.value = '';

    // Optimistic UI: show immediately
    const clientMessageId = generateUUID();
    const userId = localStorage.getItem('userId');
    const senderDisplayName = localStorage.getItem('userName') || 'User';
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
        const pendingEl = document.querySelector(`.message[data-client-id="${clientMessageId}"]`);
        if (pendingEl) pendingEl.classList.add('pending');
    }

    try {
        await window.socketHandler.sendMessage(currentConversationId, content, 'text', clientMessageId);
        console.log('✅ Message sent');
    } catch (error) {
        console.error('❌ Send message error:', error);
        showNotification('Không thể gửi tin nhắn', 'error');
        // Remove optimistic message if send fails
        const pendingEl = document.querySelector(`.message[data-client-id="${clientMessageId}"]`);
        if (pendingEl) pendingEl.remove();
        input.value = content; // Restore nếu fail
    }
}

// ===== CREATE GROUP MODAL (Người 2) =====
function openCreateGroupModal() {
    const modal = document.getElementById('createGroupModal');
    if (!modal) return;

    modal.classList.add('show');
    modal.style.display = 'flex';

    // Close handlers
    document.getElementById('closeGroupModal')?.addEventListener('click', closeCreateGroupModal);
    document.getElementById('cancelGroupModal')?.addEventListener('click', closeCreateGroupModal);
    
    // Create group handler
    document.getElementById('confirmCreateGroup')?.addEventListener('click', createGroup);
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
        alert('Vui lòng nhập tên nhóm');
        return;
    }

    // TODO: Người 2 - Send create_group via WebSocket
    // TODO: Người 4 - Search users để add members
    
    console.log('Creating group:', groupName);
    
    // Mock: Add to conversation list
    const newConv = {
        id: 'conv-' + Date.now(),
        type: 'group',
        title: groupName,
        avatarUrl: null,
        lastMessage: '',
        lastMessageTime: 'Vừa xong',
        unreadCount: 0
    };
    
    currentConversations.unshift(newConv);
    displayConversations(currentConversations); // Fixed: use displayConversations instead of renderConversationList
    closeCreateGroupModal();
    
    alert('Đã tạo nhóm "' + groupName + '"');
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
        conv.lastMessage = content;
        conv.updatedAt = new Date().toISOString();
        displayConversations(currentConversations);
    }
}

console.log('Chat Application loaded');
