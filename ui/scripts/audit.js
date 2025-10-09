// Audit Logs Module

const Audit = {
    currentLogs: [],
    filters: {
        action: 'all',
        dateFrom: null,
        dateTo: null,
        user: ''
    },

    /**
     * Initialize audit module
     */
    init() {
        this.setupEventListeners();
        this.loadAuditLogs();
    },

    /**
     * Setup event listeners
     */
    setupEventListeners() {
        document.getElementById('refreshAudit')?.addEventListener('click', () => {
            this.loadAuditLogs();
        });

        document.getElementById('auditActionFilter')?.addEventListener('change', (e) => {
            this.filters.action = e.target.value;
            this.renderAuditLogs();
        });

        document.getElementById('applyAuditFilters')?.addEventListener('click', () => {
            this.applyFilters();
        });

        document.getElementById('exportAudit')?.addEventListener('click', () => {
            this.exportLogs();
        });
    },

    /**
     * Load audit logs from API
     */
    async loadAuditLogs() {
        try {
            Utils.showLoading('Loading audit logs...');
            
            // Mock data for demonstration
            this.currentLogs = [
                {
                    timestamp: new Date().toISOString(),
                    user: 'admin@example.com',
                    action: 'file.upload',
                    resource: 'document.pdf',
                    ipAddress: '192.168.1.100',
                    status: 'success',
                    details: 'File uploaded successfully'
                },
                {
                    timestamp: new Date(Date.now() - 300000).toISOString(),
                    user: 'john.doe@example.com',
                    action: 'user.login',
                    resource: '-',
                    ipAddress: '192.168.1.101',
                    status: 'success',
                    details: 'User logged in'
                },
                {
                    timestamp: new Date(Date.now() - 600000).toISOString(),
                    user: 'admin@example.com',
                    action: 'file.delete',
                    resource: 'old-backup.zip',
                    ipAddress: '192.168.1.100',
                    status: 'success',
                    details: 'File deleted'
                },
                {
                    timestamp: new Date(Date.now() - 900000).toISOString(),
                    user: 'jane.smith@example.com',
                    action: 'file.download',
                    resource: 'report.xlsx',
                    ipAddress: '192.168.1.102',
                    status: 'success',
                    details: 'File downloaded'
                },
                {
                    timestamp: new Date(Date.now() - 1200000).toISOString(),
                    user: 'admin@example.com',
                    action: 'admin.action',
                    resource: 'Quota update',
                    ipAddress: '192.168.1.100',
                    status: 'success',
                    details: 'Storage quota increased'
                }
            ];
            
            this.renderAuditLogs();
            
        } catch (error) {
            console.error('Error loading audit logs:', error);
            Utils.handleError(error, 'Failed to load audit logs');
        } finally {
            Utils.hideLoading();
        }
    },

    /**
     * Apply filters
     */
    applyFilters() {
        this.filters.dateFrom = document.getElementById('auditDateFrom').value;
        this.filters.dateTo = document.getElementById('auditDateTo').value;
        this.filters.user = document.getElementById('auditUserFilter').value.toLowerCase();
        this.renderAuditLogs();
    },

    /**
     * Render audit logs table
     */
    renderAuditLogs() {
        const tbody = document.getElementById('auditTableBody');
        if (!tbody) return;

        let filteredLogs = this.currentLogs;

        // Filter by action
        if (this.filters.action !== 'all') {
            filteredLogs = filteredLogs.filter(log => log.action === this.filters.action);
        }

        // Filter by user
        if (this.filters.user) {
            filteredLogs = filteredLogs.filter(log => 
                log.user.toLowerCase().includes(this.filters.user)
            );
        }

        // Filter by date range
        if (this.filters.dateFrom) {
            const fromDate = new Date(this.filters.dateFrom);
            filteredLogs = filteredLogs.filter(log => new Date(log.timestamp) >= fromDate);
        }

        if (this.filters.dateTo) {
            const toDate = new Date(this.filters.dateTo);
            toDate.setHours(23, 59, 59, 999);
            filteredLogs = filteredLogs.filter(log => new Date(log.timestamp) <= toDate);
        }

        tbody.innerHTML = filteredLogs.map(log => `
            <tr>
                <td>${Utils.formatDate(log.timestamp, false)}</td>
                <td>${Utils.escapeHtml(log.user)}</td>
                <td><span class="status-badge">${log.action}</span></td>
                <td>${Utils.escapeHtml(log.resource)}</td>
                <td><code>${log.ipAddress}</code></td>
                <td>
                    <span class="status-badge ${log.status === 'success' ? 'completed' : 'failed'}">
                        ${log.status}
                    </span>
                </td>
                <td>
                    <button class="btn-icon" title="View details" onclick="alert('${Utils.escapeHtml(log.details)}')">
                        <i class="fas fa-info-circle"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    },

    /**
     * Export audit logs to CSV
     */
    exportLogs() {
        const csv = [
            ['Timestamp', 'User', 'Action', 'Resource', 'IP Address', 'Status', 'Details'].join(','),
            ...this.currentLogs.map(log => [
                log.timestamp,
                log.user,
                log.action,
                log.resource,
                log.ipAddress,
                log.status,
                log.details
            ].map(field => `"${field}"`).join(','))
        ].join('\n');

        const blob = new Blob([csv], { type: 'text/csv' });
        Utils.downloadBlob(blob, `audit-logs-${new Date().toISOString().split('T')[0]}.csv`);
        Utils.showToast('Success', 'Audit logs exported successfully', 'success');
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Audit;
}
