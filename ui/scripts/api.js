// API Client

const API = {
    /**
     * Base request method
     */
    async request(endpoint, options = {}) {
        const url = `${CONFIG.API_BASE_URL}${endpoint}`;
        const token = Utils.storage.get('accessToken');

        const headers = {
            'Content-Type': 'application/json',
            ...options.headers
        };

        if (token && !options.skipAuth) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        const config = {
            ...options,
            headers,
            signal: options.signal || AbortSignal.timeout(CONFIG.API_TIMEOUT)
        };

        try {
            const response = await fetch(url, config);
            
            // Handle 401 - token expired
            if (response.status === 401 && !options.skipAuth) {
                // Try to refresh token
                const refreshed = await this.refreshToken();
                if (refreshed) {
                    // Retry the original request
                    return this.request(endpoint, options);
                } else {
                    // Redirect to login
                    window.location.href = '/ui/index.html';
                    throw new Error('Session expired');
                }
            }

            // Parse response
            let data;
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                data = await response.json();
            } else {
                data = await response.text();
            }

            if (!response.ok) {
                throw {
                    response: {
                        status: response.status,
                        statusText: response.statusText,
                        data
                    }
                };
            }

            return data;
        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('Request timeout');
            }
            throw error;
        }
    },

    /**
     * GET request
     */
    async get(endpoint, params = {}, options = {}) {
        const queryString = new URLSearchParams(params).toString();
        const url = queryString ? `${endpoint}?${queryString}` : endpoint;
        return this.request(url, { ...options, method: 'GET' });
    },

    /**
     * POST request
     */
    async post(endpoint, data, options = {}) {
        return this.request(endpoint, {
            ...options,
            method: 'POST',
            body: JSON.stringify(data)
        });
    },

    /**
     * PUT request
     */
    async put(endpoint, data, options = {}) {
        return this.request(endpoint, {
            ...options,
            method: 'PUT',
            body: JSON.stringify(data)
        });
    },

    /**
     * DELETE request
     */
    async delete(endpoint, options = {}) {
        return this.request(endpoint, {
            ...options,
            method: 'DELETE'
        });
    },

    /**
     * Upload file with progress
     */
    async uploadFile(file, onProgress) {
        const formData = new FormData();
        formData.append('file', file);

        const token = Utils.storage.get('accessToken');
        const url = `${CONFIG.API_BASE_URL}/files/upload`;

        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            // Progress handler
            if (onProgress) {
                xhr.upload.addEventListener('progress', (e) => {
                    if (e.lengthComputable) {
                        const percentComplete = (e.loaded / e.total) * 100;
                        onProgress(percentComplete);
                    }
                });
            }

            // Load handler
            xhr.addEventListener('load', () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        const response = JSON.parse(xhr.responseText);
                        resolve(response);
                    } catch (e) {
                        reject(new Error('Invalid response format'));
                    }
                } else {
                    reject({
                        response: {
                            status: xhr.status,
                            statusText: xhr.statusText,
                            data: xhr.responseText
                        }
                    });
                }
            });

            // Error handler
            xhr.addEventListener('error', () => {
                reject(new Error('Network error'));
            });

            // Timeout handler
            xhr.timeout = CONFIG.API_TIMEOUT;
            xhr.addEventListener('timeout', () => {
                reject(new Error('Request timeout'));
            });

            xhr.open('POST', url);
            if (token) {
                xhr.setRequestHeader('Authorization', `Bearer ${token}`);
            }
            xhr.send(formData);
        });
    },

    /**
     * Download file
     */
    async downloadFile(fileId, filename) {
        const token = Utils.storage.get('accessToken');
        const url = `${CONFIG.API_BASE_URL}/files/${fileId}/download`;

        const response = await fetch(url, {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        if (!response.ok) {
            throw new Error('Download failed');
        }

        const blob = await response.blob();
        Utils.downloadBlob(blob, filename);
    },

    // Auth endpoints
    auth: {
        async login(username, password) {
            return API.post('/auth/login', { username, password }, { skipAuth: true });
        },

        async signup(username, email, password) {
            return API.post('/auth/signup', { username, email, password }, { skipAuth: true });
        },

        async logout() {
            try {
                await API.post('/auth/logout');
            } finally {
                Utils.storage.clear();
            }
        },

        async refreshToken() {
            const refreshToken = Utils.storage.get('refreshToken');
            if (!refreshToken) return false;

            try {
                const response = await API.post('/auth/refresh', { refreshToken }, { skipAuth: true });
                Utils.storage.set('accessToken', response.accessToken);
                Utils.storage.set('refreshToken', response.refreshToken);
                return true;
            } catch (e) {
                Utils.storage.clear();
                return false;
            }
        }
    },

    // File endpoints
    files: {
        async search(query, options = {}) {
            return API.get('/files/search', { query, ...options });
        },

        async advancedSearch(filters) {
            return API.post('/files/advanced-search', filters);
        },

        async getMetadata(fileId) {
            return API.get(`/files/${fileId}/metadata`);
        },

        async delete(fileId) {
            return API.delete(`/files/${fileId}`);
        },

        async getStatistics() {
            return API.get('/files/statistics');
        }
    },

    // Tenant endpoints
    tenant: {
        async getQuota() {
            return API.get('/tenants/current/quota');
        },

        async getUsage() {
            return API.get('/tenants/current/usage');
        }
    }
};

// Helper to refresh token
API.refreshToken = API.auth.refreshToken;

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = API;
}
