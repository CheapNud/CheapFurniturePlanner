// Cheap Furniture Planner JavaScript Functions

// Get bounding rectangle of an element
window.getBoundingClientRect = (element) => {
    if (!element) return null;
    return element.getBoundingClientRect();
};

// Download file function for exporting
window.downloadFile = (filename, contentType, content) => {
    // Create a blob with the content
    const blob = new Blob([content], { type: contentType });

    // Create a temporary URL for the blob
    const url = window.URL.createObjectURL(blob);

    // Create a temporary anchor element and trigger download
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.style.display = 'none';

    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    // Clean up the URL
    window.URL.revokeObjectURL(url);
};

// Capture element as image (for exporting room plans as images)
window.captureElementAsImage = async (elementId, filename = 'room-plan.png') => {
    try {
        const element = document.getElementById(elementId);
        if (!element) {
            console.error('Element not found:', elementId);
            return false;
        }

        // Use html2canvas if available (would need to be included)
        if (typeof html2canvas !== 'undefined') {
            const canvas = await html2canvas(element, {
                backgroundColor: '#ffffff',
                scale: 2, // Higher resolution
                useCORS: true,
                allowTaint: true
            });

            // Convert canvas to blob and download
            canvas.toBlob((blob) => {
                const url = window.URL.createObjectURL(blob);
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = filename;
                anchor.click();
                window.URL.revokeObjectURL(url);
            }, 'image/png');

            return true;
        } else {
            console.warn('html2canvas not available. Install html2canvas for image export functionality.');
            return false;
        }
    } catch (error) {
        console.error('Error capturing element as image:', error);
        return false;
    }
};

// Print specific element
window.printElement = (elementId) => {
    try {
        const element = document.getElementById(elementId);
        if (!element) {
            console.error('Element not found:', elementId);
            return false;
        }

        // Create a new window for printing
        const printWindow = window.open('', '_blank');
        if (!printWindow) {
            console.error('Could not open print window');
            return false;
        }

        // Copy styles from parent document
        const styles = Array.from(document.styleSheets)
            .map(styleSheet => {
                try {
                    return Array.from(styleSheet.cssRules)
                        .map(rule => rule.cssText)
                        .join('');
                } catch (e) {
                    // Handle cross-origin stylesheets
                    return '';
                }
            })
            .join('');

        // Create print document
        printWindow.document.write(`
            <!DOCTYPE html>
            <html>
            <head>
                <title>Room Plan - ${document.title}</title>
                <style>
                    ${styles}
                    @media print {
                        body { margin: 0; }
                        .no-print { display: none !important; }
                        .furniture-planner-container .toolbar { display: none !important; }
                        .action-buttons { display: none !important; }
                    }
                </style>
            </head>
            <body>
                ${element.outerHTML}
            </body>
            </html>
        `);

        printWindow.document.close();
        printWindow.focus();

        // Print after a short delay to ensure styles are loaded
        setTimeout(() => {
            printWindow.print();
            printWindow.close();
        }, 250);

        return true;
    } catch (error) {
        console.error('Error printing element:', error);
        return false;
    }
};

// Local storage helpers for user preferences
window.localStorageHelper = {
    setItem: (key, value) => {
        try {
            localStorage.setItem(key, JSON.stringify(value));
            return true;
        } catch (error) {
            console.error('Error setting localStorage item:', error);
            return false;
        }
    },

    getItem: (key, defaultValue = null) => {
        try {
            const item = localStorage.getItem(key);
            return item ? JSON.parse(item) : defaultValue;
        } catch (error) {
            console.error('Error getting localStorage item:', error);
            return defaultValue;
        }
    },

    removeItem: (key) => {
        try {
            localStorage.removeItem(key);
            return true;
        } catch (error) {
            console.error('Error removing localStorage item:', error);
            return false;
        }
    }
};

// File upload helper
window.uploadFileHelper = {
    // Trigger file input dialog
    triggerFileUpload: (inputId, accept = '*') => {
        const input = document.getElementById(inputId);
        if (input) {
            input.accept = accept;
            input.click();
        }
    },

    // Read file as text
    readFileAsText: (file) => {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = (e) => resolve(e.target.result);
            reader.onerror = (e) => reject(e);
            reader.readAsText(file);
        });
    },

    // Read file as data URL (for images)
    readFileAsDataURL: (file) => {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = (e) => resolve(e.target.result);
            reader.onerror = (e) => reject(e);
            reader.readAsDataURL(file);
        });
    }
};

