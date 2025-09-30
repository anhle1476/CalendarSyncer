const express = require('express');
const router = express.Router();
const storage = require('../services/storage');
const rabbitmqService = require('../services/rabbitmq');

/**
 * Get service statistics
 * GET /api/stats
 */
router.get('/stats', (req, res) => {
    try {
        const stats = storage.getStatistics();
        const rabbitmqStatus = rabbitmqService.getStatus();
        
        res.json({
            statistics: stats,
            rabbitmq: rabbitmqStatus,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve statistics',
            message: error.message
        });
    }
});

/**
 * Get recent events
 * GET /api/events
 */
router.get('/events', (req, res) => {
    try {
        const limit = parseInt(req.query.limit) || 50;
        const offset = parseInt(req.query.offset) || 0;
        const eventType = req.query.type;
        const calendarId = req.query.calendar;
        
        let events = storage.getEvents(1000, 0).events; // Get more for filtering
        
        // Apply filters
        if (eventType) {
            events = events.filter(event => event.eventType === eventType);
        }
        
        if (calendarId) {
            events = events.filter(event => event.calendarId === calendarId);
        }
        
        // Apply pagination
        const paginatedEvents = events.slice(offset, offset + limit);
        
        res.json({
            events: paginatedEvents,
            total: events.length,
            limit,
            offset,
            filters: {
                eventType: eventType || null,
                calendarId: calendarId || null
            },
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve events',
            message: error.message
        });
    }
});

/**
 * Get specific event by ID
 * GET /api/events/:eventId
 */
router.get('/events/:eventId', (req, res) => {
    try {
        const { eventId } = req.params;
        const event = storage.getEventById(eventId);
        
        if (!event) {
            return res.status(404).json({
                error: 'Event not found',
                eventId
            });
        }
        
        res.json({
            event,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve event',
            message: error.message
        });
    }
});

/**
 * Get webhook channels
 * GET /api/channels
 */
router.get('/channels', (req, res) => {
    try {
        const channels = storage.getWebhookChannels();
        
        res.json({
            channels,
            total: channels.length,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve channels',
            message: error.message
        });
    }
});

/**
 * Get health check history
 * GET /api/health-history
 */
router.get('/health-history', (req, res) => {
    try {
        const limit = parseInt(req.query.limit) || 20;
        const healthChecks = storage.getHealthChecks(limit);
        
        res.json({
            healthChecks,
            total: healthChecks.length,
            limit,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve health history',
            message: error.message
        });
    }
});

/**
 * Test RabbitMQ connection
 * POST /api/test/rabbitmq
 */
router.post('/test/rabbitmq', async (req, res) => {
    try {
        const isHealthy = await rabbitmqService.testConnection();
        const status = rabbitmqService.getStatus();
        
        res.json({
            healthy: isHealthy,
            status,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            healthy: false,
            error: error.message,
            timestamp: new Date().toISOString()
        });
    }
});

/**
 * Send test message to RabbitMQ
 * POST /api/test/message
 */
router.post('/test/message', async (req, res) => {
    if (process.env.NODE_ENV === 'production') {
        return res.status(404).json({ 
            error: 'Test endpoints not available in production' 
        });
    }

    try {
        const testMessage = {
            eventId: 'api_test_' + Date.now(),
            calendarId: req.body.calendarId || 'test@example.com',
            eventType: req.body.eventType || 'created',
            timestamp: new Date().toISOString(),
            resourceId: 'api_test_resource_' + Date.now(),
            resourceUri: 'https://www.googleapis.com/calendar/v3/calendars/test@example.com/events',
            channelId: 'api_test_channel_' + Date.now(),
            resourceState: 'exists'
        };

        // Store in local storage
        const storedEvent = storage.addEvent(testMessage);

        // Publish to RabbitMQ
        await rabbitmqService.publishMessage(testMessage);

        res.json({
            status: 'test_message_sent',
            event: storedEvent,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            status: 'test_failed',
            error: error.message,
            timestamp: new Date().toISOString()
        });
    }
});

/**
 * Get system information
 * GET /api/system
 */
router.get('/system', (req, res) => {
    try {
        const stats = storage.getStatistics();
        
        res.json({
            system: {
                nodeVersion: process.version,
                platform: process.platform,
                arch: process.arch,
                pid: process.pid,
                memory: {
                    used: Math.round(process.memoryUsage().heapUsed / 1024 / 1024),
                    total: Math.round(process.memoryUsage().heapTotal / 1024 / 1024),
                    external: Math.round(process.memoryUsage().external / 1024 / 1024),
                    rss: Math.round(process.memoryUsage().rss / 1024 / 1024)
                },
                uptime: {
                    process: Math.floor(process.uptime()),
                    service: stats.uptime
                }
            },
            environment: {
                nodeEnv: process.env.NODE_ENV || 'development',
                port: process.env.PORT || 3000,
                rabbitmqUrl: process.env.RABBITMQ_URL ? 'configured' : 'not_configured',
                rabbitmqQueue: process.env.RABBITMQ_QUEUE || 'calendar_events'
            },
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve system information',
            message: error.message
        });
    }
});

/**
 * Reset statistics (development only)
 * POST /api/reset
 */
router.post('/reset', (req, res) => {
    if (process.env.NODE_ENV === 'production') {
        return res.status(404).json({ 
            error: 'Reset endpoint not available in production' 
        });
    }

    try {
        storage.reset();
        
        res.json({
            status: 'statistics_reset',
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to reset statistics',
            message: error.message
        });
    }
});

/**
 * Export data (for debugging)
 * GET /api/export
 */
router.get('/export', (req, res) => {
    if (process.env.NODE_ENV === 'production') {
        return res.status(404).json({ 
            error: 'Export endpoint not available in production' 
        });
    }

    try {
        const data = storage.exportData();
        
        res.setHeader('Content-Type', 'application/json');
        res.setHeader('Content-Disposition', 'attachment; filename="webhook-service-data.json"');
        res.send(data);
    } catch (error) {
        res.status(500).json({
            error: 'Failed to export data',
            message: error.message
        });
    }
});

/**
 * Get calendar statistics
 * GET /api/calendars
 */
router.get('/calendars', (req, res) => {
    try {
        const stats = storage.getStatistics();
        const channels = storage.getWebhookChannels();
        
        // Group channels by calendar
        const calendarChannels = {};
        channels.forEach(channel => {
            if (!calendarChannels[channel.calendarId]) {
                calendarChannels[channel.calendarId] = [];
            }
            calendarChannels[channel.calendarId].push(channel);
        });
        
        // Combine with event statistics
        const calendars = Object.keys(stats.eventsByCalendar).map(calendarId => ({
            calendarId,
            eventCount: stats.eventsByCalendar[calendarId],
            channels: calendarChannels[calendarId] || [],
            hasActiveChannel: (calendarChannels[calendarId] || []).length > 0
        }));
        
        res.json({
            calendars,
            total: calendars.length,
            totalEvents: stats.totalEvents,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve calendar statistics',
            message: error.message
        });
    }
});

module.exports = router;