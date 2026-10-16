document.addEventListener('DOMContentLoaded', function () {
    const loginForm = document.getElementById('loginForm');
    const googleForm = document.getElementById('googleForm');

    // UI View Containers
    const loginView = document.getElementById('loginView');
    const googleLoadingView = document.getElementById('googleLoadingView');
    const authSuccessView = document.getElementById('authSuccessView');

    // Action & Status Elements
    const btnSignIn = document.getElementById('btnSignIn');
    const btnSignInText = document.getElementById('btnSignInText');
    const btnSignInArrow = document.getElementById('btnSignInArrow');
    const btnGoogle = document.getElementById('btnGoogle');
    const signInSpinner = document.getElementById('signInSpinner');
    const loginErrorAlert = document.getElementById('loginErrorAlert');
    const loginErrorMessage = document.getElementById('loginErrorMessage');
    const closeErrorBtn = document.getElementById('closeErrorBtn');

    // Input Controls
    const loginIdInput = document.getElementById('LoginId');
    const passwordInput = document.getElementById('Password');
    const togglePasswordBtn = document.getElementById('togglePassword');
    const togglePasswordIcon = togglePasswordBtn ? togglePasswordBtn.querySelector('i') : null;

    const prefersReducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ── 1. Toggle Password Visibility ─────────────────────────────────────────
    if (togglePasswordBtn && passwordInput) {
        togglePasswordBtn.addEventListener('click', function (e) {
            e.preventDefault();
            const isPassword = passwordInput.getAttribute('type') === 'password';
            passwordInput.setAttribute('type', isPassword ? 'text' : 'password');
            
            this.setAttribute('aria-pressed', isPassword ? 'true' : 'false');
            this.setAttribute('aria-label', isPassword ? 'Hide password' : 'Show password');
            this.setAttribute('title', isPassword ? 'Hide password' : 'Show password');

            if (togglePasswordIcon) {
                togglePasswordIcon.classList.toggle('bi-eye-slash', !isPassword);
                togglePasswordIcon.classList.toggle('bi-eye', isPassword);
            }
            passwordInput.focus();
        });
    }

    // ── 2. Role Selector Tabs & Keyboard Navigation ───────────────────────────
    const roleTabs = document.getElementById('roleTabs');
    const loginIdLabel = document.getElementById('loginIdLabel');

    if (roleTabs && loginIdLabel && loginIdInput) {
        const tabs = Array.from(roleTabs.querySelectorAll('.auth-role-tab'));

        function activateTab(tabBtn) {
            tabs.forEach(t => {
                t.classList.remove('active');
                t.setAttribute('aria-selected', 'false');
                t.setAttribute('tabindex', '-1');
            });

            tabBtn.classList.add('active');
            tabBtn.setAttribute('aria-selected', 'true');
            tabBtn.setAttribute('tabindex', '0');

            const role = tabBtn.getAttribute('data-role');
            if (role === 'STUDENT') {
                loginIdLabel.textContent = 'Student ID or University Email';
                loginIdInput.setAttribute('placeholder', 'e.g. STU001 or student@aub.edu.kh');
            } else if (role === 'TEACHER') {
                loginIdLabel.textContent = 'Faculty ID or University Email';
                loginIdInput.setAttribute('placeholder', 'e.g. TCH001 or teacher@aub.edu.kh');
            } else if (role === 'ADMIN') {
                loginIdLabel.textContent = 'Administrator Email or Staff ID';
                loginIdInput.setAttribute('placeholder', 'e.g. ADMIN001 or admin@aub.edu.kh');
            }
        }

        roleTabs.addEventListener('click', function (e) {
            const tabBtn = e.target.closest('.auth-role-tab');
            if (tabBtn) {
                activateTab(tabBtn);
            }
        });

        // Accessible Keyboard Arrow Navigation (Left / Right / Home / End)
        roleTabs.addEventListener('keydown', function (e) {
            const currentTab = document.activeElement.closest('.auth-role-tab');
            if (!currentTab) return;

            const currentIndex = tabs.indexOf(currentTab);
            let nextIndex = -1;

            if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
                e.preventDefault();
                nextIndex = (currentIndex + 1) % tabs.length;
            } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
                e.preventDefault();
                nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
            } else if (e.key === 'Home') {
                e.preventDefault();
                nextIndex = 0;
            } else if (e.key === 'End') {
                e.preventDefault();
                nextIndex = tabs.length - 1;
            }

            if (nextIndex !== -1) {
                tabs[nextIndex].focus();
                activateTab(tabs[nextIndex]);
            }
        });
    }

    // ── 3. Google OAuth Flow ──────────────────────────────────────────────────
    if (btnGoogle && googleForm) {
        btnGoogle.addEventListener('click', function () {
            if (loginView) loginView.classList.add('d-none');
            if (googleLoadingView) googleLoadingView.classList.remove('d-none');
            setTimeout(() => { googleForm.submit(); }, prefersReducedMotion ? 100 : 500);
        });
    }

    // ── 4. Dismiss Error Alert Button ─────────────────────────────────────────
    if (closeErrorBtn && loginErrorAlert) {
        closeErrorBtn.addEventListener('click', function () {
            loginErrorAlert.classList.add('d-none');
        });
    }

    // ── 5. Standard Login AJAX ────────────────────────────────────────────────
    if (loginForm) {
        loginForm.addEventListener('submit', async function (e) {
            e.preventDefault();

            // Client-side validation check
            const loginIdVal = loginIdInput ? loginIdInput.value.trim() : '';
            const passwordVal = passwordInput ? passwordInput.value : '';

            if (!loginIdVal || !passwordVal) {
                showError('Please enter your University ID/Email and password.');
                if (!loginIdVal && loginIdInput) {
                    loginIdInput.classList.add('is-invalid');
                    loginIdInput.setAttribute('aria-invalid', 'true');
                    loginIdInput.focus();
                } else if (!passwordVal && passwordInput) {
                    passwordInput.classList.add('is-invalid');
                    passwordInput.setAttribute('aria-invalid', 'true');
                    passwordInput.focus();
                }
                return;
            }

            // Set loading state
            setLoading(true);

            try {
                const formData = new FormData(loginForm);
                const response = await fetch(loginForm.action, {
                    method: 'POST',
                    body: formData,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                if (response.status === 400) {
                    setLoading(false);
                    showError('Your security session has expired. Please refresh the page.');
                    return;
                }

                if (!response.ok) {
                    throw new Error(`Server connection error (${response.status}). Please try again.`);
                }

                let data;
                try {
                    data = await response.json();
                } catch {
                    throw new Error('Unexpected server response format. Please refresh the page.');
                }

                if (data.success) {
                    setLoading(false);
                    if (prefersReducedMotion) {
                        window.location.href = data.redirectUrl;
                        return;
                    }

                    // Smooth transition to Success screen
                    if (loginView) loginView.classList.add('d-none');
                    if (authSuccessView) authSuccessView.classList.remove('d-none');

                    setTimeout(() => {
                        window.location.href = data.redirectUrl;
                    }, 500);
                } else {
                    setLoading(false);
                    showError(data.message || 'Invalid University ID/Email or password.');
                    if (loginIdInput) {
                        loginIdInput.classList.add('is-invalid');
                        loginIdInput.setAttribute('aria-invalid', 'true');
                    }
                    if (passwordInput) {
                        passwordInput.classList.add('is-invalid');
                        passwordInput.setAttribute('aria-invalid', 'true');
                    }
                }
            } catch (error) {
                console.error('Authentication error:', error);
                setLoading(false);
                showError(error.message || 'An unexpected error occurred. Please try again.');
            }
        });
    }

    // ── 6. Clear Validation & Error State on User Input ──────────────────────
    [loginIdInput, passwordInput].forEach(function (input) {
        if (!input) return;
        input.addEventListener('input', function () {
            this.classList.remove('is-invalid');
            this.removeAttribute('aria-invalid');
            if (loginErrorAlert) loginErrorAlert.classList.add('d-none');
        });
    });

    // ── 7. Helper Functions ───────────────────────────────────────────────────
    function setLoading(isLoading) {
        if (btnSignIn) {
            btnSignIn.disabled = isLoading;
            btnSignIn.setAttribute('aria-busy', isLoading ? 'true' : 'false');
        }
        if (signInSpinner) {
            signInSpinner.classList.toggle('d-none', !isLoading);
        }
        if (btnSignInArrow) {
            btnSignInArrow.classList.toggle('d-none', isLoading);
        }
        if (btnSignInText) {
            btnSignInText.textContent = isLoading ? 'Signing in…' : 'Sign In to Portal';
        }
    }

    function showError(message) {
        if (loginErrorMessage) loginErrorMessage.textContent = message;
        if (loginErrorAlert) {
            loginErrorAlert.classList.remove('d-none');
            if (!prefersReducedMotion) {
                loginErrorAlert.classList.add('shake-animation');
                setTimeout(() => loginErrorAlert.classList.remove('shake-animation'), 500);
            }
        }
    }
});
