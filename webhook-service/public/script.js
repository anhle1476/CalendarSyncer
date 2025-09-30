// Dashboard JavaScript for Calendar Webhook Service
class WebhookDashboard {
    constructor() {
        this.refreshInterval = null;
        this.refreshRate = 30000; // 30 seconds
        this.isLoading = false;
        this.lastUpdate = null;
        
        this.init();
    }

    init() {
        this.bindEvents();
        this.loadInitialData();
        this.startAutoRefresh();
    }

    bindEvents() {
        // Refresh button
        const refreshBtn = document.getElementById('refreshBtn');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.refreshData());
        }

        // Filter controls
        const eventTypeFilter = document.getElementById('eventTypeFilter');
        const calendarFilter = document.getElementById('calendarFilter');
        
        if (eventTypeFilter) {
            eventTypeFilter.addEventListener('change', () => this.filterEvents());
        }
        
        if (calendarFilter) {
            calendarFilter.addEventListener('change', () => this.filterEvents());
        }

        // Modal close
        const errorModalClose = document.getElementById('errorModalClose');
        if (errorModalClose) {
            errorModalClose.addEventListener('click', () => this.hideModal('errorModal'));
        }

        // Click outside modal to close
        const errorModal = document.getElementById('errorModal');
        if (errorModal) {
            errorModal.addEventListener('click', (e) => {
                if (e.target === errorModal) {
                    this.hideModal('errorModal');
                }
            });
        }

        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if (e.key === 'r' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                this.refreshData();
            }
            if (e.key === 'Escape') {
                this.hideModal('errorModal');
            }
        });
    }

    async loadInitialData() {
        this.showLoading();
        try {
            await Promise.all([
                this.loadServiceStatus(),
                this.loadEvents(),
                this.loadSystemInfo()
            ]);
            this.updateLastRefreshTime();
        } catch (error) {
            this.showError('Failed to load initial data: ' + error.message);
        } finally {
            this.hideLoading();
        }
    }

    async refreshData() {
        if (this.isLoading) return;
        
        const refreshBtn = document.getElementById('refreshBtn');
        if (refreshBtn) {
            refreshBtn.classList.add('loading');
            const icon = refreshBtn.querySelector('i');
            if (icon) {
                icon.classList.add('fa-spin');
            }
        }

        try {
            await this.loadInitialData();
        } finally {
            if (refreshBtn) {
                refreshBtn.classList.remove('loading');
                const icon = refreshBtn.querySelector('i');
                if (icon) {
                    icon.classList.remove('fa-spin');
                }
            }
        }
    }

    async loadStatistics() {
        try {
            const response = await fetch('/api/stats');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            const data = await response.json();
            this.updateStatistics(data);
        } catch (error) {
            console.error('Failed to load statistics:', error);
            this.updateStatistics({
                totalEvents: 0,
                eventsByType: { created: 0, updated: 0, deleted: 0, sync: 0 }
            });
        }
    }

    updateStatistics(data) {
        const totalEvents = document.getElementById('totalEvents');
        const createdEvents = document.getElementById('createdEvents');
        const updatedEvents = document.getElementById('updatedEvents');
        const deletedEvents = document.getElementById('deletedEvents');

        if (totalEvents) totalEvents.textContent = data.totalEvents || 0;
        if (createdEvents) createdEvents.textContent = data.eventsByType?.created || 0;
        if (updatedEvents) updatedEvents.textContent = data.eventsByType?.updated || 0;
        if (deletedEvents) deletedEvents.textContent = data.eventsByType?.deleted || 0;
    }

    async loadServiceStatus() {
        try {
            const response = await fetch('/health/detailed');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            const data = await response.json();
            this.updateServiceStatus(data);
        } catch (error) {
            console.error('Failed to load service status:', error);
            this.updateServiceStatus({ status: 'error', services: {} });
        }
    }

    updateServiceStatus(data) {
        // Update main status indicator
        const statusIndicator = document.getElementById('statusIndicator');
        const statusDot = statusIndicator?.querySelector('.status-dot');
        const statusText = statusIndicator?.querySelector('.status-text');

        if (statusDot && statusText) {
            statusDot.className = 'status-dot';
            if (data.status === 'healthy') {
                statusDot.classList.add('online');
                statusText.textContent = 'Online';
            } else if (data.status === 'degraded') {
                statusDot.classList.add('warning');
                statusText.textContent = 'Warning';
            } else {
                statusDot.classList.add('offline');
                statusText.textContent = 'Offline';
            }
        }

        // Update webhook service status
        const webhookStatus = document.getElementById('webhookStatus');
        if (webhookStatus) {
            const badge = webhookStatus.querySelector('.status-badge');
            const uptime = document.getElementById('webhookUptime');
            const channels = document.getElementById('activeChannels');

            if (badge) {
                badge.className = 'status-badge';
                badge.textContent = data.status === 'healthy' ? 'Healthy' : 
                                  data.status === 'degraded' ? 'Warning' : 'Error';
                badge.classList.add(data.status === 'healthy' ? 'healthy' : 
                                  data.status === 'degraded' ? 'warning' : 'error');
            }

            // Simply show "Healthy" status instead of uptime
            if (uptime) uptime.textContent = 'Healthy';
            
            // Get active channels from storage service
            const activeChannelsCount = data.services?.storage?.webhookChannels?.length || 0;
            if (channels) channels.textContent = activeChannelsCount;
        }

        // Update RabbitMQ status
        const rabbitmqStatus = document.getElementById('rabbitmqStatus');
        if (rabbitmqStatus) {
            const badge = rabbitmqStatus.querySelector('.status-badge');
            const connected = document.getElementById('rabbitmqConnected');
            const queue = document.getElementById('rabbitmqQueue');

            // Map backend status to frontend status
            const rmqBackendStatus = data.services?.rabbitmq?.status || 'unknown';
            const rmqConnected = data.services?.rabbitmq?.connected || false;
            
            let rmqStatus = 'disconnected';
            if (rmqBackendStatus === 'up' && rmqConnected) {
                rmqStatus = 'connected';
            } else if (rmqBackendStatus === 'up' && !rmqConnected) {
                rmqStatus = 'connecting';
            }
            
            if (badge) {
                badge.className = 'status-badge';
                badge.textContent = rmqStatus === 'connected' ? 'Connected' : 
                                  rmqStatus === 'connecting' ? 'Connecting' : 'Disconnected';
                badge.classList.add(rmqStatus === 'connected' ? 'healthy' : 
                                  rmqStatus === 'connecting' ? 'warning' : 'error');
            }

            if (connected) connected.textContent = rmqConnected ? 'Yes' : 'No';
            if (queue) queue.textContent = data.services?.rabbitmq?.config?.queue || 'calendar-events';
        }
    }

    async loadEvents() {
        try {
            const response = await fetch('/api/events?limit=50');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            const data = await response.json();
            this.updateEventsTable(data.events || []);
            this.updateCalendarFilter(data.calendars || []);
        } catch (error) {
            console.error('Failed to load events:', error);
            this.updateEventsTable([]);
        }
    }

    updateEventsTable(events) {
        const tableBody = document.getElementById('eventsTableBody');
        if (!tableBody) return;

        if (events.length === 0) {
            tableBody.innerHTML = '<tr><td colspan="7" class="no-data">No events found</td></tr>';
            return;
        }

        tableBody.innerHTML = events.map(event => `
            <tr>
                <td>${this.formatDateTime(event.timestamp || event.receivedAt)}</td>
                <td><span class="event-type ${event.eventType}">${event.eventType}</span></td>
                <td>${this.truncateText(event.calendarId, 25)}</td>
                <td>${this.truncateText(event.resourceId || 'N/A', 20)}</td>
                <td>${this.truncateText(event.channelId || 'N/A', 20)}</td>
                <td>${event.messageNumber || 'N/A'}</td>
                <td><span class="resource-state ${event.resourceState}">${event.resourceState || 'unknown'}</span></td>
            </tr>
        `).join('');
    }

    updateCalendarFilter(calendars) {
        const calendarFilter = document.getElementById('calendarFilter');
        if (!calendarFilter) return;

        // Keep the "All Calendars" option
        const currentValue = calendarFilter.value;
        calendarFilter.innerHTML = '<option value="">All Calendars</option>';
        
        calendars.forEach(calendar => {
            const option = document.createElement('option');
            option.value = calendar;
            option.textContent = this.truncateText(calendar, 30);
            calendarFilter.appendChild(option);
        });

        // Restore previous selection if it still exists
        if (currentValue && calendars.includes(currentValue)) {
            calendarFilter.value = currentValue;
        }
    }

    async loadChannels() {
        try {
            const response = await fetch('/api/channels');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            const data = await response.json();
            this.updateChannelsGrid(data.channels || []);
        } catch (error) {
            console.error('Failed to load channels:', error);
            this.updateChannelsGrid([]);
        }
    }

    updateChannelsGrid(channels) {
        const channelsGrid = document.getElementById('channelsGrid');
        if (!channelsGrid) return;

        if (channels.length === 0) {
            channelsGrid.innerHTML = '<div class="no-data">No active webhook channels</div>';
            return;
        }

        channelsGrid.innerHTML = channels.map(channel => `
            <div class="channel-card">
                <div class="channel-header">
                    <div class="channel-id">${this.truncateText(channel.id, 25)}</div>
                    <span class="channel-status ${channel.active ? 'active' : 'inactive'}">
                        ${channel.active ? 'Active' : 'Inactive'}
                    </span>
                </div>
                <div class="channel-details">
                    <p><strong>Calendar:</strong> ${this.truncateText(channel.calendarId, 30)}</p>
                    <p><strong>Created:</strong> ${this.formatDateTime(channel.createdAt)}</p>
                    <p><strong>Last Activity:</strong> ${channel.lastActivity ? this.formatDateTime(channel.lastActivity) : 'Never'}</p>
                    <p><strong>Events:</strong> ${channel.eventCount || 0}</p>
                </div>
            </div>
        `).join('');
    }

    async loadSystemInfo() {
        try {
            const response = await fetch('/api/system');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            const data = await response.json();
            this.updateSystemInfo(data);
        } catch (error) {
            console.error('Failed to load system info:', error);
            this.updateSystemInfo({});
        }
    }

    updateSystemInfo(data) {
        const nodeVersion = document.getElementById('nodeVersion');
        const nodeEnv = document.getElementById('nodeEnv');
        const platform = document.getElementById('platform');
        const memoryUsed = document.getElementById('memoryUsed');
        const memoryTotal = document.getElementById('memoryTotal');
        const memoryRss = document.getElementById('memoryRss');

        if (nodeVersion) nodeVersion.textContent = data.system?.nodeVersion || process.version || 'Unknown';
        if (nodeEnv) nodeEnv.textContent = data.configuration?.environment || 'Unknown';
        if (platform) platform.textContent = data.system?.platform || 'Unknown';
        
        if (data.system?.memory) {
            if (memoryUsed) memoryUsed.textContent = data.system.memory.used || '-';
            if (memoryTotal) memoryTotal.textContent = data.system.memory.total || '-';
            if (memoryRss) memoryRss.textContent = data.system.memory.external || '-';
        }
    }

    async filterEvents() {
        const typeFilter = document.getElementById('eventTypeFilter')?.value;
        const calendarFilter = document.getElementById('calendarFilter')?.value;

        const params = new URLSearchParams();
        if (typeFilter) params.append('type', typeFilter);
        if (calendarFilter) params.append('calendar', calendarFilter);
        params.append('limit', '50');

        try {
            const response = await fetch(`/api/events?${params}`);
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            const data = await response.json();
            this.updateEventsTable(data.events || []);
        } catch (error) {
            console.error('Failed to filter events:', error);
            this.showError('Failed to filter events: ' + error.message);
        }
    }

    startAutoRefresh() {
        this.refreshInterval = setInterval(() => {
            if (!this.isLoading) {
                this.loadServiceStatus();
            }
        }, this.refreshRate);
    }

    stopAutoRefresh() {
        if (this.refreshInterval) {
            clearInterval(this.refreshInterval);
            this.refreshInterval = null;
        }
    }

    showLoading() {
        this.isLoading = true;
        const overlay = document.getElementById('loadingOverlay');
        if (overlay) {
            overlay.classList.add('show');
        }
    }

    hideLoading() {
        this.isLoading = false;
        const overlay = document.getElementById('loadingOverlay');
        if (overlay) {
            overlay.classList.remove('show');
        }
    }

    showError(message) {
        const modal = document.getElementById('errorModal');
        const messageEl = document.getElementById('errorMessage');
        
        if (modal && messageEl) {
            messageEl.textContent = message;
            modal.classList.add('show');
        }
    }

    hideModal(modalId) {
        const modal = document.getElementById(modalId);
        if (modal) {
            modal.classList.remove('show');
        }
    }

    updateLastRefreshTime() {
        this.lastUpdate = new Date();
        const lastUpdated = document.getElementById('lastUpdated');
        if (lastUpdated) {
            lastUpdated.textContent = this.formatDateTime(this.lastUpdate);
        }
    }

    formatDateTime(timestamp) {
        if (!timestamp) return 'N/A';
        
        const date = new Date(timestamp);
        if (isNaN(date.getTime())) return 'Invalid Date';
        
        return date.toLocaleString('en-US', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
        });
    }

    formatUptime(seconds) {
        if (!seconds || seconds < 0) return '0s';
        
        const days = Math.floor(seconds / 86400);
        const hours = Math.floor((seconds % 86400) / 3600);
        const minutes = Math.floor((seconds % 3600) / 60);
        const secs = Math.floor(seconds % 60);

        if (days > 0) {
            return `${days}d ${hours}h ${minutes}m`;
        } else if (hours > 0) {
            return `${hours}h ${minutes}m ${secs}s`;
        } else if (minutes > 0) {
            return `${minutes}m ${secs}s`;
        } else {
            return `${secs}s`;
        }
    }

    truncateText(text, maxLength) {
        if (!text) return 'N/A';
        if (text.length <= maxLength) return text;
        return text.substring(0, maxLength - 3) + '...';
    }

    // Cleanup when page is unloaded
    destroy() {
        this.stopAutoRefresh();
    }
}

// Initialize dashboard when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    window.dashboard = new WebhookDashboard();
});

// Cleanup on page unload
window.addEventListener('beforeunload', () => {
    if (window.dashboard) {
        window.dashboard.destroy();
    }
});

// Handle visibility change to pause/resume auto-refresh
document.addEventListener('visibilitychange', () => {
    if (window.dashboard) {
        if (document.hidden) {
            window.dashboard.stopAutoRefresh();
        } else {
            window.dashboard.startAutoRefresh();
            // Refresh data when page becomes visible again
            setTimeout(() => {
                if (!window.dashboard.isLoading) {
                    window.dashboard.refreshData();
                }
            }, 1000);
        }
    }
});

// Export for potential external use
if (typeof module !== 'undefined' && module.exports) {
    module.exports = WebhookDashboard;
}