/**
 * AUB Digital Academy - Public Website Interactive Logic
 */
document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    // 1. Sticky Navigation Scroll State
    const siteHeader = document.getElementById('siteHeader');
    const updateHeaderScroll = () => {
        if (!siteHeader) return;
        if (window.scrollY > 20) {
            siteHeader.classList.add('scrolled');
        } else {
            siteHeader.classList.remove('scrolled');
        }
    };
    window.addEventListener('scroll', updateHeaderScroll, { passive: true });
    updateHeaderScroll();

    // 2. Mobile Drawer Navigation & Accessibility
    const mobileMenuToggle = document.getElementById('mobileMenuToggle');
    const mobileDrawer = document.getElementById('mobileDrawer');
    const mobileDrawerClose = document.getElementById('mobileDrawerClose');
    const mobileBackdrop = document.getElementById('mobileBackdrop');
    const mobileNavLinks = document.querySelectorAll('.mobile-nav-link');

    const openDrawer = () => {
        if (!mobileDrawer) return;
        mobileDrawer.classList.add('active');
        mobileBackdrop?.classList.add('active');
        mobileMenuToggle?.setAttribute('aria-expanded', 'true');
        document.body.style.overflow = 'hidden';
        mobileDrawerClose?.focus();
    };

    const closeDrawer = () => {
        if (!mobileDrawer) return;
        mobileDrawer.classList.remove('active');
        mobileBackdrop?.classList.remove('active');
        mobileMenuToggle?.setAttribute('aria-expanded', 'false');
        document.body.style.overflow = '';
        mobileMenuToggle?.focus();
    };

    mobileMenuToggle?.addEventListener('click', openDrawer);
    mobileDrawerClose?.addEventListener('click', closeDrawer);
    mobileBackdrop?.addEventListener('click', closeDrawer);

    // Close on mobile link click
    mobileNavLinks.forEach(link => {
        link.addEventListener('click', () => {
            closeDrawer();
        });
    });

    // Close drawer on Escape key
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && mobileDrawer?.classList.contains('active')) {
            closeDrawer();
        }
    });

    // 3. Scrollspy - Highlight Active Header Link
    const navLinks = document.querySelectorAll('#navLinksList .nav-link');
    const trackedSections = document.querySelectorAll('section[id]');

    const updateScrollspy = () => {
        if (!trackedSections.length) return;
        const headerHeight = siteHeader?.offsetHeight || 76;
        const scrollPosition = window.scrollY + headerHeight + 60;

        let activeId = '';
        trackedSections.forEach(section => {
            const top = section.offsetTop;
            const height = section.offsetHeight;
            if (scrollPosition >= top && scrollPosition < top + height) {
                activeId = section.getAttribute('id');
            }
        });

        // If at top of the page, default to home
        if (window.scrollY < 120) {
            activeId = 'home';
        }

        if (activeId) {
            navLinks.forEach(link => {
                const href = link.getAttribute('href');
                if (href === `#${activeId}`) {
                    link.classList.add('active');
                    link.setAttribute('aria-current', 'page');
                } else {
                    link.classList.remove('active');
                    link.removeAttribute('aria-current');
                }
            });

            mobileNavLinks.forEach(link => {
                const href = link.getAttribute('href');
                if (href === `#${activeId}`) {
                    link.classList.add('active');
                } else {
                    link.classList.remove('active');
                }
            });
        }
    };

    window.addEventListener('scroll', updateScrollspy, { passive: true });
    updateScrollspy();

    // 4. Program Category Filter Chips
    const filterChips = document.querySelectorAll('.category-filter-wrapper .chip-filter');
    const programCards = document.querySelectorAll('#programsGrid .program-card-col');

    filterChips.forEach(chip => {
        chip.addEventListener('click', function () {
            // Update filter chip UI and ARIA states
            filterChips.forEach(c => {
                c.classList.remove('active');
                c.setAttribute('aria-selected', 'false');
            });
            this.classList.add('active');
            this.setAttribute('aria-selected', 'true');

            const filterValue = this.getAttribute('data-filter');

            // Filter program cards with smooth transitions
            programCards.forEach(card => {
                const cardCategory = card.getAttribute('data-category');
                const isMatch = filterValue === 'all' || cardCategory === filterValue;

                if (isMatch) {
                    card.style.display = '';
                    // Trigger fade-in
                    requestAnimationFrame(() => {
                        card.style.opacity = '1';
                        card.style.transform = 'translateY(0) scale(1)';
                    });
                } else {
                    card.style.opacity = '0';
                    card.style.transform = 'translateY(12px) scale(0.98)';
                    setTimeout(() => {
                        if (chip.classList.contains('active') && chip.getAttribute('data-filter') === filterValue) {
                            card.style.display = 'none';
                        }
                    }, 220);
                }
            });
        });
    });
});
