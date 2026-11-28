// ============================================
// AUB Digital Academy - Global Presentation JS
// ============================================

document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    // 1. Sidebar Toggle & Collapse Logic
    const sidebarToggle = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('sidebar') || document.querySelector('.admin-sidebar');
    const wrapper = document.getElementById('wrapper');
    const sidebarOverlay = document.getElementById('sidebarOverlay');

    // Restore desktop collapsed preference if saved
    const isDesktop = window.innerWidth >= 992;
    try {
        const savedCollapsed = localStorage.getItem('aub_sidebar_collapsed');
        if (isDesktop && savedCollapsed === 'true') {
            sidebar?.classList.add('collapsed');
            wrapper?.classList.add('sidebar-collapsed');
        }
    } catch (e) {}

    if (sidebarToggle && sidebar) {
        sidebarToggle.addEventListener('click', function (e) {
            e.preventDefault();
            if (window.innerWidth < 992) {
                // Mobile offcanvas drawer
                sidebar.classList.toggle('show');
                sidebarOverlay?.classList.toggle('show');
            } else {
                // Desktop icon-collapse toggle
                sidebar.classList.toggle('collapsed');
                wrapper?.classList.toggle('sidebar-collapsed');
                const isNowCollapsed = sidebar.classList.contains('collapsed');
                try {
                    localStorage.setItem('aub_sidebar_collapsed', isNowCollapsed ? 'true' : 'false');
                } catch (e) {}
            }
        });
    }

    if (sidebarOverlay && sidebar) {
        sidebarOverlay.addEventListener('click', function () {
            sidebar.classList.remove('show');
            sidebarOverlay.classList.remove('show');
        });
    }

    // Close mobile sidebar on navigation (supports both admin and default sidebars)
    document.querySelectorAll('.sidebar-nav .nav-link-item, .admin-sidebar .admin-nav-item').forEach(link => {
        link.addEventListener('click', function () {
            if (window.innerWidth < 992 && sidebar) {
                sidebar.classList.remove('show');
                sidebarOverlay?.classList.remove('show');
            }
        });
    });

    // 2. Active Sidebar Navigation Handler based on current route
    initBackendSidebarActiveState();

    // 3. Theme Toggle Logic (Dark / Light Mode)
    const themeToggle = document.getElementById('themeToggle');
    const themeIcon = document.getElementById('themeIcon');
    const htmlElement = document.documentElement;

    // Resolve preference: localStorage > system preference > default light
    const savedTheme = localStorage.getItem('theme');
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    let currentTheme = savedTheme || (prefersDark ? 'dark' : 'light');

    setTheme(currentTheme);

    if (themeToggle) {
        themeToggle.addEventListener('click', function () {
            currentTheme = currentTheme === 'light' ? 'dark' : 'light';
            setTheme(currentTheme);
        });
    }

    // Listen to system OS theme changes if user has not explicitly set a choice
    if (window.matchMedia) {
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
            if (!localStorage.getItem('theme')) {
                setTheme(e.matches ? 'dark' : 'light');
            }
        });
    }

    function setTheme(theme) {
        htmlElement.setAttribute('data-bs-theme', theme);
        try {
            localStorage.setItem('theme', theme);
        } catch (e) {}

        if (themeIcon) {
            if (theme === 'dark') {
                themeIcon.className = 'bi bi-sun-fill text-warning';
            } else {
                themeIcon.className = 'bi bi-moon-stars-fill text-secondary';
            }
        }
    }

    // 4. Global Search Shortcut (Ctrl+K or Cmd+K) & Enter submission
    const searchInput = document.getElementById('globalSearchInput');
    if (searchInput) {
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
                e.preventDefault();
                searchInput.focus();
                searchInput.select();
            }
        });

        searchInput.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                searchInput.blur();
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const query = searchInput.value.trim();
                if (query) {
                    window.location.href = '/Admin/Users?search=' + encodeURIComponent(query);
                }
            }
        });
    }

    // 5. Initialize Bootstrap Tooltips & Popovers
    if (typeof bootstrap !== 'undefined') {
        const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]');
        [...tooltipTriggerList].forEach(el => new bootstrap.Tooltip(el));

        const popoverTriggerList = document.querySelectorAll('[data-bs-toggle="popover"]');
        [...popoverTriggerList].forEach(el => new bootstrap.Popover(el));
    }

    // 6. Auto-dismiss alerts after 5 seconds
    const alerts = document.querySelectorAll('.alert:not(.alert-permanent)');
    alerts.forEach(function (alert) {
        setTimeout(function () {
            if (typeof bootstrap !== 'undefined' && bootstrap.Alert) {
                const bsAlert = bootstrap.Alert.getInstance(alert) || new bootstrap.Alert(alert);
                bsAlert.close();
            } else {
                alert.style.display = 'none';
            }
        }, 5000);
    });
});

