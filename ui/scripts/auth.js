// Authentication Module

const Auth = {
    /**
     * Initialize authentication
     */
    init() {
        this.setupAuthTabs();
        this.setupLoginForm();
        this.setupSignupForm();
        this.setupPasswordToggles();
        this.setupPasswordStrength();
        this.checkAuthState();
    },

    /**
     * Setup auth tab switching
     */
    setupAuthTabs() {
        const tabs = document.querySelectorAll('.auth-tab');
        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                const tabName = tab.dataset.tab;
                
                // Update active tab
                tabs.forEach(t => t.classList.remove('active'));
                tab.classList.add('active');
                
                // Show corresponding form
                document.querySelectorAll('.auth-form').forEach(form => {
                    form.classList.remove('active');
                });
                document.getElementById(`${tabName}Form`).classList.add('active');
            });
        });
    },

    /**
     * Setup login form
     */
    setupLoginForm() {
        const form = document.getElementById('loginForm');
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            
            const email = document.getElementById('loginEmail').value;
            const password = document.getElementById('loginPassword').value;
            const rememberMe = document.getElementById('rememberMe').checked;

            // Clear previous errors
            this.clearErrors('login');

            // Validate
            if (!Utils.isValidEmail(email)) {
                this.showError('loginEmail', 'Please enter a valid email address');
                return;
            }

            if (password.length < 8) {
                this.showError('loginPassword', 'Password must be at least 8 characters');
                return;
            }

            try {
                Utils.showLoading('Logging in...');
                const response = await API.auth.login(email, password);
                
                // Store tokens
                Utils.storage.set('accessToken', response.accessToken);
                Utils.storage.set('refreshToken', response.refreshToken);
                Utils.storage.set('user', response.user || { username: email });
                
                if (rememberMe) {
                    Utils.storage.set('rememberMe', true);
                }

                Utils.showToast('Success', 'Login successful!', 'success');
                
                // Switch to dashboard
                this.showDashboard();
            } catch (error) {
                console.error('Login error:', error);
                Utils.showToast('Login Failed', 
                    error.response?.data?.message || 'Invalid credentials. Please try again.', 
                    'error');
            } finally {
                Utils.hideLoading();
            }
        });
    },

    /**
     * Setup signup form
     */
    setupSignupForm() {
        const form = document.getElementById('signupForm');
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            
            const email = document.getElementById('signupEmail').value;
            const username = document.getElementById('signupUsername').value;
            const password = document.getElementById('signupPassword').value;
            const confirmPassword = document.getElementById('signupConfirmPassword').value;
            const agreeTerms = document.getElementById('agreeTerms').checked;

            // Clear previous errors
            this.clearErrors('signup');

            // Validate
            if (!Utils.isValidEmail(email)) {
                this.showError('signupEmail', 'Please enter a valid email address');
                return;
            }

            if (username.length < 3) {
                this.showError('signupUsername', 'Username must be at least 3 characters');
                return;
            }

            if (password.length < 8) {
                this.showError('signupPassword', 'Password must be at least 8 characters');
                return;
            }

            if (password !== confirmPassword) {
                this.showError('signupConfirm', 'Passwords do not match');
                return;
            }

            if (!agreeTerms) {
                Utils.showToast('Terms Required', 'You must agree to the Terms of Service', 'warning');
                return;
            }

            try {
                Utils.showLoading('Creating account...');
                const response = await API.auth.signup(username, email, password);
                
                Utils.showToast('Account Created', 
                    'Your account has been created successfully. Please log in.', 
                    'success');
                
                // Switch to login tab
                document.querySelector('.auth-tab[data-tab="login"]').click();
                
                // Pre-fill login email
                document.getElementById('loginEmail').value = email;
            } catch (error) {
                console.error('Signup error:', error);
                Utils.showToast('Signup Failed', 
                    error.response?.data?.message || 'Unable to create account. Please try again.', 
                    'error');
            } finally {
                Utils.hideLoading();
            }
        });
    },

    /**
     * Setup password visibility toggles
     */
    setupPasswordToggles() {
        document.querySelectorAll('.toggle-password').forEach(button => {
            button.addEventListener('click', () => {
                const targetId = button.dataset.target;
                const input = document.getElementById(targetId);
                const icon = button.querySelector('i');

                if (input.type === 'password') {
                    input.type = 'text';
                    icon.classList.remove('fa-eye');
                    icon.classList.add('fa-eye-slash');
                } else {
                    input.type = 'password';
                    icon.classList.remove('fa-eye-slash');
                    icon.classList.add('fa-eye');
                }
            });
        });
    },

    /**
     * Setup password strength indicator
     */
    setupPasswordStrength() {
        const passwordInput = document.getElementById('signupPassword');
        const strengthBar = document.getElementById('passwordStrengthBar');
        const strengthText = document.getElementById('passwordStrengthText');

        passwordInput.addEventListener('input', () => {
            const password = passwordInput.value;
            if (password.length === 0) {
                strengthBar.className = 'strength-bar';
                strengthText.textContent = '';
                return;
            }

            const strength = Utils.getPasswordStrength(password);
            strengthBar.className = `strength-bar ${strength}`;
            strengthText.textContent = `Password strength: ${strength}`;
        });
    },

    /**
     * Check if user is already authenticated
     */
    checkAuthState() {
        const token = Utils.storage.get('accessToken');
        if (token) {
            // Verify token is still valid
            API.tenant.getQuota()
                .then(() => {
                    this.showDashboard();
                })
                .catch(() => {
                    // Token invalid, stay on login
                    Utils.storage.clear();
                });
        }
    },

    /**
     * Show error message for a field
     */
    showError(fieldName, message) {
        const errorElement = document.getElementById(`${fieldName}Error`);
        if (errorElement) {
            errorElement.textContent = message;
        }
    },

    /**
     * Clear all errors for a form
     */
    clearErrors(formPrefix) {
        document.querySelectorAll(`#${formPrefix}Form .error-message`).forEach(el => {
            el.textContent = '';
        });
    },

    /**
     * Show dashboard after successful login
     */
    showDashboard() {
        const loginScreen = document.getElementById('loginScreen');
        const mainDashboard = document.getElementById('mainDashboard');
        
        loginScreen.classList.add('hidden');
        mainDashboard.classList.remove('hidden');

        // Load user data
        const user = Utils.storage.get('user');
        if (user) {
            document.getElementById('userName').textContent = user.username || user.email || 'User';
        }

        // Initialize other modules
        if (typeof Files !== 'undefined') Files.init();
        if (typeof Upload !== 'undefined') Upload.init();
        if (typeof Search !== 'undefined') Search.init();
        if (typeof Analytics !== 'undefined') Analytics.init();
        if (typeof Buckets !== 'undefined') Buckets.init();
        if (typeof Jobs !== 'undefined') Jobs.init();
        if (typeof Audit !== 'undefined') Audit.init();
        if (typeof System !== 'undefined') System.init();
    },

    /**
     * Logout user
     */
    async logout() {
        try {
            Utils.showLoading('Logging out...');
            await API.auth.logout();
        } catch (error) {
            console.error('Logout error:', error);
        } finally {
            Utils.hideLoading();
            
            // Clear local data
            Utils.storage.clear();
            
            // Show login screen
            const loginScreen = document.getElementById('loginScreen');
            const mainDashboard = document.getElementById('mainDashboard');
            
            mainDashboard.classList.add('hidden');
            loginScreen.classList.remove('hidden');
            
            Utils.showToast('Logged Out', 'You have been logged out successfully', 'info');
        }
    }
};

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Auth;
}
