/**
 * AUB Digital Academy - Production Login Controller
 * Handles credentials verification, show/hide password, UI state, and role dashboard redirection.
 */
document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    const loginForm = document.getElementById('loginForm');
    const btnSignIn = document.getElementById('btnSignIn');
    const btnSignInText = document.getElementById('btnSignInText');
    const btnSignInArrow = document.getElementById('btnSignInArrow');
    const signInSpinner = document.getElementById('signInSpinner');
    const loginErrorAlert = document.getElementById('loginErrorAlert');
    const loginErrorMessage = document.getElementById('loginErrorMessage');
    const closeErrorBtn = document.getElementById('closeErrorBtn');

    const loginIdInput = document.getElementById('LoginId');
    const passwordInput = document.getElementById('Password');
    const togglePasswordBtn = document.getElementById('togglePassword');
    const togglePasswordIcon = togglePasswordBtn ? togglePasswordBtn.querySelector('i') : null;

    const prefersReducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // 1. Password Visibility Show/Hide Toggle
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

    // 2. Dismiss Error Alert Button
    if (closeErrorBtn && loginErrorAlert) {
        closeErrorBtn.addEventListener('click', function () {
            loginErrorAlert.classList.add('d-none');
        });
    }

    // 3. Form Submission with Progressive Enhancement (AJAX with safe fallback)
    if (loginForm) {
        loginForm.addEventListener('submit', async function (e) {
            e.preventDefault();

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

                if (data.success && data.redirectUrl) {
                    // Direct immediate redirect to authorized dashboard
                    window.location.href = data.redirectUrl;
                } else if ((data.requiresVerification || data.requiresLink) && data.redirectUrl) {
                    showError(data.message || 'Redirecting to Telegram security verification...');
                    setTimeout(() => {
                        window.location.href = data.redirectUrl;
                    }, 1000);
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

    // 4. Clear Validation & Error State on User Input
    [loginIdInput, passwordInput].forEach(function (input) {
        if (!input) return;
        input.addEventListener('input', function () {
            this.classList.remove('is-invalid');
            this.removeAttribute('aria-invalid');
            if (loginErrorAlert) loginErrorAlert.classList.add('d-none');
        });
    });

    // 5. Helpers
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