// Dragging utilities for furniture planner
window.dragHelper = {
    // Get mouse/touch position relative to element
    getRelativePosition: (event, element) => {
        const rect = element.getBoundingClientRect();
        const clientX = event.clientX || (event.touches && event.touches[0].clientX) || 0;
        const clientY = event.clientY || (event.touches && event.touches[0].clientY) || 0;

        return {
            x: clientX - rect.left,
            y: clientY - rect.top
        };
    },

    // Snap value to grid
    snapToGrid: (value, gridSize) => {
        return Math.round(value / gridSize) * gridSize;
    },

    // Check if point is within bounds
    isWithinBounds: (x, y, width, height, containerWidth, containerHeight) => {
        return x >= 0 && y >= 0 && x + width <= containerWidth && y + height <= containerHeight;
    }
};

// Client-side drag handler — bypasses SignalR during drag for smooth 60fps movement
window.furnitureDrag = {
    _dotNetRef: null,
    _state: null,
    _boundMouseMove: null,
    _boundMouseUp: null,
    _boundTouchMove: null,
    _boundTouchEnd: null,

    start: function (dotNetRef, config) {
        // config: { primaryId, offsetX, offsetY, boundsLeft, boundsTop,
        //           floorWidth, floorHeight, primaryWidth, primaryHeight,
        //           initialX, initialY, groupMembers: [{id, initialX, initialY, width, height}] }
        this._dotNetRef = dotNetRef;
        this._state = config;
        this._rafId = null;
        this._pendingX = 0;
        this._pendingY = 0;
        // Track accumulated delta for transform-based movement
        this._deltaX = 0;
        this._deltaY = 0;

        // Compute delta clamp limits for group bounds
        if (config.groupMembers && config.groupMembers.length > 0) {
            var minX = config.initialX, minY = config.initialY;
            var maxR = config.initialX + config.primaryWidth;
            var maxB = config.initialY + config.primaryHeight;
            for (var i = 0; i < config.groupMembers.length; i++) {
                var m = config.groupMembers[i];
                minX = Math.min(minX, m.initialX);
                minY = Math.min(minY, m.initialY);
                maxR = Math.max(maxR, m.initialX + m.width);
                maxB = Math.max(maxB, m.initialY + m.height);
            }
            this._state.minDeltaX = -minX;
            this._state.minDeltaY = -minY;
            this._state.maxDeltaX = config.floorWidth - maxR;
            this._state.maxDeltaY = config.floorHeight - maxB;
        }

        // Kill CSS transition and promote to compositor layer BEFORE any transforms
        // This must happen in JS (not via Blazor CSS class) to avoid the race condition
        // where JS applies transforms before Blazor's render batch adds the .dragging class
        var primaryEl = document.getElementById(config.primaryId);
        if (primaryEl) {
            primaryEl.style.transition = 'none';
            primaryEl.style.willChange = 'transform';
        }
        if (config.groupMembers) {
            for (var i = 0; i < config.groupMembers.length; i++) {
                var gel = document.getElementById(config.groupMembers[i].id);
                if (gel) {
                    gel.style.transition = 'none';
                    gel.style.willChange = 'transform';
                }
            }
        }

        this._boundMouseMove = this._onMouseMove.bind(this);
        this._boundMouseUp = this._onMouseUp.bind(this);
        this._boundTouchMove = this._onTouchMove.bind(this);
        this._boundTouchEnd = this._onTouchEnd.bind(this);

        document.addEventListener('mousemove', this._boundMouseMove);
        document.addEventListener('mouseup', this._boundMouseUp);
        document.addEventListener('touchmove', this._boundTouchMove, { passive: false });
        document.addEventListener('touchend', this._boundTouchEnd);
    },

    _applyPosition: function () {
        this._rafId = null;
        var s = this._state;
        if (!s) return;

        var newX = this._pendingX - s.boundsLeft - s.offsetX;
        var newY = this._pendingY - s.boundsTop - s.offsetY;

        if (s.groupMembers && s.groupMembers.length > 0) {
            // Group drag: clamp delta to keep all members in bounds
            var deltaX = newX - s.initialX;
            var deltaY = newY - s.initialY;
            deltaX = Math.max(s.minDeltaX, Math.min(deltaX, s.maxDeltaX));
            deltaY = Math.max(s.minDeltaY, Math.min(deltaY, s.maxDeltaY));

            // Use transform instead of left/top — GPU composited, no layout reflow
            var el = document.getElementById(s.primaryId);
            if (el) el.style.transform = 'translate(' + deltaX + 'px,' + deltaY + 'px)';

            for (var i = 0; i < s.groupMembers.length; i++) {
                var m = s.groupMembers[i];
                var gel = document.getElementById(m.id);
                if (gel) gel.style.transform = 'translate(' + deltaX + 'px,' + deltaY + 'px)';
            }

            this._deltaX = deltaX;
            this._deltaY = deltaY;
        } else {
            // Solo drag: clamp to bounds
            newX = Math.max(0, Math.min(newX, s.floorWidth - s.primaryWidth));
            newY = Math.max(0, Math.min(newY, s.floorHeight - s.primaryHeight));

            var deltaX = newX - s.initialX;
            var deltaY = newY - s.initialY;

            var el = document.getElementById(s.primaryId);
            if (el) el.style.transform = 'translate(' + deltaX + 'px,' + deltaY + 'px)';

            this._deltaX = deltaX;
            this._deltaY = deltaY;
        }
    },

    _scheduleUpdate: function (clientX, clientY) {
        this._pendingX = clientX;
        this._pendingY = clientY;
        // Batch to next animation frame — one paint per vsync, always latest position
        if (!this._rafId) {
            this._rafId = requestAnimationFrame(this._applyPosition.bind(this));
        }
    },

    _onMouseMove: function (e) {
        this._scheduleUpdate(e.clientX, e.clientY);
    },

    _onTouchMove: function (e) {
        if (e.touches.length > 0) {
            e.preventDefault();
            this._scheduleUpdate(e.touches[0].clientX, e.touches[0].clientY);
        }
    },

    _endDrag: function () {
        var s = this._state;
        if (!s) return;

        // Cancel pending rAF
        if (this._rafId) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }

        document.removeEventListener('mousemove', this._boundMouseMove);
        document.removeEventListener('mouseup', this._boundMouseUp);
        document.removeEventListener('touchmove', this._boundTouchMove);
        document.removeEventListener('touchend', this._boundTouchEnd);

        // Compute final position from initial + accumulated delta
        var finalX = s.initialX + this._deltaX;
        var finalY = s.initialY + this._deltaY;

        // Clear inline overrides — Blazor re-render sets final left/top
        var el = document.getElementById(s.primaryId);
        if (el) {
            el.style.transform = '';
            el.style.willChange = '';
            el.style.transition = '';
        }
        if (s.groupMembers) {
            for (var i = 0; i < s.groupMembers.length; i++) {
                var gel = document.getElementById(s.groupMembers[i].id);
                if (gel) {
                    gel.style.transform = '';
                    gel.style.willChange = '';
                    gel.style.transition = '';
                }
            }
        }

        this._state = null;

        // Single SignalR call back to C# with final position
        if (this._dotNetRef) {
            this._dotNetRef.invokeMethodAsync('OnClientDragEnd', finalX, finalY);
        }
    },

    _onMouseUp: function (e) {
        this._endDrag();
    },

    _onTouchEnd: function (e) {
        this._endDrag();
    },

    stop: function () {
        if (this._rafId) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }
        if (this._boundMouseMove) {
            document.removeEventListener('mousemove', this._boundMouseMove);
            document.removeEventListener('mouseup', this._boundMouseUp);
            document.removeEventListener('touchmove', this._boundTouchMove);
            document.removeEventListener('touchend', this._boundTouchEnd);
        }
        // Clear inline overrides on any active elements
        if (this._state) {
            var el = document.getElementById(this._state.primaryId);
            if (el) { el.style.transform = ''; el.style.willChange = ''; el.style.transition = ''; }
            if (this._state.groupMembers) {
                for (var i = 0; i < this._state.groupMembers.length; i++) {
                    var gel = document.getElementById(this._state.groupMembers[i].id);
                    if (gel) { gel.style.transform = ''; gel.style.willChange = ''; gel.style.transition = ''; }
                }
            }
        }
        this._state = null;
        this._dotNetRef = null;
    }
};

