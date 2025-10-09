// Configuration
const CONFIG = {
    API_BASE_URL: 'http://localhost:5289/api',
    API_TIMEOUT: 30000,
    MAX_FILE_SIZE: 100 * 1024 * 1024, // 100MB
    ALLOWED_FILE_TYPES: '*',
    STORAGE_KEY_PREFIX: 'silo_',
    TOAST_DURATION: 5000,
    PAGINATION: {
        DEFAULT_PAGE_SIZE: 20,
        MAX_PAGE_SIZE: 100
    },
    FILE_ICONS: {
        'pdf': 'fa-file-pdf',
        'doc': 'fa-file-word',
        'docx': 'fa-file-word',
        'xls': 'fa-file-excel',
        'xlsx': 'fa-file-excel',
        'ppt': 'fa-file-powerpoint',
        'pptx': 'fa-file-powerpoint',
        'txt': 'fa-file-alt',
        'jpg': 'fa-file-image',
        'jpeg': 'fa-file-image',
        'png': 'fa-file-image',
        'gif': 'fa-file-image',
        'svg': 'fa-file-image',
        'mp4': 'fa-file-video',
        'avi': 'fa-file-video',
        'mov': 'fa-file-video',
        'mp3': 'fa-file-audio',
        'wav': 'fa-file-audio',
        'zip': 'fa-file-archive',
        'rar': 'fa-file-archive',
        'tar': 'fa-file-archive',
        'gz': 'fa-file-archive',
        'js': 'fa-file-code',
        'html': 'fa-file-code',
        'css': 'fa-file-code',
        'json': 'fa-file-code',
        'xml': 'fa-file-code',
        'default': 'fa-file'
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = CONFIG;
}
