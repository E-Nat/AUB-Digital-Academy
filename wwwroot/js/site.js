// ============================================
// AUB Digital Academy - Global JavaScript
// ============================================

document.addEventListener('DOMContentLoaded', function () {
    
    // 1. Sidebar Toggle Logic
    const sidebarToggle = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('sidebar');
    
    if (sidebarToggle && sidebar) {
        // Create overlay element for mobile
        const overlay = document.createElement('div');
        overlay.className = 'sidebar-overlay';
        document.body.appendChild(overlay);

        sidebarToggle.addEventListener('click', function (e) {
            e.preventDefault();
            
            if (window.innerWidth <= 991.98) {
                // Mobile behavior
                sidebar.classList.toggle('show');
                overlay.classList.toggle('show');
            } else {
                // Desktop behavior (optional - if you want collapsible desktop sidebar)
                document.getElementById('wrapper').classList.toggle('toggled');
            }
        });

        // Close sidebar when clicking overlay on mobile
        overlay.addEventListener('click', function () {
            sidebar.classList.remove('show');
            this.classList.remove('show');
        });
    }

    // 1b. Sidebar Scroll Position Persistence (Predictable, No jumping)
    const sidebarNav = document.querySelector('.sidebar-nav');
    if (sidebarNav) {
        try {
            const savedScroll = sessionStorage.getItem('aub_backend_sidebar_scroll');
            if (savedScroll !== null) {
                const scrollVal = parseInt(savedScroll, 10);
                if (!isNaN(scrollVal)) sidebarNav.scrollTop = scrollVal;
            }
        } catch (e) {}

        sidebarNav.addEventListener('scroll', function () {
            try {
                sessionStorage.setItem('aub_backend_sidebar_scroll', String(sidebarNav.scrollTop));
            } catch (e) {}
        }, { passive: true });

        sidebarNav.querySelectorAll('.nav-link-item').forEach(link => {
            link.addEventListener('click', function () {
                try {
                    sessionStorage.setItem('aub_backend_sidebar_scroll', String(sidebarNav.scrollTop));
                } catch (e) {}
            });
        });

        window.addEventListener('beforeunload', function () {
            try {
                sessionStorage.setItem('aub_backend_sidebar_scroll', String(sidebarNav.scrollTop));
            } catch (e) {}
        });
        window.addEventListener('pagehide', function () {
            try {
                sessionStorage.setItem('aub_backend_sidebar_scroll', String(sidebarNav.scrollTop));
            } catch (e) {}
        });
    }

    // 2. Theme Toggle Logic (Dark / Light Mode)
    const themeToggle = document.getElementById('themeToggle');
    const themeIcon = document.getElementById('themeIcon');
    const htmlElement = document.documentElement;

    // Check for saved theme preference or use system preference
    const savedTheme = localStorage.getItem('theme');
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    
    let currentTheme = savedTheme || (prefersDark ? 'dark' : 'light');
    
    // Apply initial theme
    setTheme(currentTheme);

    if (themeToggle) {
        themeToggle.addEventListener('click', function () {
            currentTheme = currentTheme === 'light' ? 'dark' : 'light';
            setTheme(currentTheme);
        });
    }

    function setTheme(theme) {
        htmlElement.setAttribute('data-bs-theme', theme);
        localStorage.setItem('theme', theme);
        
        if (themeIcon) {
            if (theme === 'dark') {
                themeIcon.classList.replace('bi-moon-fill', 'bi-sun-fill');
            } else {
                themeIcon.classList.replace('bi-sun-fill', 'bi-moon-fill');
            }
        }
    }

    // 3. Initialize Bootstrap Tooltips & Popovers
    const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]');
    const tooltipList = [...tooltipTriggerList].map(tooltipTriggerEl => new bootstrap.Tooltip(tooltipTriggerEl));

    const popoverTriggerList = document.querySelectorAll('[data-bs-toggle="popover"]');
    const popoverList = [...popoverTriggerList].map(popoverTriggerEl => new bootstrap.Popover(popoverTriggerEl));

    // 4. Auto-dismiss alerts after 5 seconds
    const alerts = document.querySelectorAll('.alert:not(.alert-permanent)');
    alerts.forEach(function (alert) {
        setTimeout(function () {
            const bsAlert = new bootstrap.Alert(alert);
            bsAlert.close();
        }, 5000);
    });
});
