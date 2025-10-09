// Main Application Script

// Wait for DOM to be ready
document.addEventListener('DOMContentLoaded', () => {
    initializeApp();
});

/**
 * Initialize the application
 */
function initializeApp() {
    setupThemeToggle();
    setupNavigation();
    setupLogout();
    
    // Initialize authentication module
    if (typeof Auth !== 'undefined') {
        Auth.init();
    }
}

/**
 * Setup theme toggle functionality
 */
function setupThemeToggle() {
    const themeToggle = document.getElementById('themeToggle');
    const themeToggleMain = document.getElementById('themeToggleMain');
    
    // Check saved theme preference
    const savedTheme = Utils.storage.get('theme', 'dark');
    applyTheme(savedTheme);
    
    const toggleTheme = () => {
        const currentTheme = document.body.classList.contains('dark-theme') ? 'dark' : 'light';
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        applyTheme(newTheme);
        Utils.storage.set('theme', newTheme);
    };
    
    if (themeToggle) {
        themeToggle.addEventListener('click', toggleTheme);
    }
    
    if (themeToggleMain) {
        themeToggleMain.addEventListener('click', toggleTheme);
    }
}

/**
 * Apply theme to document
 */
function applyTheme(theme) {
    if (theme === 'dark') {
        document.body.classList.add('dark-theme');
        updateThemeIcons('fa-sun');
    } else {
        document.body.classList.remove('dark-theme');
        updateThemeIcons('fa-moon');
    }
}

/**
 * Update theme toggle icons
 */
function updateThemeIcons(iconClass) {
    const themeToggle = document.getElementById('themeToggle');
    const themeToggleMain = document.getElementById('themeToggleMain');
    
    if (themeToggle) {
        const icon = themeToggle.querySelector('i');
        if (icon) {
            icon.className = `fas ${iconClass}`;
        }
    }
    
    if (themeToggleMain) {
        const icon = themeToggleMain.querySelector('i');
        if (icon) {
            icon.className = `fas ${iconClass}`;
        }
    }
}

/**
 * Setup navigation between views
 */
function setupNavigation() {
    // Sidebar navigation
    document.querySelectorAll('.nav-item').forEach(item => {
        item.addEventListener('click', (e) => {
            e.preventDefault();
            const viewName = item.dataset.view;
            switchView(viewName);
            
            // Update active nav item
            document.querySelectorAll('.nav-item').forEach(nav => {
                nav.classList.remove('active');
            });
            item.classList.add('active');
        });
    });
}

/**
 * Switch between different views
 */
function switchView(viewName) {
    // Hide all views
    document.querySelectorAll('.view').forEach(view => {
        view.classList.remove('active');
    });
    
    // Show selected view
    const targetView = document.getElementById(`${viewName}View`);
    if (targetView) {
        targetView.classList.add('active');
    }
    
    // Load view-specific data
    switch (viewName) {
        case 'files':
            if (typeof Files !== 'undefined' && Files.loadFiles) {
                Files.loadFiles();
            }
            break;
        case 'analytics':
            if (typeof Analytics !== 'undefined' && Analytics.loadAnalytics) {
                Analytics.loadAnalytics();
            }
            break;
    }
}

/**
 * Setup logout functionality
 */
function setupLogout() {
    const logoutBtn = document.getElementById('logoutBtn');
    if (logoutBtn) {
        logoutBtn.addEventListener('click', () => {
            if (confirm('Are you sure you want to logout?')) {
                if (typeof Auth !== 'undefined') {
                    Auth.logout();
                }
            }
        });
    }
}

/**
 * Handle global errors
 */
window.addEventListener('error', (event) => {
    console.error('Global error:', event.error);
    // Don't show toast for every error, just log it
});

/**
 * Handle unhandled promise rejections
 */
window.addEventListener('unhandledrejection', (event) => {
    console.error('Unhandled promise rejection:', event.reason);
    // Don't show toast for every rejection, just log it
});

// Make switchView globally available for inline onclick handlers
window.switchView = switchView;
