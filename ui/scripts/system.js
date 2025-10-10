// System Status Module

const System = {
    /**
     * Initialize system module
     */
    init() {
        this.setupEventListeners();
        this.loadSystemStatus();
        // Auto-refresh every 30 seconds
        this.refreshInterval = setInterval(() => this.loadSystemStatus(), 30000);
    },

    /**
     * Setup event listeners
     */
    setupEventListeners() {
        document.getElementById('refreshSystem')?.addEventListener('click', () => {
            this.loadSystemStatus();
        });
    },

    /**
     * Load system status
     */
    async loadSystemStatus() {
        try {
            // In a real implementation, this would call the /health endpoint
            // For now, we'll use the existing health check
            
            // Update dashboard stats
            this.updateDashboardStats();
            
        } catch (error) {
            console.error('Error loading system status:', error);
        }
    },

    /**
     * Update dashboard statistics
     */
    updateDashboardStats() {
        // Mock data for demonstration
        document.getElementById('activeUsers').textContent = '12';
        document.getElementById('runningJobs').textContent = '3';
        document.getElementById('totalBuckets').textContent = '15';
        document.getElementById('alertsCount').textContent = '0';
    },

    /**
     * Cleanup on destroy
     */
    destroy() {
        if (this.refreshInterval) {
            clearInterval(this.refreshInterval);
        }
    }
};

// Notifications Module
const Notifications = {
    /**
     * Initialize notifications
     */
    init() {
        this.setupEventListeners();
    },

    /**
     * Setup event listeners
     */
    setupEventListeners() {
        document.getElementById('notificationsBtn')?.addEventListener('click', () => {
            this.togglePanel();
        });

        document.getElementById('closeNotifications')?.addEventListener('click', () => {
            this.hidePanel();
        });

        document.getElementById('markAllRead')?.addEventListener('click', () => {
            this.markAllAsRead();
        });

        // Close panel when clicking outside
        document.addEventListener('click', (e) => {
            const panel = document.getElementById('notificationsPanel');
            const btn = document.getElementById('notificationsBtn');
            if (panel && !panel.contains(e.target) && !btn?.contains(e.target)) {
                this.hidePanel();
            }
        });
    },

    /**
     * Toggle notifications panel
     */
    togglePanel() {
        const panel = document.getElementById('notificationsPanel');
        panel?.classList.toggle('hidden');
    },

    /**
     * Hide notifications panel
     */
    hidePanel() {
        const panel = document.getElementById('notificationsPanel');
        panel?.classList.add('hidden');
    },

    /**
     * Mark all notifications as read
     */
    markAllAsRead() {
        const unreadItems = document.querySelectorAll('.notification-item.unread');
        unreadItems.forEach(item => item.classList.remove('unread'));
        
        const badge = document.querySelector('#notificationsBtn .badge');
        if (badge) badge.textContent = '0';
        
        Utils.showToast('Success', 'All notifications marked as read', 'success');
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { System, Notifications };
}
