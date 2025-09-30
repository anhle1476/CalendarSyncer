class StorageService {
    constructor() {
        this.data = {
            events: [],
            statistics: {
                totalEvents: 0,
                eventsByType: {
                    created: 0,
                    updated: 0,
                    deleted: 0
                },
                eventsByCalendar: {},
                lastEventTime: null,
                uptime: Date.now()
            },
            webhookChannels: new Map(),
            healthChecks: []
        };
        
        this.maxEvents = 1000; // Keep last 1000 events
        this.maxHealthChecks = 100; // Keep last 100 health checks
    }

    initialize() {
        console.log('Storage service initialized');
        
        // Clean up old data periodically
        setInterval(() => {
            this.cleanup();
        }, 60000); // Every minute
    }

    // Event management
    addEvent(event) {
        // Generate a unique ID for the webhook event since Google doesn't provide eventId
        const eventWithId = {
            ...event,
            id: require('uuid').v4()
        };

        // Add to events array
        this.data.events.unshift(eventWithId);
        
        // Trim to max size
        if (this.data.events.length > this.maxEvents) {
            this.data.events = this.data.events.slice(0, this.maxEvents);
        }

        // Update statistics
        this.updateStatistics(eventWithId);

        console.log('Optimized webhook event stored:', {
            id: eventWithId.id,
            eventType: eventWithId.eventType,
            calendarId: eventWithId.calendarId,
            channelId: eventWithId.channelId
        });

        return eventWithId;
    }

    getEvents(limit = 50, offset = 0) {
        return {
            events: this.data.events.slice(offset, offset + limit),
            total: this.data.events.length,
            limit,
            offset
        };
    }

    getEventById(eventId) {
        return this.data.events.find(event => event.eventId === eventId);
    }

    // Statistics management
    updateStatistics(event) {
        this.data.statistics.totalEvents++;
        this.data.statistics.lastEventTime = event.receivedAt;

        // Update by type
        if (event.eventType && this.data.statistics.eventsByType.hasOwnProperty(event.eventType)) {
            this.data.statistics.eventsByType[event.eventType]++;
        }

        // Update by calendar
        if (event.calendarId) {
            if (!this.data.statistics.eventsByCalendar[event.calendarId]) {
                this.data.statistics.eventsByCalendar[event.calendarId] = 0;
            }
            this.data.statistics.eventsByCalendar[event.calendarId]++;
        }
    }

    getStatistics() {
        const now = Date.now();
        const uptimeMs = now - this.data.statistics.uptime;
        
        return {
            ...this.data.statistics,
            uptime: {
                milliseconds: uptimeMs,
                seconds: Math.floor(uptimeMs / 1000),
                minutes: Math.floor(uptimeMs / 60000),
                hours: Math.floor(uptimeMs / 3600000),
                formatted: this.formatUptime(uptimeMs)
            },
            recentEvents: this.data.events.slice(0, 10)
        };
    }

    // Webhook channel management
    addWebhookChannel(channelId, channelInfo) {
        this.data.webhookChannels.set(channelId, {
            ...channelInfo,
            createdAt: new Date().toISOString(),
            lastActivity: new Date().toISOString()
        });
    }

    updateChannelActivity(channelId) {
        const channel = this.data.webhookChannels.get(channelId);
        if (channel) {
            channel.lastActivity = new Date().toISOString();
        }
    }

    getWebhookChannels() {
        return Array.from(this.data.webhookChannels.entries()).map(([id, info]) => ({
            channelId: id,
            ...info
        }));
    }

    removeWebhookChannel(channelId) {
        return this.data.webhookChannels.delete(channelId);
    }

    // Health check management
    addHealthCheck(status, details = {}) {
        const healthCheck = {
            timestamp: new Date().toISOString(),
            status,
            details
        };

        this.data.healthChecks.unshift(healthCheck);
        
        // Trim to max size
        if (this.data.healthChecks.length > this.maxHealthChecks) {
            this.data.healthChecks = this.data.healthChecks.slice(0, this.maxHealthChecks);
        }

        return healthCheck;
    }

    getHealthChecks(limit = 20) {
        return this.data.healthChecks.slice(0, limit);
    }

    getLatestHealthCheck() {
        return this.data.healthChecks[0] || null;
    }

    // Utility methods
    formatUptime(uptimeMs) {
        const hours = Math.floor(uptimeMs / 3600000);
        const minutes = Math.floor((uptimeMs % 3600000) / 60000);
        const seconds = Math.floor((uptimeMs % 60000) / 1000);
        
        if (hours > 0) {
            return `${hours}h ${minutes}m ${seconds}s`;
        } else if (minutes > 0) {
            return `${minutes}m ${seconds}s`;
        } else {
            return `${seconds}s`;
        }
    }

    cleanup() {
        const now = Date.now();
        const oneHourAgo = now - (60 * 60 * 1000);

        // Clean up old health checks
        this.data.healthChecks = this.data.healthChecks.filter(check => {
            return new Date(check.timestamp).getTime() > oneHourAgo;
        });

        // Clean up inactive webhook channels (older than 24 hours)
        const oneDayAgo = now - (24 * 60 * 60 * 1000);
        for (const [channelId, info] of this.data.webhookChannels.entries()) {
            if (new Date(info.lastActivity).getTime() < oneDayAgo) {
                this.data.webhookChannels.delete(channelId);
                console.log(`Cleaned up inactive webhook channel: ${channelId}`);
            }
        }
    }

    // Export/Import for debugging
    exportData() {
        return JSON.stringify(this.data, null, 2);
    }

    importData(jsonData) {
        try {
            const imported = JSON.parse(jsonData);
            this.data = { ...this.data, ...imported };
            console.log('Data imported successfully');
            return true;
        } catch (error) {
            console.error('Failed to import data:', error.message);
            return false;
        }
    }

    // Reset data
    reset() {
        const uptime = this.data.statistics.uptime;
        this.data = {
            events: [],
            statistics: {
                totalEvents: 0,
                eventsByType: {
                    created: 0,
                    updated: 0,
                    deleted: 0
                },
                eventsByCalendar: {},
                lastEventTime: null,
                uptime: uptime // Preserve uptime
            },
            webhookChannels: new Map(),
            healthChecks: []
        };
        console.log('Storage data reset');
    }
}

module.exports = new StorageService();