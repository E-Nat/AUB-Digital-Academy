document.addEventListener('DOMContentLoaded', function () {
    const loginForm = document.getElementById('loginForm');
    const googleForm = document.getElementById('googleForm');

    // UI Elements
    const loginView = document.getElementById('loginView');
    const googleLoadingView = document.getElementById('googleLoadingView');
    const authSuccessView = document.getElementById('authSuccessView');
    const roleVerificationView = document.getElementById('roleVerificationView');

    const btnSignIn = document.getElementById('btnSignIn');
    const btnGoogle = document.getElementById('btnGoogle');
    const signInSpinner = document.getElementById('signInSpinner');
    const loginErrorAlert = document.getElementById('loginErrorAlert');
    const loginErrorMessage = document.getElementById('loginErrorMessage');

    const loginIdInput = document.getElementById('LoginId');
    const passwordInput = document.getElementById('Password');
    const togglePasswordBtn = document.getElementById('togglePassword');
    const togglePasswordIcon = togglePasswordBtn ? togglePasswordBtn.querySelector('i') : null;

    // ── Toggle Password Visibility ──────────────────────────────────────────────
    if (togglePasswordBtn && passwordInput) {
        togglePasswordBtn.addEventListener('click', function () {
            const isPassword = passwordInput.getAttribute('type') === 'password';
            passwordInput.setAttribute('type', isPassword ? 'text' : 'password');
            if (togglePasswordIcon) {
                togglePasswordIcon.classList.toggle('bi-eye-slash', !isPassword);
                togglePasswordIcon.classList.toggle('bi-eye', isPassword);
            }
        });
    }

    // ── Google Login ─────────────────────────────────────────────────────────────
    if (btnGoogle && googleForm) {
        btnGoogle.addEventListener('click', function () {
            loginView.classList.add('d-none');
            googleLoadingView.classList.remove('d-none');
            setTimeout(() => { googleForm.submit(); }, 800);
        });
    }

    // ── Standard Login AJAX ──────────────────────────────────────────────────────
    if (loginForm) {
        loginForm.addEventListener('submit', async function (e) {
            e.preventDefault();

            // Client-side empty-field check
            const loginIdVal = loginIdInput ? loginIdInput.value.trim() : '';
            const passwordVal = passwordInput ? passwordInput.value.trim() : '';

            if (!loginIdVal || !passwordVal) {
                showError('Please enter your University ID or Email and Password.');
                if (!loginIdVal && loginIdInput) loginIdInput.classList.add('is-invalid');
                if (!passwordVal && passwordInput) passwordInput.classList.add('is-invalid');
                return;
            }

            // Loading state
            setLoading(true);

            try {
                const formData = new FormData(loginForm);
                const response = await fetch(loginForm.action, {
                    method: 'POST',
                    body: formData,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                // HTTP 400 most likely means a stale AntiForgery token.
                if (response.status === 400) {
                    setLoading(false);
                    showError('Your session has expired. Please refresh the page and try again.');
                    return;
                }

                // Any other non-OK response is a server error.
                if (!response.ok) {
                    throw new Error(`Server error (${response.status}). Please try again.`);
                }

                // Parse JSON — guard against non-JSON bodies on unexpected errors.
                let data;
                try {
                    data = await response.json();
                } catch {
                    throw new Error('Unexpected server response. Please refresh and try again.');
                }

                if (data.success) {
                    // Success screens (4 → 5 → redirect)
                    setLoading(false);
                    loginView.classList.add('d-none');
                    authSuccessView.classList.remove('d-none');

                    setTimeout(() => {
                        authSuccessView.classList.add('d-none');
                        roleVerificationView.classList.remove('d-none');

                        setTimeout(() => {
                            window.location.href = data.redirectUrl;
                        }, 1200);
                    }, 1500);
                } else {
                    // Credentials rejected
                    setLoading(false);
                    showError(data.message || 'Invalid University ID or Password.');
                    if (loginIdInput) loginIdInput.classList.add('is-invalid');
                    if (passwordInput) passwordInput.classList.add('is-invalid');
                }

            } catch (error) {
                console.error('Login error:', error);
                setLoading(false);
                showError(error.message || 'An unexpected error occurred. Please try again.');
            }
        });
    }

    // ── Clear error state on user input ─────────────────────────────────────────
    [loginIdInput, passwordInput].forEach(function (input) {
        if (!input) return;
        input.addEventListener('input', function () {
            this.classList.remove('is-invalid');
            if (loginErrorAlert) loginErrorAlert.classList.add('d-none');
        });
    });

    // ── Helpers ──────────────────────────────────────────────────────────────────
    function setLoading(loading) {
        if (btnSignIn) btnSignIn.disabled = loading;
        if (signInSpinner) {
            signInSpinner.classList.toggle('d-none', !loading);
        }
    }

    function showError(message) {
        if (loginErrorMessage) loginErrorMessage.textContent = message;
        if (loginErrorAlert) {
            loginErrorAlert.classList.remove('d-none');
            loginErrorAlert.classList.add('shake-animation');
            setTimeout(() => loginErrorAlert.classList.remove('shake-animation'), 500);
        }
    }
});
