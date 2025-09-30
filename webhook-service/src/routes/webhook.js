const express = require('express');
const router = express.Router();
const rabbitmqService = require('../services/rabbitmq');
const storage = require('../services/storage');
const validator = require('../services/validator');

/**
 * Google Calendar Webhook Endpoint
 * POST /webhook/calendar
 */
router.post('/calendar', async (req, res) => {
    const startTime = Date.now();
    
    try {
        // Log only x-goog headers for debugging
        const googHeaders = {};
        Object.keys(req.headers).forEach(key => {
            if (key.toLowerCase().startsWith('x-goog-')) {
                googHeaders[key] = req.headers[key];
            }
        });
        
        console.log('Webhook received:', {
            googHeaders: googHeaders,
            timestamp: new Date().toISOString()
        });

        // Process webhook request
        const result = validator.processWebhookRequest(req.headers, req.body);
        
        if (!result.success) {
            console.error('Webhook validation failed:', result.error);
            return res.status(400).json({
                error: 'Invalid webhook request',
                message: result.error,
                details: result.details
            });
        }

        const { eventMessage, headers } = result;

        // Handle sync messages (initial channel setup)
        if (validator.isSyncMessage(req.headers)) {
            console.log('Sync message received for channel:', headers['x-goog-channel-id']);
            
            // Store webhook channel info
            storage.addWebhookChannel(headers['x-goog-channel-id'], {
                calendarId: eventMessage.calendarId,
                resourceUri: headers['x-goog-resource-uri'],
                resourceId: headers['x-goog-resource-id'],
                token: headers['x-goog-channel-token'],
                expiration: headers['x-goog-channel-expiration']
            });

            return res.status(200).json({
                status: 'sync_acknowledged',
                channelId: headers['x-goog-channel-id'],
                timestamp: new Date().toISOString()
            });
        }

        // Update channel activity
        storage.updateChannelActivity(headers['x-goog-channel-id']);

        // Store event in local storage
        const storedEvent = storage.addEvent(eventMessage);

        // Publish to RabbitMQ
        try {
            await rabbitmqService.publishMessage(eventMessage);
            console.log('Event published to RabbitMQ successfully');
        } catch (rabbitmqError) {
            console.error('Failed to publish to RabbitMQ:', rabbitmqError.message);
            
            // Still return success to Google to avoid retries
            // The Windows Service should have fallback polling
            return res.status(200).json({
                status: 'received_with_warning',
                message: 'Event received but failed to publish to queue',
                eventId: storedEvent.id,
                timestamp: new Date().toISOString(),
                warning: 'RabbitMQ publish failed - fallback polling will handle this'
            });
        }

        const responseTime = Date.now() - startTime;

        // Success response
        res.status(200).json({
            status: 'success',
            eventId: storedEvent.id,
            eventType: eventMessage.eventType,
            calendarId: eventMessage.calendarId,
            channelId: headers['x-goog-channel-id'],
            timestamp: new Date().toISOString(),
            responseTime: `${responseTime}ms`
        });

    } catch (error) {
        console.error('Webhook processing error:', error);
        
        const responseTime = Date.now() - startTime;
        
        res.status(500).json({
            status: 'error',
            error: 'Internal server error',
            message: process.env.NODE_ENV === 'development' ? error.message : 'Something went wrong',
            timestamp: new Date().toISOString(),
            responseTime: `${responseTime}ms`
        });
    }
});

/**
 * Webhook verification endpoint (for testing)
 * GET /webhook/calendar
 */
router.get('/calendar', (req, res) => {
    res.json({
        status: 'webhook_endpoint_active',
        endpoint: '/webhook/calendar',
        methods: ['POST'],
        description: 'Google Calendar Webhook Endpoint',
        timestamp: new Date().toISOString(),
        configuration: {
            rabbitmqConnected: rabbitmqService.getStatus().connected
        }
    });
});

/**
 * Test webhook endpoint (for development)
 * POST /webhook/test
 */
router.post('/test', async (req, res) => {
    if (process.env.NODE_ENV === 'production') {
        return res.status(404).json({ error: 'Test endpoint not available in production' });
    }

    try {
        const testEvent = {
            eventId: 'test_' + Date.now(),
            calendarId: req.body.calendarId || 'test@example.com',
            eventType: req.body.eventType || 'created',
            timestamp: new Date().toISOString(),
            resourceId: 'test_resource_' + Date.now(),
            resourceUri: 'https://www.googleapis.com/calendar/v3/calendars/test@example.com/events',
            channelId: 'test_channel_' + Date.now(),
            channelToken: 'test_token',
            resourceState: 'exists'
        };

        // Store event
        const storedEvent = storage.addEvent(testEvent);

        // Try to publish to RabbitMQ
        let rabbitmqStatus = 'success';
        try {
            await rabbitmqService.publishMessage(testEvent);
        } catch (error) {
            rabbitmqStatus = 'failed: ' + error.message;
        }

        res.json({
            status: 'test_event_processed',
            event: storedEvent,
            rabbitmqStatus,
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
 * Webhook statistics
 * GET /webhook/stats
 */
router.get('/stats', (req, res) => {
    try {
        const stats = storage.getStatistics();
        const webhookChannels = storage.getWebhookChannels();
        const rabbitmqStatus = rabbitmqService.getStatus();

        res.json({
            webhook: {
                totalEvents: stats.totalEvents,
                eventsByType: stats.eventsByType,
                eventsByCalendar: stats.eventsByCalendar,
                lastEventTime: stats.lastEventTime,
                activeChannels: webhookChannels.length,
                channels: webhookChannels
            },
            rabbitmq: rabbitmqStatus,
            uptime: stats.uptime,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve webhook statistics',
            message: error.message,
            timestamp: new Date().toISOString()
        });
    }
});

/**
 * Webhook channel management
 * GET /webhook/channels
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
            error: 'Failed to retrieve webhook channels',
            message: error.message
        });
    }
});

/**
 * Remove webhook channel
 * DELETE /webhook/channels/:channelId
 */
router.delete('/channels/:channelId', (req, res) => {
    try {
        const { channelId } = req.params;
        const removed = storage.removeWebhookChannel(channelId);
        
        if (removed) {
            res.json({
                status: 'channel_removed',
                channelId,
                timestamp: new Date().toISOString()
            });
        } else {
            res.status(404).json({
                error: 'Channel not found',
                channelId
            });
        }
    } catch (error) {
        res.status(500).json({
            error: 'Failed to remove webhook channel',
            message: error.message
        });
    }
});

module.exports = router;