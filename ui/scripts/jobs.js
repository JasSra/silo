// Background Jobs Module

const Jobs = {
    currentJobs: [],
    jobStatusFilter: 'all',

    /**
     * Initialize jobs module
     */
    init() {
        this.setupEventListeners();
        this.loadJobs();
        // Auto-refresh every 5 seconds
        this.refreshInterval = setInterval(() => this.loadJobs(), 5000);
    },

    /**
     * Setup event listeners
     */
    setupEventListeners() {
        document.getElementById('refreshJobs')?.addEventListener('click', () => {
            this.loadJobs();
        });

        document.getElementById('jobStatusFilter')?.addEventListener('change', (e) => {
            this.jobStatusFilter = e.target.value;
            this.renderJobs();
        });
    },

    /**
     * Load jobs from API
     */
    async loadJobs() {
        try {
            // Mock data for demonstration
            this.currentJobs = [
                {
                    id: 'job-001',
                    type: 'File Backup',
                    status: 'running',
                    progress: 65,
                    createdAt: new Date().toISOString()
                },
                {
                    id: 'job-002',
                    type: 'Thumbnail Generation',
                    status: 'pending',
                    progress: 0,
                    createdAt: new Date(Date.now() - 300000).toISOString()
                },
                {
                    id: 'job-003',
                    type: 'File Indexing',
                    status: 'completed',
                    progress: 100,
                    createdAt: new Date(Date.now() - 600000).toISOString()
                },
                {
                    id: 'job-004',
                    type: 'Malware Scan',
                    status: 'failed',
                    progress: 45,
                    createdAt: new Date(Date.now() - 900000).toISOString()
                }
            ];
            
            this.updateJobStats();
            this.renderJobs();
            
        } catch (error) {
            console.error('Error loading jobs:', error);
        }
    },

    /**
     * Update job statistics
     */
    updateJobStats() {
        const stats = {
            running: this.currentJobs.filter(j => j.status === 'running').length,
            pending: this.currentJobs.filter(j => j.status === 'pending').length,
            completed: this.currentJobs.filter(j => j.status === 'completed').length,
            failed: this.currentJobs.filter(j => j.status === 'failed').length
        };

        document.getElementById('jobsRunning').textContent = stats.running;
        document.getElementById('jobsPending').textContent = stats.pending;
        document.getElementById('jobsCompleted').textContent = stats.completed;
        document.getElementById('jobsFailed').textContent = stats.failed;
    },

    /**
     * Render jobs table
     */
    renderJobs() {
        const tbody = document.getElementById('jobsTableBody');
        if (!tbody) return;

        const filteredJobs = this.jobStatusFilter === 'all' 
            ? this.currentJobs 
            : this.currentJobs.filter(j => j.status === this.jobStatusFilter);

        tbody.innerHTML = filteredJobs.map(job => `
            <tr>
                <td><code>${job.id}</code></td>
                <td>${Utils.escapeHtml(job.type)}</td>
                <td>
                    <span class="status-badge ${job.status}">${job.status}</span>
                </td>
                <td>
                    <div class="job-progress">
                        <div class="job-progress-bar" style="width: ${job.progress}%"></div>
                    </div>
                    <span style="font-size: 0.875rem;">${job.progress}%</span>
                </td>
                <td>${Utils.formatDate(job.createdAt)}</td>
                <td>
                    <button class="btn-icon" title="View details">
                        <i class="fas fa-eye"></i>
                    </button>
                    ${job.status === 'failed' ? `
                        <button class="btn-icon" title="Retry">
                            <i class="fas fa-redo"></i>
                        </button>
                    ` : ''}
                </td>
            </tr>
        `).join('');
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

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Jobs;
}
