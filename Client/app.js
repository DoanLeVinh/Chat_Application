// API Base URL
const API_URL = 'http://localhost:5000/api/auth';

// DOM Elements
const tabs = document.querySelectorAll('.tab');
const loginForm = document.getElementById('loginForm');
const registerForm = document.getElementById('registerForm');
const messageEl = document.getElementById('message');

// Tab switching
tabs.forEach(tab => {
    tab.addEventListener('click', () => {
        const tabName = tab.dataset.tab;
        
        // Update active tab
        tabs.forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
        
        // Update active form
        if (tabName === 'login') {
            loginForm.classList.add('active');
            registerForm.classList.remove('active');
        } else {
            registerForm.classList.add('active');
            loginForm.classList.remove('active');
        }
        
        // Clear message
        hideMessage();
    });
});

// Login form submit
loginForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const email = document.getElementById('loginEmail').value.trim();
    const password = document.getElementById('loginPassword').value;
    
    if (!email || !password) {
        showMessage('Vui lòng điền đầy đủ thông tin.', 'error');
        return;
    }
    
    await login(email, password);
});

// Register form submit
registerForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const displayName = document.getElementById('registerName').value.trim();
    const email = document.getElementById('registerEmail').value.trim();
    const password = document.getElementById('registerPassword').value;
    const confirmPassword = document.getElementById('confirmPassword').value;
    
    if (!displayName || !email || !password || !confirmPassword) {
        showMessage('Vui lòng điền đầy đủ thông tin.', 'error');
        return;
    }
    
    if (password !== confirmPassword) {
        showMessage('Mật khẩu xác nhận không khớp.', 'error');
        return;
    }
    
    if (password.length < 6) {
        showMessage('Mật khẩu phải có ít nhất 6 ký tự.', 'error');
        return;
    }
    
    await register(displayName, email, password);
});

// Login API call
async function login(email, password) {
    const btn = loginForm.querySelector('button');
    setLoading(btn, true);
    
    try {
        const response = await fetch(`${API_URL}/login`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ email, password })
        });
        
        const data = await response.json();
        
        if (data.success) {
            showMessage('Đăng nhập thành công!', 'success');
            
            // Save token and user info
            localStorage.setItem('token', data.token);
            localStorage.setItem('user', JSON.stringify(data.user));
            
            // Redirect to chat page after 1 second
            setTimeout(() => {
                window.location.href = 'chat.html';
            }, 1000);
        } else {
            showMessage(data.message || 'Đăng nhập thất bại.', 'error');
        }
    } catch (error) {
        showMessage('Không thể kết nối đến server.', 'error');
        console.error('Login error:', error);
    } finally {
        setLoading(btn, false);
    }
}

// Register API call
async function register(displayName, email, password) {
    const btn = registerForm.querySelector('button');
    setLoading(btn, true);
    
    try {
        const response = await fetch(`${API_URL}/register`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ displayName, email, password })
        });
        
        const data = await response.json();
        
        if (data.success) {
            showMessage('Đăng ký thành công! Đang chuyển sang đăng nhập...', 'success');
            
            // Switch to login tab after 1.5 seconds
            setTimeout(() => {
                tabs[0].click();
                document.getElementById('loginEmail').value = email;
                document.getElementById('loginPassword').focus();
            }, 1500);
        } else {
            showMessage(data.message || 'Đăng ký thất bại.', 'error');
        }
    } catch (error) {
        showMessage('Không thể kết nối đến server.', 'error');
        console.error('Register error:', error);
    } finally {
        setLoading(btn, false);
    }
}

// Show message
function showMessage(text, type) {
    messageEl.textContent = text;
    messageEl.className = 'message ' + type;
}

// Hide message
function hideMessage() {
    messageEl.className = 'message';
    messageEl.textContent = '';
}

// Set loading state
function setLoading(btn, isLoading) {
    if (isLoading) {
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner"></span>Đang xử lý...';
    } else {
        btn.disabled = false;
        btn.textContent = btn.closest('#loginForm') ? 'Đăng nhập' : 'Đăng ký';
    }
}

// Check if already logged in
window.addEventListener('load', () => {
    const token = localStorage.getItem('token');
    if (token) {
        // Could redirect to chat.html if already logged in
        // window.location.href = 'chat.html';
    }
});


let stickerMap = {};
let stickerList = [];

async function loadStickers() {
    const res = await fetch("http://localhost:5000/api/stickers");
    const data = await res.json();

    stickerList = data;
    stickerMap = {};

    data.forEach(s => {
        stickerMap[s.code] = s;
    });

    renderStickerPicker(stickerList); // ✅ ĐÚNG
}


function renderStickerPicker(stickers) {
    const container = document.getElementById("stickerPicker");
    container.innerHTML = "";

    stickers.forEach(sticker => {
        const img = document.createElement("img");
        img.src = "http://localhost:5000" + sticker.imageUrl;
        img.className = "sticker-item";

        img.onclick = () => {
            sendSticker(sticker.code);
        };

        container.appendChild(img);
    });
}

