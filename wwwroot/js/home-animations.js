/**
 * AUB Digital Academy - Scroll Animation Engine & Performance Particle Network
 */
document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const revealElements = document.querySelectorAll('.reveal-up, .reveal-fade, .reveal-scale, .reveal-left, .reveal-right');

    if (prefersReducedMotion) {
        document.documentElement.classList.add('reduced-motion');
        revealElements.forEach(el => el.classList.add('settled'));
        return; // Skip motion animations completely when reduced motion is preferred
    }

    // Mark HTML as JS-ready to arm progressive CSS animations
    document.documentElement.classList.add('js-ready');

    // 1. Intersection Observer for Scroll Reveals
    if ('IntersectionObserver' in window) {
        const observerOptions = {
            root: null,
            rootMargin: '0px 0px -40px 0px',
            threshold: 0.08
        };

        const revealObserver = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const el = entry.target;
                    el.classList.add('reveal-active');

                    // Read custom delay if present
                    let delay = 0;
                    const classList = Array.from(el.classList);
                    const delayClass = classList.find(c => c.startsWith('delay-'));
                    if (delayClass) {
                        const parsed = parseInt(delayClass.replace('delay-', ''), 10);
                        if (!isNaN(parsed)) delay = parsed;
                    }

                    // Settle into static layout flow after animation completes
                    setTimeout(() => {
                        el.classList.add('settled');
                    }, delay + 550);

                    observer.unobserve(el);
                }
            });
        }, observerOptions);

        revealElements.forEach(el => revealObserver.observe(el));
    } else {
        // Fallback for older browsers
        revealElements.forEach(el => el.classList.add('settled'));
    }

    // 2. High-Performance Lightweight Particle Network
    const canvas = document.getElementById('heroParticleCanvas');
    const heroSection = document.getElementById('home');

    if (canvas && heroSection) {
        const ctx = canvas.getContext('2d');
        let width = canvas.width = heroSection.clientWidth || window.innerWidth;
        let height = canvas.height = heroSection.clientHeight || window.innerHeight;
        let animationFrameId = null;
        let isHeroVisible = true;

        const updateCanvasSize = () => {
            if (!heroSection) return;
            width = canvas.width = heroSection.clientWidth || window.innerWidth;
            height = canvas.height = heroSection.clientHeight || window.innerHeight;
        };

        window.addEventListener('resize', updateCanvasSize, { passive: true });

        // Node points configuration - kept lean to prevent CPU/battery strain
        const numPoints = width < 768 ? 16 : 30;
        const maxDistance = width < 768 ? 95 : 125;

        const points = Array.from({ length: numPoints }, () => ({
            x: Math.random() * width,
            y: Math.random() * height,
            vx: (Math.random() - 0.5) * 0.35,
            vy: (Math.random() - 0.5) * 0.35,
            radius: Math.random() < 0.2 ? 2.2 : 1.3,
            isGold: Math.random() < 0.25
        }));

        let mouseX = -1000;
        let mouseY = -1000;

        heroSection.addEventListener('mousemove', (e) => {
            const rect = canvas.getBoundingClientRect();
            mouseX = e.clientX - rect.left;
            mouseY = e.clientY - rect.top;
        }, { passive: true });

        heroSection.addEventListener('mouseleave', () => {
            mouseX = -1000;
            mouseY = -1000;
        }, { passive: true });

        function animateParticles() {
            if (!isHeroVisible) return;

            ctx.clearRect(0, 0, width, height);

            // Connect lines
            for (let i = 0; i < points.length; i++) {
                for (let j = i + 1; j < points.length; j++) {
                    const dx = points[i].x - points[j].x;
                    const dy = points[i].y - points[j].y;
                    const dist = Math.sqrt(dx * dx + dy * dy);

                    if (dist < maxDistance) {
                        const alpha = (1 - dist / maxDistance) * 0.16;
                        ctx.strokeStyle = `rgba(79, 156, 249, ${alpha})`;
                        ctx.lineWidth = 0.75;
                        ctx.beginPath();
                        ctx.moveTo(points[i].x, points[i].y);
                        ctx.lineTo(points[j].x, points[j].y);
                        ctx.stroke();
                    }
                }
            }

            // Move and render dots
            for (let p of points) {
                p.x += p.vx;
                p.y += p.vy;

                if (p.x < 0) p.x = width;
                if (p.x > width) p.x = 0;
                if (p.y < 0) p.y = height;
                if (p.y > height) p.y = 0;

                // Subtle cursor repulsion
                const mdx = p.x - mouseX;
                const mdy = p.y - mouseY;
                const mdist = Math.sqrt(mdx * mdx + mdy * mdy);
                if (mdist < 90 && mdist > 0) {
                    p.x += (mdx / mdist) * 0.7;
                    p.y += (mdy / mdist) * 0.7;
                }

                ctx.fillStyle = p.isGold ? 'rgba(245, 184, 0, 0.75)' : 'rgba(79, 156, 249, 0.65)';
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2);
                ctx.fill();
            }

            animationFrameId = requestAnimationFrame(animateParticles);
        }

        // Pause particle network when hero scrolls out of view to preserve battery & CPU
        if ('IntersectionObserver' in window) {
            const heroObserver = new IntersectionObserver((entries) => {
                entries.forEach(entry => {
                    isHeroVisible = entry.isIntersecting;
                    if (isHeroVisible) {
                        if (!animationFrameId) {
                            animationFrameId = requestAnimationFrame(animateParticles);
                        }
                    } else {
                        if (animationFrameId) {
                            cancelAnimationFrame(animationFrameId);
                            animationFrameId = null;
                        }
                    }
                });
            }, { threshold: 0.05 });

            heroObserver.observe(heroSection);
        } else {
            animationFrameId = requestAnimationFrame(animateParticles);
        }
    }
});
