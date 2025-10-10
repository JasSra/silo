// Files Module

const Files = {
    currentFiles: [],
    currentView: 'grid',

    /**
     * Initialize files module
     */
    init() {
        this.setupViewModeToggle();
        this.setupSorting();
        this.loadFiles();
    },

    /**
     * Setup view mode toggle (grid/list)
     */
    setupViewModeToggle() {
        document.querySelectorAll('.view-mode .btn-icon').forEach(button => {
            button.addEventListener('click', () => {
                const mode = button.dataset.mode;
                this.currentView = mode;
                
                // Update active button
                document.querySelectorAll('.view-mode .btn-icon').forEach(btn => {
                    btn.classList.remove('active');
                });
                button.classList.add('active');
                
                // Update grid class
                const filesGrid = document.getElementById('filesGrid');
                if (mode === 'list') {
                    filesGrid.classList.add('files-list');
                    filesGrid.classList.remove('files-grid');
                } else {
                    filesGrid.classList.add('files-grid');
                    filesGrid.classList.remove('files-list');
                }
            });
        });
    },

    /**
     * Setup sorting
     */
    setupSorting() {
        const sortSelect = document.getElementById('sortFiles');
        sortSelect.addEventListener('change', () => {
            this.sortFiles(sortSelect.value);
        });
    },

    /**
     * Load files from API
     */
    async loadFiles() {
        try {
            Utils.showLoading('Loading files...');
            
            // Use search with empty query to get all files
            const response = await API.files.search('', { limit: 100 });
            
            this.currentFiles = response.results || [];
            this.renderFiles();
            
        } catch (error) {
            console.error('Error loading files:', error);
            Utils.handleError(error, 'Failed to load files');
        } finally {
            Utils.hideLoading();
        }
    },

    /**
     * Sort files
     */
    sortFiles(sortBy) {
        const [field, direction] = sortBy.split('-');
        
        this.currentFiles.sort((a, b) => {
            let aVal, bVal;
            
            switch (field) {
                case 'name':
                    aVal = a.fileName || '';
                    bVal = b.fileName || '';
                    break;
                case 'date':
                    aVal = new Date(a.uploadedAt || a.createdAt);
                    bVal = new Date(b.uploadedAt || b.createdAt);
                    break;
                case 'size':
                    aVal = a.fileSize || 0;
                    bVal = b.fileSize || 0;
                    break;
                default:
                    return 0;
            }
            
            if (direction === 'asc') {
                return aVal > bVal ? 1 : -1;
            } else {
                return aVal < bVal ? 1 : -1;
            }
        });
        
        this.renderFiles();
    },

    /**
     * Render files in grid
     */
    renderFiles() {
        const filesGrid = document.getElementById('filesGrid');
        const filesEmpty = document.getElementById('filesEmpty');
        
        if (this.currentFiles.length === 0) {
            filesGrid.style.display = 'none';
            filesEmpty.style.display = 'block';
            return;
        }
        
        filesGrid.style.display = 'grid';
        filesEmpty.style.display = 'none';
        
        filesGrid.innerHTML = this.currentFiles.map(file => this.createFileCard(file)).join('');
        
        // Add click handlers
        filesGrid.querySelectorAll('.file-card').forEach((card, index) => {
            card.addEventListener('click', () => {
                this.openFile(this.currentFiles[index]);
            });
            
            card.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                this.showContextMenu(e, this.currentFiles[index]);
            });
        });
    },

    /**
     * Create file card HTML
     */
    createFileCard(file) {
        const icon = Utils.getFileIcon(file.fileName);
        const size = Utils.formatFileSize(file.fileSize || 0);
        const date = Utils.formatDate(file.uploadedAt || file.createdAt);
        
        return `
            <div class="file-card" data-file-id="${file.fileId || file.id}">
                <div class="file-icon">
                    <i class="fas ${icon}"></i>
                </div>
                <div class="file-name" title="${Utils.escapeHtml(file.fileName)}">${Utils.escapeHtml(file.fileName)}</div>
                <div class="file-meta">
                    <span>${size}</span>
                    <span>${date}</span>
                </div>
            </div>
        `;
    },

    /**
     * Open file details
     */
    openFile(file) {
        Utils.showToast('File Info', 
            `${file.fileName} (${Utils.formatFileSize(file.fileSize)})`, 
            'info');
    },

    /**
     * Show context menu
     */
    showContextMenu(event, file) {
        const menu = document.getElementById('fileContextMenu');
        menu.style.left = event.pageX + 'px';
        menu.style.top = event.pageY + 'px';
        menu.classList.remove('hidden');
        
        // Remove previous handlers
        const newMenu = menu.cloneNode(true);
        menu.parentNode.replaceChild(newMenu, menu);
        
        // Add new handlers
        newMenu.querySelectorAll('.context-menu-item').forEach(item => {
            item.addEventListener('click', () => {
                this.handleContextAction(item.dataset.action, file);
                newMenu.classList.add('hidden');
            });
        });
        
        // Hide on click outside
        const hideMenu = (e) => {
            if (!newMenu.contains(e.target)) {
                newMenu.classList.add('hidden');
                document.removeEventListener('click', hideMenu);
            }
        };
        setTimeout(() => document.addEventListener('click', hideMenu), 0);
    },

    /**
     * Handle context menu actions
     */
    async handleContextAction(action, file) {
        switch (action) {
            case 'download':
                try {
                    Utils.showLoading('Downloading...');
                    await API.downloadFile(file.fileId || file.id, file.fileName);
                    Utils.showToast('Success', 'File download started', 'success');
                } catch (error) {
                    Utils.handleError(error, 'Download failed');
                } finally {
                    Utils.hideLoading();
                }
                break;
                
            case 'info':
                try {
                    Utils.showLoading('Loading metadata...');
                    const metadata = await API.files.getMetadata(file.fileId || file.id);
                    Utils.showToast('File Info', JSON.stringify(metadata, null, 2), 'info', 10000);
                } catch (error) {
                    Utils.handleError(error, 'Failed to load metadata');
                } finally {
                    Utils.hideLoading();
                }
                break;
                
            case 'delete':
                if (confirm(`Are you sure you want to delete ${file.fileName}?`)) {
                    try {
                        Utils.showLoading('Deleting...');
                        await API.files.delete(file.fileId || file.id);
                        Utils.showToast('Success', 'File deleted successfully', 'success');
                        this.loadFiles();
                    } catch (error) {
                        Utils.handleError(error, 'Delete failed');
                    } finally {
                        Utils.hideLoading();
                    }
                }
                break;
        }
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Files;
}
