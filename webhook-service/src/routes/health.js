const express = require('express');
const router = express.Router();
const rabbitmqService = require('../services/rabbitmq');
const storage = require('../services/storage');

/**
 * Basic health check endpoint
 * GET /health
 */
router.get('/', async (req, res) => {
    try {
        const startTime = Date.now();
        
        // Check RabbitMQ connection
        const rabbitmqHealthy = await rabbitmqService.testConnection();
        
        // Get service statistics
        const stats = storage.getStatistics();
        const rabbitmqStatus = rabbitmqService.getStatus();
        
        const responseTime = Date.now() - startTime;
        
        const healthStatus = {
            status: rabbitmqHealthy ? 'healthy' : 'degraded',
            timestamp: new Date().toISOString(),
            uptime: stats.uptime,
            responseTime: `${responseTime}ms`,
            services: {
                rabbitmq: {
                    status: rabbitmqHealthy ? 'up' : 'down',
                    connected: rabbitmqStatus.connected,
                    reconnectAttempts: rabbitmqStatus.reconnectAttempts,
                    config: rabbitmqStatus.config
                },
                storage: {
                    status: 'up',
                    totalEvents: stats.totalEvents,
                    lastEventTime: stats.lastEventTime
                },
                webhook: {
                    status: 'up',
                    endpoint: '/webhook/calendar',
                    channels: storage.getWebhookChannels().length
                }
            },
            version: process.env.npm_package_version || '1.0.0',
            environment: process.env.NODE_ENV || 'development'
        };

        // Store health check result
        storage.addHealthCheck(healthStatus.status, {
            responseTime,
            rabbitmqHealthy,
            totalEvents: stats.totalEvents
        });

        // Return appropriate HTTP status
        const httpStatus = rabbitmqHealthy ? 200 : 503;
        res.status(httpStatus).json(healthStatus);
        
    } catch (error) {
        console.error('Health check error:', error);
        
        const errorStatus = {
            status: 'unhealthy',
            timestamp: new Date().toISOString(),
            error: error.message,
            services: {
                rabbitmq: { status: 'unknown' },
                storage: { status: 'unknown' },
                webhook: { status: 'unknown' }
            }
        };

        storage.addHealthCheck('unhealthy', {
            error: error.message
        });

        res.status(503).json(errorStatus);
    }
});

/**
 * Detailed health check with diagnostics
 * GET /health/detailed
 */
router.get('/detailed', async (req, res) => {
    try {
        const startTime = Date.now();
        
        // Comprehensive health checks
        const rabbitmqHealthy = await rabbitmqService.testConnection();
        const stats = storage.getStatistics();
        const rabbitmqStatus = rabbitmqService.getStatus();
        const recentHealthChecks = storage.getHealthChecks(10);
        
        const responseTime = Date.now() - startTime;
        
        const detailedHealth = {
            status: rabbitmqHealthy ? 'healthy' : 'degraded',
            timestamp: new Date().toISOString(),
            responseTime: `${responseTime}ms`,
            
            // System information
            system: {
                nodeVersion: process.version,
                platform: process.platform,
                arch: process.arch,
                pid: process.pid,
                memory: {
                    used: Math.round(process.memoryUsage().heapUsed / 1024 / 1024) + ' MB',
                    total: Math.round(process.memoryUsage().heapTotal / 1024 / 1024) + ' MB',
                    external: Math.round(process.memoryUsage().external / 1024 / 1024) + ' MB'
                },
                uptime: stats.uptime
            },
            
            // Service details
            services: {
                rabbitmq: {
                    status: rabbitmqHealthy ? 'up' : 'down',
                    connected: rabbitmqStatus.connected,
                    reconnectAttempts: rabbitmqStatus.reconnectAttempts,
                    config: rabbitmqStatus.config,
                    lastCheck: new Date().toISOString()
                },
                storage: {
                    status: 'up',
                    statistics: stats,
                    webhookChannels: storage.getWebhookChannels(),
                    recentEvents: stats.recentEvents
                },
                webhook: {
                    status: 'up',
                    endpoint: '/webhook/calendar',
                    validator: require('../services/validator').getValidationStats()
                }
            },
            
            // Recent health checks
            healthHistory: recentHealthChecks,
            
            // Configuration
            configuration: {
                environment: process.env.NODE_ENV || 'development',
                port: process.env.PORT || 3000,
                rabbitmqQueue: process.env.RABBITMQ_QUEUE || 'calendar_events'
            }
        };

        res.json(detailedHealth);
        
    } catch (error) {
        console.error('Detailed health check error:', error);
        res.status(503).json({
            status: 'error',
            timestamp: new Date().toISOString(),
            error: error.message,
            stack: process.env.NODE_ENV === 'development' ? error.stack : undefined
        });
    }
});

/**
 * Readiness probe for Kubernetes/Docker
 * GET /health/ready
 */
router.get('/ready', async (req, res) => {
    try {
        const rabbitmqHealthy = await rabbitmqService.testConnection();
        
        if (rabbitmqHealthy) {
            res.status(200).json({
                status: 'ready',
                timestamp: new Date().toISOString()
            });
        } else {
            res.status(503).json({
                status: 'not_ready',
                reason: 'RabbitMQ connection failed',
                timestamp: new Date().toISOString()
            });
        }
    } catch (error) {
        res.status(503).json({
            status: 'not_ready',
            reason: error.message,
            timestamp: new Date().toISOString()
        });
    }
});

/**
 * Liveness probe for Kubernetes/Docker
 * GET /health/live
 */
router.get('/live', (req, res) => {
    // Simple liveness check - if we can respond, we're alive
    res.status(200).json({
        status: 'alive',
        timestamp: new Date().toISOString(),
        uptime: process.uptime()
    });
});

/**
 * Health check history
 * GET /health/history
 */
router.get('/history', (req, res) => {
    try {
        const limit = parseInt(req.query.limit) || 50;
        const healthChecks = storage.getHealthChecks(limit);
        
        res.json({
            healthChecks,
            total: healthChecks.length,
            limit
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to retrieve health check history',
            message: error.message
        });
    }
});

module.exports = router;