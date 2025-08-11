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