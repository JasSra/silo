// Buckets Management Module

const Buckets = {
    currentBuckets: [],

    /**
     * Initialize buckets module
     */
    init() {
        this.setupEventListeners();
        this.loadBuckets();
    },

    /**
     * Setup event listeners
     */
    setupEventListeners() {
        document.getElementById('refreshBuckets')?.addEventListener('click', () => {
            this.loadBuckets();
        });

        document.getElementById('createBucketBtn')?.addEventListener('click', () => {
            this.showCreateBucketModal();
        });

        document.getElementById('closeBucketModal')?.addEventListener('click', () => {
            this.hideCreateBucketModal();
        });

        document.getElementById('cancelBucketCreate')?.addEventListener('click', () => {
            this.hideCreateBucketModal();
        });

        document.getElementById('confirmBucketCreate')?.addEventListener('click', () => {
            this.createBucket();
        });
    },

    /**
     * Load buckets from API
     */
    async loadBuckets() {
        try {
            Utils.showLoading('Loading buckets...');
            
            const response = await API.get('/buckets');
            
            this.currentBuckets = response.Buckets || [];
            this.renderBuckets();
            
        } catch (error) {
            console.error('Error loading buckets:', error);
            Utils.handleError(error, 'Failed to load buckets');
        } finally {
            Utils.hideLoading();
        }
    },

    /**
     * Render buckets grid
     */
    renderBuckets() {
        const bucketsGrid = document.getElementById('bucketsGrid');
        const bucketsEmpty = document.getElementById('bucketsEmpty');
        
        if (this.currentBuckets.length === 0) {
            bucketsGrid.innerHTML = '';
            bucketsEmpty?.classList.remove('hidden');
            return;
        }
        
        bucketsEmpty?.classList.add('hidden');
        
        bucketsGrid.innerHTML = this.currentBuckets.map(bucket => this.createBucketCard(bucket)).join('');
        
        // Add event listeners
        bucketsGrid.querySelectorAll('.bucket-delete').forEach((btn, index) => {
            btn.addEventListener('click', () => this.deleteBucket(this.currentBuckets[index]));
        });
    },

    /**
     * Create bucket card HTML
     */
    createBucketCard(bucket) {
        const icon = bucket.Type === 'files' ? 'fa-folder' :
                     bucket.Type === 'thumbnails' ? 'fa-image' :
                     bucket.Type === 'versions' ? 'fa-history' :
                     bucket.Type === 'backups' ? 'fa-database' : 'fa-hdd';

        return `
            <div class="bucket-card">
                <div class="bucket-header">
                    <div class="bucket-icon">
                        <i class="fas ${icon}"></i>
                    </div>
                    <div class="bucket-info">
                        <h4>${Utils.escapeHtml(bucket.Name)}</h4>
                        <div class="bucket-type">${Utils.escapeHtml(bucket.Type)}</div>
                    </div>
                </div>
                <div class="bucket-stats">
                    <div class="bucket-stat">
                        <span class="bucket-stat-label">Objects:</span>
                        <span>${bucket.ObjectCount || 0}</span>
                    </div>
                    <div class="bucket-stat">
                        <span class="bucket-stat-label">Size:</span>
                        <span>${Utils.formatFileSize(bucket.Size || 0)}</span>
                    </div>
                    <div class="bucket-stat">
                        <span class="bucket-stat-label">Created:</span>
                        <span>${Utils.formatDate(bucket.CreatedAt)}</span>
                    </div>
                </div>
                <div class="bucket-actions">
                    <button class="btn btn-outline btn-sm bucket-browse">
                        <i class="fas fa-folder-open"></i> Browse
                    </button>
                    <button class="btn btn-outline btn-sm bucket-delete">
                        <i class="fas fa-trash"></i> Delete
                    </button>
                </div>
            </div>
        `;
    },

    /**
     * Show create bucket modal
     */
    showCreateBucketModal() {
        const modal = document.getElementById('createBucketModal');
        modal?.classList.remove('hidden');
    },

    /**
     * Hide create bucket modal
     */
    hideCreateBucketModal() {
        const modal = document.getElementById('createBucketModal');
        modal?.classList.add('hidden');
        document.getElementById('newBucketName').value = '';
        document.getElementById('newBucketDesc').value = '';
        document.getElementById('bucketNameError').textContent = '';
    },

    /**
     * Create new bucket
     */
    async createBucket() {
        const name = document.getElementById('newBucketName').value.trim();
        const type = document.getElementById('newBucketType').value;
        const description = document.getElementById('newBucketDesc').value.trim();
        const errorEl = document.getElementById('bucketNameError');

        // Validation
        if (!name) {
            errorEl.textContent = 'Bucket name is required';
            return;
        }

        if (!/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(name)) {
            errorEl.textContent = 'Invalid bucket name format';
            return;
        }

        try {
            Utils.showLoading('Creating bucket...');
            
            await API.post('/buckets', { name, type, description });
            
            Utils.showToast('Success', `Bucket "${name}" created successfully`, 'success');
            this.hideCreateBucketModal();
            this.loadBuckets();
            
        } catch (error) {
            console.error('Error creating bucket:', error);
            Utils.handleError(error, 'Failed to create bucket');
        } finally {
            Utils.hideLoading();
        }
    },

    /**
     * Delete bucket
     */
    async deleteBucket(bucket) {
        if (!confirm(`Are you sure you want to delete bucket "${bucket.Name}"?`)) {
            return;
        }

        try {
            Utils.showLoading('Deleting bucket...');
            
            await API.delete(`/buckets/${bucket.Name}`);
            
            Utils.showToast('Success', `Bucket "${bucket.Name}" deleted successfully`, 'success');
            this.loadBuckets();
            
        } catch (error) {
            console.error('Error deleting bucket:', error);
            Utils.handleError(error, 'Failed to delete bucket');
        } finally {
            Utils.hideLoading();
        }
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Buckets;
}
