/**
 * AUB Digital Academy - Login Left Panel Interactive Network Canvas & Motion Handler
 * Implements high-performance, GPU-friendly constellation particles, visibility detection,
 * debounced resize, and full prefers-reduced-motion accessibility compliance.
 */
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', initLeftPanelNetwork);

    function initLeftPanelNetwork() {
        const canvas = document.getElementById('loginNetworkCanvas');
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const prefersReducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        let width = 0;
        let height = 0;
        let dpr = 1;
        let particles = [];
        let animationFrameId = null;
        let isRunning = false;
        let resizeTimer = null;

        // Configuration
        const MAX_NODES = 70;
        const MIN_NODES = 24;
        const AREA_PER_NODE = 14000;
        const CONNECT_DISTANCE = 110;
        const CONNECT_DISTANCE_SQ = CONNECT_DISTANCE * CONNECT_DISTANCE;

        function resizeCanvas() {
            const rect = canvas.getBoundingClientRect();
            dpr = Math.min(window.devicePixelRatio || 1, 2);
            width = rect.width;
            height = rect.height;

            if (width === 0 || height === 0) return;

            canvas.width = Math.floor(width * dpr);
            canvas.height = Math.floor(height * dpr);
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

            initParticles();
            drawFrame();
        }

        function initParticles() {
            const calculatedCount = Math.floor((width * height) / AREA_PER_NODE);
            const count = Math.max(MIN_NODES, Math.min(MAX_NODES, calculatedCount));

            particles = [];
            for (let i = 0; i < count; i++) {
                const isGold = Math.random() < 0.16; // ~16% gold accents, rest cyan/blue
                particles.push({
                    x: Math.random() * width,
                    y: Math.random() * height,
                    vx: (Math.random() - 0.5) * 0.45,
                    vy: (Math.random() - 0.5) * 0.45,
                    radius: isGold ? Math.random() * 1.4 + 1.6 : Math.random() * 1.2 + 1.2,
                    isGold: isGold,
                    baseAlpha: isGold ? 0.8 : Math.random() * 0.4 + 0.35
                });
            }
        }

        function drawFrame() {
            ctx.clearRect(0, 0, width, height);

            const pLen = particles.length;

            // Draw connecting lines
            ctx.lineWidth = 0.85;
            for (let i = 0; i < pLen; i++) {
                const p1 = particles[i];
                for (let j = i + 1; j < pLen; j++) {
                    const p2 = particles[j];
                    const dx = p1.x - p2.x;
                    const dy = p1.y - p2.y;
                    const distSq = dx * dx + dy * dy;

                    if (distSq < CONNECT_DISTANCE_SQ) {
                        const alpha = (1 - Math.sqrt(distSq) / CONNECT_DISTANCE) * 0.28;
                        if (p1.isGold || p2.isGold) {
                            ctx.strokeStyle = `rgba(245, 184, 46, ${alpha * 1.2})`;
                        } else {
                            ctx.strokeStyle = `rgba(56, 189, 248, ${alpha})`;
                        }
                        ctx.beginPath();
                        ctx.moveTo(p1.x, p1.y);
                        ctx.lineTo(p2.x, p2.y);
                        ctx.stroke();
                    }
                }
            }

            // Draw particle nodes
            for (let i = 0; i < pLen; i++) {
                const p = particles[i];
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2);
                if (p.isGold) {
                    ctx.fillStyle = `rgba(245, 184, 46, ${p.baseAlpha})`;
                    ctx.shadowColor = 'rgba(245, 184, 46, 0.6)';
                    ctx.shadowBlur = 6;
                } else {
                    ctx.fillStyle = `rgba(56, 189, 248, ${p.baseAlpha})`;
                    ctx.shadowColor = 'rgba(56, 189, 248, 0.4)';
                    ctx.shadowBlur = 4;
                }
                ctx.fill();
            }

            // Reset shadows for next operations
            ctx.shadowColor = 'transparent';
            ctx.shadowBlur = 0;
        }

        function updateParticles() {
            const pLen = particles.length;
            for (let i = 0; i < pLen; i++) {
                const p = particles[i];
                p.x += p.vx;
                p.y += p.vy;

                // Bounce off boundaries with smooth wrap
                if (p.x < 0) {
                    p.x = 0;
                    p.vx = -p.vx;
                } else if (p.x > width) {
                    p.x = width;
                    p.vx = -p.vx;
                }

                if (p.y < 0) {
                    p.y = 0;
                    p.vy = -p.vy;
                } else if (p.y > height) {
                    p.y = height;
                    p.vy = -p.vy;
                }
            }
        }

        function animate() {
            if (!isRunning) return;
            updateParticles();
            drawFrame();
            animationFrameId = requestAnimationFrame(animate);
        }

        function startLoop() {
            if (prefersReducedMotion) {
                // Static rendering only
                drawFrame();
                return;
            }
            if (!isRunning) {
                isRunning = true;
                animationFrameId = requestAnimationFrame(animate);
            }
        }

        function stopLoop() {
            isRunning = false;
            if (animationFrameId) {
                cancelAnimationFrame(animationFrameId);
                animationFrameId = null;
            }
        }

        // Debounced resize handler
        function handleResize() {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(() => {
                resizeCanvas();
                if (!isRunning && !prefersReducedMotion && document.visibilityState === 'visible') {
                    startLoop();
                }
            }, 150);
        }

        // Visibility handling: Pause loop when tab is hidden to save CPU/battery
        function handleVisibilityChange() {
            if (document.hidden) {
                stopLoop();
            } else if (!prefersReducedMotion) {
                startLoop();
            }
        }

        // Initialize setup
        resizeCanvas();
        startLoop();

        window.addEventListener('resize', handleResize, { passive: true });
        document.addEventListener('visibilitychange', handleVisibilityChange);
    }
})();
