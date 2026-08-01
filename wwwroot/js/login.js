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
    const togglePasswordIcon = togglePasswordBtn?.querySelector('i');

    // Toggle Password Visibility
    if (togglePasswordBtn && passwordInput) {
        togglePasswordBtn.addEventListener('click', function () {
            const type = passwordInput.getAttribute('type') === 'password' ? 'text' : 'password';
            passwordInput.setAttribute('type', type);
            
            if (type === 'text') {
                togglePasswordIcon.classList.remove('bi-eye-slash');
                togglePasswordIcon.classList.add('bi-eye');
            } else {
                togglePasswordIcon.classList.remove('bi-eye');
                togglePasswordIcon.classList.add('bi-eye-slash');
            }
        });
    }

    // Google Login Logic (Screen 3)
    if (btnGoogle && googleForm) {
        btnGoogle.addEventListener('click', function () {
            // Hide login, show Google loading
            loginView.classList.add('d-none');
            googleLoadingView.classList.remove('d-none');
            
            // Submit the form after a slight delay for UI effect
            setTimeout(() => {
                googleForm.submit();
            }, 800);
        });
    }

    // Standard Login AJAX (Screens 1, 2, 4, 5)
    if (loginForm) {
        loginForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            
            // Basic client-side validation
            if (!loginIdInput.value.trim() || !passwordInput.value.trim()) {
                showError("Please enter your University ID and Password.");
                if (!loginIdInput.value.trim()) loginIdInput.classList.add('is-invalid');
                if (!passwordInput.value.trim()) passwordInput.classList.add('is-invalid');
                return;
            }

            // Start Loading state
            btnSignIn.disabled = true;
            signInSpinner.classList.remove('d-none');
            loginErrorAlert.classList.add('d-none');
            loginIdInput.classList.remove('is-invalid');
            passwordInput.classList.remove('is-invalid');

            try {
                const formData = new FormData(loginForm);
                const response = await fetch(loginForm.action, {
                    method: 'POST',
                    body: formData,
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest'
                    }
                });

                const data = await response.json();

                if (data.success) {
                    // Success Flow (Screen 4 & 5)
                    btnSignIn.disabled = false;
                    signInSpinner.classList.add('d-none');
                    
                    // Hide Login Form, Show Success Checkmark
                    loginView.classList.add('d-none');
                    authSuccessView.classList.remove('d-none');
                    
                    // After 1.5s, switch to Role Verification (Screen 5)
                    setTimeout(() => {
                        authSuccessView.classList.add('d-none');
                        roleVerificationView.classList.remove('d-none');
                        
                        // After another 1.2s, do the actual redirect (Screen 6)
                        setTimeout(() => {
                            window.location.href = data.redirectUrl;
                        }, 1200);
                    }, 1500);
                } else {
                    // Error Flow (Screen 2)
                    btnSignIn.disabled = false;
                    signInSpinner.classList.add('d-none');
                    showError(data.message || "Invalid University ID or Password");
                    
                    // Highlight inputs in red
                    loginIdInput.classList.add('is-invalid');
                    passwordInput.classList.add('is-invalid');
                }
            } catch (error) {
                console.error("Login Error:", error);
                btnSignIn.disabled = false;
                signInSpinner.classList.add('d-none');
                showError("An unexpected error occurred. Please try again.");
            }
        });
    }

    // Remove is-invalid class on input
    [loginIdInput, passwordInput].forEach(input => {
        if (input) {
            input.addEventListener('input', function() {
                this.classList.remove('is-invalid');
            });
        }
    });

    function showError(message) {
        loginErrorMessage.textContent = message;
        loginErrorAlert.classList.remove('d-none');
        // Add a subtle shake animation
        loginErrorAlert.classList.add('shake-animation');
        setTimeout(() => loginErrorAlert.classList.remove('shake-animation'), 500);
    }
});