// Keyboard shortcuts for planner
window.setupKeyboardShortcuts = () => {
    document.addEventListener('keydown', (event) => {
        // Only process shortcuts when planner is focused
        const plannerElement = document.getElementById('furniture-planner');
        if (!plannerElement || document.activeElement !== plannerElement) {
            return;
        }

        switch (event.key) {
            case 'Delete':
                // Delete selected furniture (handled by Blazor component)
                break;
            case 'r':
            case 'R':
                // Rotate selected furniture (handled by Blazor component)
                break;
            case 'g':
            case 'G':
                // Group/ungroup selected furniture (handled by Blazor component)
                break;
            case 's':
            case 'S':
                if (event.ctrlKey || event.metaKey) {
                    event.preventDefault();
                    // Save room plan (could trigger Blazor method)
                    const saveEvent = new CustomEvent('plannerSave');
                    plannerElement.dispatchEvent(saveEvent);
                }
                break;
            case 'z':
            case 'Z':
                if (event.ctrlKey || event.metaKey) {
                    event.preventDefault();
                    // Undo (future feature)
                    console.log('Undo not yet implemented');
                }
                break;
            case 'y':
            case 'Y':
                if (event.ctrlKey || event.metaKey) {
                    event.preventDefault();
                    // Redo (future feature)
                    console.log('Redo not yet implemented');
                }
                break;
        }
    });
};