// Active Sidebar Navigation Sync
function initBackendSidebarActiveState() {
    // Only apply client-side route match if server-side didn't already select an active item
    const adminActive = document.querySelector('.admin-sidebar .admin-nav-item.active');
    if (adminActive) return; // Server-side Razor helper IsActiveRoute already made the accurate match!

    const navLinks = document.querySelectorAll('.sidebar-nav .nav-link-item');
    if (!navLinks.length) return;

    function syncActiveNav() {
        const currentPath = window.location.pathname.toLowerCase();
        let matched = false;

        navLinks.forEach(link => {
            const href = (link.getAttribute('href') || '').toLowerCase();
            let isMatch = false;

            if (href === currentPath) {
                isMatch = true;
            } else if (href && href !== '/' && !href.endsWith('/dashboard') && currentPath.startsWith(href)) {
                isMatch = true;
            } else if ((currentPath === '/' || currentPath.endsWith('/dashboard')) && href.endsWith('/dashboard')) {
                isMatch = true;
            }

            if (isMatch && !matched) {
                link.classList.add('active');
                matched = true;
            } else {
                link.classList.remove('active');
            }
        });
    }

    syncActiveNav();
}

// Phase 5C: In-App Notification Center Topbar Script
(function () {
    const btn = document.getElementById('notificationBtn');
    const badge = document.getElementById('notificationBadge');
    const container = document.getElementById('notificationListContainer');
    const markAllBtn = document.getElementById('markAllReadBtn');
    if (!btn || !badge) return;

    // 1. Fetch unread count on page load
    async function updateUnreadCount() {
        try {
            const res = await fetch('/api/Notification/UnreadCount');
            if (!res.ok) return;
            const data = await res.json();
            if (data.success && data.unreadCount > 0) {
                badge.textContent = data.unreadCount > 99 ? '99+' : data.unreadCount;
                badge.style.display = 'inline-block';
            } else {
                badge.style.display = 'none';
            }
        } catch (e) {
            // Silently fail if not authenticated
        }
    }

    updateUnreadCount();

    // 2. Fetch notifications when dropdown opens
    btn.addEventListener('show.bs.dropdown', async function () {
        if (!container) return;
        try {
            const res = await fetch('/api/Notification/List?take=10');
            if (!res.ok) return;
            const data = await res.json();
            if (data.success && data.data && data.data.notifications && data.data.notifications.length > 0) {
                container.replaceChildren();
                data.data.notifications.forEach(n => {
                    const item = document.createElement('a');
                    item.href = n.targetUrl || '/Notifications';
                    item.className = `dropdown-item p-3 border-bottom text-wrap d-flex align-items-start gap-2.5 ${n.isRead ? 'opacity-75' : 'bg-primary-subtle bg-opacity-10 fw-semibold'}`;
                    item.dataset.notifId = n.id;

                    item.innerHTML = `
                        <div class="flex-shrink-0 mt-0.5">
                            <i class="bi ${n.iconClass} fs-5"></i>
                        </div>
                        <div class="flex-grow-1" style="min-width: 0;">
                            <div class="d-flex align-items-center justify-content-between gap-1 mb-0.5">
                                <span class="badge ${n.badgeBgClass} rounded-pill" style="font-size: 10px;">${n.typeDisplay}</span>
                                <small class="text-muted" style="font-size: 11px;">${n.relativeTime}</small>
                            </div>
                            <div class="small text-dark text-truncate fw-bold mb-0.5">${n.title}</div>
                            <div class="text-secondary small text-truncate" style="font-size: 12px;">${n.message}</div>
                        </div>
                    `;

                    item.addEventListener('click', async function () {
                        if (!n.isRead) {
                            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
                            const body = new URLSearchParams({ id: n.id });
                            fetch('/api/Notification/MarkAsRead', {
                                method: 'POST',
                                headers: {
                                    'Content-Type': 'application/x-www-form-urlencoded',
                                    'RequestVerificationToken': token
                                },
                                body: body.toString()
                            });
                        }
                    });

                    container.appendChild(item);
                });
            } else {
                container.innerHTML = `
                    <div class="p-4 text-center text-muted">
                        <i class="bi bi-bell-slash fs-3 d-block mb-2 text-secondary opacity-50"></i>
                        <div class="fw-semibold small text-dark-emphasis">No new notifications</div>
                        <div class="text-secondary small mt-1" style="font-size: 12px;">You are completely caught up on system notices.</div>
                    </div>`;
            }
        } catch (e) {
            console.error('Error fetching notifications:', e);
        }
    });

    // 3. Mark all as read
    if (markAllBtn) {
        markAllBtn.addEventListener('click', async function (e) {
            e.stopPropagation();
            try {
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
                const res = await fetch('/api/Notification/MarkAllAsRead', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'RequestVerificationToken': token
                    }
                });
                if (res.ok) {
                    badge.style.display = 'none';
                    document.querySelectorAll('#notificationListContainer .dropdown-item').forEach(el => {
                        el.classList.remove('bg-primary-subtle', 'bg-opacity-10', 'fw-semibold');
                        el.classList.add('opacity-75');
                    });
                }
            } catch (err) {
                console.error('Error marking all as read:', err);
            }
        });
    }
})();
