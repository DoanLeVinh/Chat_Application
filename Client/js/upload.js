/**
 * Upload Service - Xử lý upload file (ảnh, video, documents)
 */

class UploadService {
    constructor() {
        this.baseUrl = 'http://localhost:5000/api/uploads';
        this.pendingFiles = new Map(); // File đang chờ upload
        this.chunkSize = 1 * 1024 * 1024; // 1MB chunks for smoother progress & resume
    }

    getFingerprint(file) {
        const lastModified = typeof file.lastModified === 'number' ? file.lastModified : 0;
        return `${file.name}|${file.size}|${lastModified}`;
    }

    getResumeKey(file) {
        return `upload_resume:${this.getFingerprint(file)}`;
    }

    readResumeState(file) {
        try {
            const raw = localStorage.getItem(this.getResumeKey(file));
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    writeResumeState(file, state) {
        try {
            localStorage.setItem(this.getResumeKey(file), JSON.stringify(state));
        } catch {
            // ignore quota
        }
    }

    clearResumeState(file) {
        try {
            localStorage.removeItem(this.getResumeKey(file));
        } catch {
            // ignore
        }
    }

    async getUploadStatus(sessionId, token) {
        const response = await fetch(`${this.baseUrl}/status/${sessionId}`, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        if (!response.ok) {
            const err = await response.json().catch(() => ({}));
            throw new Error(err.error || 'Failed to get upload status');
        }

        return await response.json();
    }

    async completeUpload(sessionId, token) {
        const completeResponse = await fetch(`${this.baseUrl}/complete/${sessionId}`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        if (!completeResponse.ok) {
            const err = await completeResponse.json().catch(() => ({}));
            throw new Error(err.error || 'Failed to complete upload');
        }

        return await completeResponse.json();
    }

    /**
     * Upload file đơn giản (< 5MB)
     */
    async uploadSimple(file, token) {
        const formData = new FormData();
        formData.append('file', file);

        const response = await fetch(`${this.baseUrl}/simple`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`
            },
            body: formData
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Upload failed');
        }

        return await response.json();
    }

    /**
     * Upload file lớn với chunked upload
     */
    async uploadChunked(file, token, onProgress) {
        // Kept for backward compatibility; use uploadResumable() for resume support.
        return await this.uploadResumable(file, token, onProgress);
    }

    /**
     * Upload resumable (chunked) - lưu sessionId + uploadedSize để resume khi mất mạng
     */
    async uploadResumable(file, token, onProgress, onState) {
        const resumeKey = this.getResumeKey(file);
        const saved = this.readResumeState(file);

        let sessionId = saved?.sessionId || null;
        let uploadedSize = Number.isFinite(saved?.uploadedSize) ? saved.uploadedSize : 0;

        // Nếu có session cũ -> hỏi server uploadedSize để đồng bộ (tránh lệch local)
        if (sessionId) {
            try {
                const status = await this.getUploadStatus(sessionId, token);
                if (status?.status === 'completed' && status?.url) {
                    this.clearResumeState(file);
                    if (onProgress) onProgress(100);
                    if (onState) onState({ status: 'completed', sessionId, uploadedSize: file.size });
                    return { url: status.url, sessionId };
                }
                if (typeof status?.uploadedSize === 'number') {
                    uploadedSize = status.uploadedSize;
                }
            } catch {
                // Nếu status fail -> tạo session mới
                sessionId = null;
                uploadedSize = 0;
            }
        }

        // Chưa có session -> initiate
        if (!sessionId) {
            const initResponse = await fetch(`${this.baseUrl}/initiate`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    fileName: file.name,
                    fileSize: file.size,
                    contentType: file.type
                })
            });

            if (!initResponse.ok) {
                const error = await initResponse.json().catch(() => ({}));
                throw new Error(error.error || 'Failed to initiate upload');
            }

            const initData = await initResponse.json();
            sessionId = initData.sessionId;
            uploadedSize = 0;
        }

        // persist initial
        this.writeResumeState(file, {
            sessionId,
            uploadedSize,
            totalSize: file.size,
            fileName: file.name,
            fileType: file.type,
            updatedAt: Date.now()
        });

        if (onState) onState({ status: 'uploading', sessionId, uploadedSize });

        const chunkSize = this.chunkSize;
        while (uploadedSize < file.size) {
            // Nếu offline thì pause
            if (typeof navigator !== 'undefined' && navigator.onLine === false) {
                this.writeResumeState(file, {
                    sessionId,
                    uploadedSize,
                    totalSize: file.size,
                    fileName: file.name,
                    fileType: file.type,
                    updatedAt: Date.now(),
                    paused: true
                });
                if (onState) onState({ status: 'paused', sessionId, uploadedSize });
                const err = new Error('OFFLINE');
                err.code = 'OFFLINE';
                throw err;
            }

            const start = uploadedSize;
            const end = Math.min(start + chunkSize, file.size);
            const chunk = file.slice(start, end);

            try {
                const chunkResponse = await fetch(`${this.baseUrl}/chunk/${sessionId}?offset=${start}`, {
                    method: 'POST',
                    headers: {
                        'Authorization': `Bearer ${token}`
                    },
                    body: chunk
                });

                if (!chunkResponse.ok) {
                    const error = await chunkResponse.json().catch(() => ({}));
                    throw new Error(error.error || 'Failed to upload chunk');
                }

                const resp = await chunkResponse.json().catch(() => ({}));
                if (typeof resp.uploadedSize === 'number') {
                    uploadedSize = resp.uploadedSize;
                } else {
                    uploadedSize += chunk.size;
                }

                this.writeResumeState(file, {
                    sessionId,
                    uploadedSize,
                    totalSize: file.size,
                    fileName: file.name,
                    fileType: file.type,
                    updatedAt: Date.now()
                });

                if (onProgress) {
                    onProgress((uploadedSize / file.size) * 100);
                }
                if (onState) onState({ status: 'uploading', sessionId, uploadedSize });
            } catch (e) {
                // Lưu tiến độ hiện tại để resume
                this.writeResumeState(file, {
                    sessionId,
                    uploadedSize,
                    totalSize: file.size,
                    fileName: file.name,
                    fileType: file.type,
                    updatedAt: Date.now(),
                    paused: true
                });
                if (onState) onState({ status: 'paused', sessionId, uploadedSize });
                throw e;
            }
        }

        // complete
        const completed = await this.completeUpload(sessionId, token);
        this.clearResumeState(file);
        if (onProgress) onProgress(100);
        if (onState) onState({ status: 'completed', sessionId, uploadedSize: file.size });
        return completed;
    }

    /**
     * Upload file (tự động chọn phương thức phù hợp)
     */
    async upload(file, token, onProgress) {
        // Always use resumable chunked upload to support resume-from-progress.
        return await this.uploadResumable(file, token, onProgress);
    }

    /**
     * Lấy icon cho file type
     */
    getFileIcon(contentType) {
        if (contentType.startsWith('image/')) return '🖼️';
        if (contentType.startsWith('video/')) return '🎥';
        if (contentType.includes('pdf')) return '📄';
        if (contentType.includes('word') || contentType.includes('document')) return '📝';
        if (contentType.includes('excel') || contentType.includes('sheet')) return '📊';
        if (contentType.includes('zip') || contentType.includes('rar')) return '📦';
        return '📎';
    }

    /**
     * Format kích thước file
     */
    formatFileSize(bytes) {
        if (bytes === 0) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
    }

    /**
     * Validate file
     */
    validateFile(file) {
        const maxSize = 100 * 1024 * 1024; // 100MB - cho phép video và nhiều loại file hơn

        if (file.size > maxSize) {
            throw new Error(`File quá lớn. Tối đa 100MB.`);
        }

        // Không giới hạn theo MIME type nữa để cho phép mọi loại file
        return true;
    }

    /**
     * Tạo preview cho file
     */
    createFilePreview(file) {
        return new Promise((resolve, reject) => {
            if (file.type.startsWith('image/')) {
                const reader = new FileReader();
                reader.onload = (e) => resolve(e.target.result);
                reader.onerror = reject;
                reader.readAsDataURL(file);
            } else {
                resolve(null);
            }
        });
    }
}

// Export
window.UploadService = UploadService;