// Utility to format file size
window.formatFileSize = (bytes) => {
    if (bytes === 0) return '0 Bytes';

    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));

    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

// Generate unique ID
window.generateUniqueId = () => {
    return Date.now().toString(36) + Math.random().toString(36).substr(2);
};

// Measure text width (useful for dynamic sizing)
window.measureTextWidth = (text, font = '14px Arial') => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');
    context.font = font;
    return context.measureText(text).width;
};

// Initialize planner JavaScript functions when page loads
document.addEventListener('DOMContentLoaded', () => {
    console.log('Cheap Furniture Planner JavaScript initialized');
    window.setupKeyboardShortcuts();
});

// Handle page unload to warn about unsaved changes
window.addEventListener('beforeunload', (event) => {
    // This would need to be coordinated with Blazor component state
    const hasUnsavedChanges = document.body.dataset.hasUnsavedChanges === 'true';

    if (hasUnsavedChanges) {
        event.preventDefault();
        event.returnValue = 'You have unsaved changes. Are you sure you want to leave?';
        return event.returnValue;
    }
});

// Touch device support
window.touchSupport = {
    isTouchDevice: () => {
        return 'ontouchstart' in window || navigator.maxTouchPoints > 0;
    },

    // Convert touch event to mouse-like event
    touchToMouse: (touchEvent) => {
        const touch = touchEvent.touches[0] || touchEvent.changedTouches[0];
        return {
            clientX: touch.clientX,
            clientY: touch.clientY,
            pageX: touch.pageX,
            pageY: touch.pageY
        };
    }
};

// Performance monitoring for complex room plans
window.performanceMonitor = {
    startTime: null,

    start: (operation) => {
        if (window.performance && window.performance.now) {
            window.performanceMonitor.startTime = window.performance.now();
            console.time(operation);
        }
    },

    end: (operation) => {
        if (window.performance && window.performance.now && window.performanceMonitor.startTime) {
            const endTime = window.performance.now();
            const duration = endTime - window.performanceMonitor.startTime;
            console.timeEnd(operation);

            if (duration > 100) { // Log if operation takes more than 100ms
                console.warn(`Performance warning: ${operation} took ${duration.toFixed(2)}ms`);
            }
        }
    }
};

// Color utilities for furniture visualization
window.colorUtils = {
    // Convert hex to RGB
    hexToRgb: (hex) => {
        const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        return result ? {
            r: parseInt(result[1], 16),
            g: parseInt(result[2], 16),
            b: parseInt(result[3], 16)
        } : null;
    },

    // Generate contrasting text color
    getContrastColor: (backgroundColor) => {
        const rgb = window.colorUtils.hexToRgb(backgroundColor);
        if (!rgb) return '#000000';

        const brightness = (rgb.r * 299 + rgb.g * 587 + rgb.b * 114) / 1000;
        return brightness > 128 ? '#000000' : '#ffffff';
    },

    // Generate random color
    randomColor: () => {
        return '#' + Math.floor(Math.random() * 16777215).toString(16).padStart(6, '0');
    }
};