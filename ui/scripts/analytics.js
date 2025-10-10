// Analytics Module

const Analytics = {
    /**
     * Initialize analytics module
     */
    init() {
        this.loadAnalytics();
    },

    /**
     * Load analytics data
     */
    async loadAnalytics() {
        try {
            Utils.showLoading('Loading analytics...');
            
            // Load file statistics
            const stats = await API.files.getStatistics();
            this.renderStatistics(stats);
            
            // Load quota and usage
            const quota = await API.tenant.getQuota();
            const usage = await API.tenant.getUsage();
            this.updateStorageUsage(usage, quota);
            
        } catch (error) {
            console.error('Error loading analytics:', error);
            // Don't show error toast, just log it
            // Some endpoints might not be available
        } finally {
            Utils.hideLoading();
        }
    },

    /**
     * Render statistics cards
     */
    renderStatistics(stats) {
        // Update stat cards
        document.getElementById('totalFiles').textContent = stats.totalFiles || 0;
        document.getElementById('totalStorage').textContent = Utils.formatFileSize(stats.totalSize || 0);
        document.getElementById('uploadsToday').textContent = stats.uploadsToday || 0;
        document.getElementById('downloadsToday').textContent = stats.downloadsToday || 0;
        
        // Render file types distribution
        if (stats.fileTypes) {
            this.renderFileTypes(stats.fileTypes);
        }
    },

    /**
     * Render file types distribution
     */
    renderFileTypes(fileTypes) {
        const container = document.getElementById('fileTypesList');
        
        if (!fileTypes || Object.keys(fileTypes).length === 0) {
            container.innerHTML = '<p style="color: var(--color-text-secondary);">No data available</p>';
            return;
        }
        
        // Calculate total
        const total = Object.values(fileTypes).reduce((sum, count) => sum + count, 0);
        
        // Sort by count
        const sortedTypes = Object.entries(fileTypes)
            .sort((a, b) => b[1] - a[1])
            .slice(0, 10); // Top 10
        
        container.innerHTML = sortedTypes.map(([type, count]) => {
            const percentage = ((count / total) * 100).toFixed(1);
            const icon = Utils.getFileIcon(`file.${type}`);
            
            return `
                <div class="type-item">
                    <div class="type-info">
                        <i class="fas ${icon} type-icon"></i>
                        <span>${type.toUpperCase()}</span>
                    </div>
                    <div class="type-bar">
                        <div class="type-bar-fill" style="width: ${percentage}%"></div>
                    </div>
                    <span>${count} (${percentage}%)</span>
                </div>
            `;
        }).join('');
    },

    /**
     * Update storage usage display
     */
    updateStorageUsage(usage, quota) {
        const usageFill = document.getElementById('storageUsageFill');
        const usageText = document.getElementById('storageUsageText');
        
        const usedBytes = usage?.storageUsed || 0;
        const quotaBytes = quota?.storageQuotaBytes || 0;
        
        if (quotaBytes > 0) {
            const percentage = (usedBytes / quotaBytes) * 100;
            usageFill.style.width = percentage + '%';
            usageText.textContent = `${Utils.formatFileSize(usedBytes)} / ${Utils.formatFileSize(quotaBytes)}`;
        } else {
            usageFill.style.width = '0%';
            usageText.textContent = `${Utils.formatFileSize(usedBytes)} / Unlimited`;
        }
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Analytics;
}
