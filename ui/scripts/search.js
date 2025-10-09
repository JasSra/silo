// Search Module

const Search = {
    /**
     * Initialize search module
     */
    init() {
        this.setupSearchForm();
        this.setupQuickSearch();
    },

    /**
     * Setup advanced search form
     */
    setupSearchForm() {
        document.getElementById('executeSearch').addEventListener('click', () => {
            this.performSearch();
        });
        
        document.getElementById('clearSearch').addEventListener('click', () => {
            this.clearSearchForm();
        });
    },

    /**
     * Setup quick search in top bar
     */
    setupQuickSearch() {
        const quickSearch = document.getElementById('quickSearch');
        const debouncedSearch = Utils.debounce(async (query) => {
            if (query.length < 2) return;
            
            try {
                const response = await API.files.search(query, { limit: 10 });
                // Could show dropdown with results
                console.log('Quick search results:', response);
            } catch (error) {
                console.error('Quick search error:', error);
            }
        }, 500);
        
        quickSearch.addEventListener('input', (e) => {
            debouncedSearch(e.target.value);
        });
    },

    /**
     * Perform advanced search
     */
    async performSearch() {
        const filters = {
            query: document.getElementById('searchQuery').value,
            extensions: document.getElementById('searchExtensions').value
                .split(',')
                .map(ext => ext.trim())
                .filter(ext => ext.length > 0),
            dateFrom: document.getElementById('searchDateFrom').value,
            dateTo: document.getElementById('searchDateTo').value,
            minSize: document.getElementById('searchMinSize').value 
                ? parseInt(document.getElementById('searchMinSize').value) 
                : undefined,
            maxSize: document.getElementById('searchMaxSize').value 
                ? parseInt(document.getElementById('searchMaxSize').value) 
                : undefined
        };
        
        // Remove empty filters
        Object.keys(filters).forEach(key => {
            if (!filters[key] || (Array.isArray(filters[key]) && filters[key].length === 0)) {
                delete filters[key];
            }
        });
        
        try {
            Utils.showLoading('Searching...');
            const response = await API.files.advancedSearch(filters);
            
            this.renderSearchResults(response.results || []);
            
            Utils.showToast('Search Complete', 
                `Found ${response.results?.length || 0} files`, 
                'success');
            
        } catch (error) {
            console.error('Search error:', error);
            Utils.handleError(error, 'Search failed');
        } finally {
            Utils.hideLoading();
        }
    },

    /**
     * Render search results
     */
    renderSearchResults(results) {
        const container = document.getElementById('searchResults');
        
        if (results.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <i class="fas fa-search"></i>
                    <h3>No results found</h3>
                    <p>Try adjusting your search criteria</p>
                </div>
            `;
            return;
        }
        
        container.innerHTML = `
            <div class="files-grid">
                ${results.map(file => {
                    const icon = Utils.getFileIcon(file.fileName);
                    const size = Utils.formatFileSize(file.fileSize || 0);
                    const date = Utils.formatDate(file.uploadedAt || file.createdAt);
                    
                    return `
                        <div class="file-card" data-file-id="${file.fileId || file.id}">
                            <div class="file-icon">
                                <i class="fas ${icon}"></i>
                            </div>
                            <div class="file-name" title="${Utils.escapeHtml(file.fileName)}">
                                ${Utils.escapeHtml(file.fileName)}
                            </div>
                            <div class="file-meta">
                                <span>${size}</span>
                                <span>${date}</span>
                            </div>
                        </div>
                    `;
                }).join('')}
            </div>
        `;
        
        // Add click handlers
        container.querySelectorAll('.file-card').forEach((card, index) => {
            card.addEventListener('click', () => {
                if (typeof Files !== 'undefined') {
                    Files.openFile(results[index]);
                }
            });
        });
    },

    /**
     * Clear search form
     */
    clearSearchForm() {
        document.getElementById('searchQuery').value = '';
        document.getElementById('searchExtensions').value = '';
        document.getElementById('searchDateFrom').value = '';
        document.getElementById('searchDateTo').value = '';
        document.getElementById('searchMinSize').value = '';
        document.getElementById('searchMaxSize').value = '';
        
        const container = document.getElementById('searchResults');
        container.innerHTML = `
            <div class="empty-state">
                <i class="fas fa-search"></i>
                <h3>Enter search criteria</h3>
                <p>Use the filters above to find your files</p>
            </div>
        `;
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Search;
}
