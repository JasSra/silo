// Upload Module

const Upload = {
    uploadQueue: [],

    /**
     * Initialize upload module
     */
    init() {
        this.setupDropZone();
        this.setupBrowseButton();
        this.setupUploadActions();
    },

    /**
     * Setup drag and drop zone
     */
    setupDropZone() {
        const dropZone = document.getElementById('dropZone');
        
        dropZone.addEventListener('click', () => {
            document.getElementById('fileInput').click();
        });
        
        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropZone.classList.add('drag-over');
        });
        
        dropZone.addEventListener('dragleave', () => {
            dropZone.classList.remove('drag-over');
        });
        
        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropZone.classList.remove('drag-over');
            
            const files = Array.from(e.dataTransfer.files);
            this.addFilesToQueue(files);
        });
    },

    /**
     * Setup browse button
     */
    setupBrowseButton() {
        const fileInput = document.getElementById('fileInput');
        const browseBtn = document.getElementById('browseBtn');
        
        browseBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            fileInput.click();
        });
        
        fileInput.addEventListener('change', () => {
            const files = Array.from(fileInput.files);
            this.addFilesToQueue(files);
            fileInput.value = ''; // Reset input
        });
    },

    /**
     * Setup upload action buttons
     */
    setupUploadActions() {
        document.getElementById('startUpload').addEventListener('click', () => {
            this.startUpload();
        });
        
        document.getElementById('clearQueue').addEventListener('click', () => {
            this.clearQueue();
        });
    },

    /**
     * Add files to upload queue
     */
    addFilesToQueue(files) {
        files.forEach(file => {
            // Check file size
            if (file.size > CONFIG.MAX_FILE_SIZE) {
                Utils.showToast('File Too Large', 
                    `${file.name} exceeds maximum size of ${Utils.formatFileSize(CONFIG.MAX_FILE_SIZE)}`, 
                    'warning');
                return;
            }
            
            this.uploadQueue.push({
                file,
                id: Date.now() + Math.random(),
                status: 'pending',
                progress: 0
            });
        });
        
        this.renderQueue();
    },

    /**
     * Render upload queue
     */
    renderQueue() {
        const queueContainer = document.getElementById('uploadQueue');
        const uploadList = document.getElementById('uploadList');
        
        if (this.uploadQueue.length === 0) {
            queueContainer.classList.add('hidden');
            return;
        }
        
        queueContainer.classList.remove('hidden');
        
        uploadList.innerHTML = this.uploadQueue.map(item => {
            const icon = Utils.getFileIcon(item.file.name);
            const size = Utils.formatFileSize(item.file.size);
            
            let statusClass = '';
            let statusIcon = '';
            if (item.status === 'uploading') {
                statusClass = 'uploading';
                statusIcon = '<i class="fas fa-spinner fa-spin"></i>';
            } else if (item.status === 'success') {
                statusClass = 'success';
                statusIcon = '<i class="fas fa-check-circle"></i>';
            } else if (item.status === 'error') {
                statusClass = 'error';
                statusIcon = '<i class="fas fa-exclamation-circle"></i>';
            }
            
            return `
                <div class="upload-item ${statusClass}">
                    <div class="upload-item-icon">
                        <i class="fas ${icon}"></i>
                    </div>
                    <div class="upload-item-info">
                        <div class="upload-item-name">${Utils.escapeHtml(item.file.name)}</div>
                        <div class="upload-item-size">${size}</div>
                        ${item.status === 'uploading' || item.status === 'success' ? `
                            <div class="upload-progress">
                                <div class="upload-progress-bar" style="width: ${item.progress}%"></div>
                            </div>
                        ` : ''}
                    </div>
                    <div class="upload-status">
                        ${statusIcon}
                    </div>
                </div>
            `;
        }).join('');
    },

    /**
     * Start uploading files
     */
    async startUpload() {
        const pendingFiles = this.uploadQueue.filter(item => item.status === 'pending');
        
        if (pendingFiles.length === 0) {
            Utils.showToast('No Files', 'No files to upload', 'warning');
            return;
        }
        
        for (const item of pendingFiles) {
            try {
                item.status = 'uploading';
                this.renderQueue();
                
                await API.uploadFile(item.file, (progress) => {
                    item.progress = Math.round(progress);
                    this.renderQueue();
                });
                
                item.status = 'success';
                item.progress = 100;
                this.renderQueue();
                
                Utils.showToast('Success', `${item.file.name} uploaded successfully`, 'success');
                
            } catch (error) {
                console.error('Upload error:', error);
                item.status = 'error';
                this.renderQueue();
                
                Utils.showToast('Upload Failed', 
                    `Failed to upload ${item.file.name}`, 
                    'error');
            }
        }
        
        // Refresh files list
        if (typeof Files !== 'undefined') {
            Files.loadFiles();
        }
    },

    /**
     * Clear upload queue
     */
    clearQueue() {
        this.uploadQueue = [];
        this.renderQueue();
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Upload;
}
