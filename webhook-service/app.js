/**
 * Calendar Webhook Service - Main Application
 * 
 * This is the main Express.js application that handles Google Calendar webhook notifications
 * and publishes events to RabbitMQ for processing by the Windows Service.
 * 
 * Features:
 * - Google Calendar webhook endpoint
 * - RabbitMQ message publishing
 * - Health checks and monitoring
 * - Web dashboard for statistics
 * - Request validation and error handling
 * - CORS and security middleware
 */

const express = require('express');
const cors = require('cors');
const helmet = require('helmet');
const compression = require('compression');
const rateLimit = require('express-rate-limit');
const path = require('path');

// Import services
const rabbitmqService = require('./services/rabbitmq');
const storageService = require('./services/storage');

// Import routes
const healthRoutes = require('./routes/health');
const webhookRoutes = require('./routes/webhook');
const apiRoutes = require('./routes/api');

// Configuration
const config = {
    port: process.env.PORT || 3000,
    httpsPort: process.env.HTTPS_PORT || 3443,
    nodeEnv: process.env.NODE_ENV || 'development',
    corsOrigins: process.env.CORS_ORIGINS ? process.env.CORS_ORIGINS.split(',') : ['http://localhost:3000'],
    rateLimitWindow: parseInt(process.env.RATE_LIMIT_WINDOW) || 15 * 60 * 1000, // 15 minutes
    rateLimitMax: parseInt(process.env.RATE_LIMIT_MAX) || 1000, // requests per window
    webhookRateLimit: parseInt(process.env.WEBHOOK_RATE_LIMIT) || 100, // webhook requests per window
};

class WebhookApplication {
    constructor() {
        this.app = express();
        this.server = null;
        this.startTime = Date.now();
        
        this.setupMiddleware();
        this.setupRoutes();
        this.setupErrorHandling();
    }

    setupMiddleware() {
        // Security middleware
        this.app.use(helmet({
            contentSecurityPolicy: {
                directives: {
                    defaultSrc: ["'self'"],
                    styleSrc: ["'self'", "'unsafe-inline'", "https://cdnjs.cloudflare.com"],
                    scriptSrc: ["'self'", "'unsafe-inline'"],
                    fontSrc: ["'self'", "https://cdnjs.cloudflare.com"],
                    imgSrc: ["'self'", "data:", "https:"],
                    connectSrc: ["'self'"]
                }
            }
        }));

        // CORS configuration
        this.app.use(cors({
            origin: config.corsOrigins,
            credentials: true,
            methods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS'],
            allowedHeaders: ['Content-Type', 'Authorization', 'X-Goog-Channel-ID', 'X-Goog-Channel-Token', 'X-Goog-Resource-ID', 'X-Goog-Resource-URI', 'X-Goog-Resource-State', 'X-Goog-Message-Number']
        }));

        // Compression
        this.app.use(compression());

        // Body parsing
        this.app.use(express.json({ limit: '10mb' }));
        this.app.use(express.urlencoded({ extended: true, limit: '10mb' }));

        // Rate limiting
        const generalLimiter = rateLimit({
            windowMs: config.rateLimitWindow,
            max: config.rateLimitMax,
            message: {
                error: 'Too many requests',
                message: 'Rate limit exceeded. Please try again later.',
                retryAfter: Math.ceil(config.rateLimitWindow / 1000)
            },
            standardHeaders: true,
            legacyHeaders: false,
            skip: (req) => {
                // Skip rate limiting for health checks
                return req.path.startsWith('/health');
            }
        });

        const webhookLimiter = rateLimit({
            windowMs: config.rateLimitWindow,
            max: config.webhookRateLimit,
            message: {
                error: 'Webhook rate limit exceeded',
                message: 'Too many webhook requests. Please check your Google Calendar notification settings.',
                retryAfter: Math.ceil(config.rateLimitWindow / 1000)
            },
            standardHeaders: true,
            legacyHeaders: false
        });

        this.app.use(generalLimiter);
        this.app.use('/webhook', webhookLimiter);

        // Request logging middleware
        this.app.use((req, res, next) => {
            const start = Date.now();
            
            // Log request
            console.log(`[${new Date().toISOString()}] ${req.method} ${req.path} - ${req.ip}`);
            
            // Log response when finished
            res.on('finish', () => {
                const duration = Date.now() - start;
                const logLevel = res.statusCode >= 400 ? 'ERROR' : 'INFO';
                console.log(`[${new Date().toISOString()}] ${logLevel} ${req.method} ${req.path} - ${res.statusCode} (${duration}ms)`);
                
                // Store request statistics
                storageService.recordHealthCheck({
                    timestamp: new Date().toISOString(),
                    method: req.method,
                    path: req.path,
                    statusCode: res.statusCode,
                    duration,
                    ip: req.ip,
                    userAgent: req.get('User-Agent')
                });
            });
            
            next();
        });

        // Static files for dashboard
        this.app.use(express.static(path.join(__dirname, 'public')));
    }

    setupRoutes() {
        // Health check routes
        this.app.use('/health', healthRoutes);
        
        // Webhook routes
        this.app.use('/webhook', webhookRoutes);
        
        // API routes for dashboard
        this.app.use('/api', apiRoutes);

        // Root route - serve dashboard
        this.app.get('/', (req, res) => {
            res.sendFile(path.join(__dirname, 'public', 'index.html'));
        });

        // Catch-all route for SPA routing
        this.app.get('*', (req, res) => {
            // Only serve index.html for non-API routes
            if (!req.path.startsWith('/api') && !req.path.startsWith('/webhook') && !req.path.startsWith('/health')) {
                res.sendFile(path.join(__dirname, 'public', 'index.html'));
            } else {
                res.status(404).json({
                    error: 'Not Found',
                    message: 'The requested endpoint does not exist',
                    path: req.path,
                    timestamp: new Date().toISOString()
                });
            }
        });
    }

    setupErrorHandling() {
        // 404 handler for API routes
        this.app.use('/api', (req, res) => {
            res.status(404).json({
                error: 'API endpoint not found',
                message: `The API endpoint ${req.method} ${req.path} does not exist`,
                availableEndpoints: [
                    'GET /api/stats',
                    'GET /api/events',
                    'GET /api/channels',
                    'GET /api/system',
                    'POST /api/test-rabbitmq'
                ],
                timestamp: new Date().toISOString()
            });
        });

        // Global error handler
        this.app.use((error, req, res, next) => {
            console.error(`[${new Date().toISOString()}] ERROR:`, error);

            // Don't leak error details in production
            const isDevelopment = config.nodeEnv === 'development';
            
            const errorResponse = {
                error: 'Internal Server Error',
                message: isDevelopment ? error.message : 'An unexpected error occurred',
                timestamp: new Date().toISOString(),
                path: req.path,
                method: req.method
            };

            if (isDevelopment) {
                errorResponse.stack = error.stack;
                errorResponse.details = error;
            }

            // Log error to storage
            storageService.recordHealthCheck({
                timestamp: new Date().toISOString(),
                type: 'error',
                error: error.message,
                stack: error.stack,
                path: req.path,
                method: req.method,
                ip: req.ip
            });

            res.status(error.status || 500).json(errorResponse);
        });

        // Handle unhandled promise rejections
        process.on('unhandledRejection', (reason, promise) => {
            console.error('[UNHANDLED REJECTION]', reason);
            // Don't exit the process, just log the error
        });

        // Handle uncaught exceptions
        process.on('uncaughtException', (error) => {
            console.error('[UNCAUGHT EXCEPTION]', error);
            // In production, you might want to exit gracefully
            if (config.nodeEnv === 'production') {
                this.gracefulShutdown('UNCAUGHT_EXCEPTION');
            }
        });
    }

    async start() {
        try {
            // Initialize services
            console.log('Initializing services...');
            
            // Initialize RabbitMQ connection
            await rabbitmqService.connect();
            console.log('✓ RabbitMQ connection established');

            // Initialize storage service
            storageService.init();
            console.log('✓ Storage service initialized');

            // Start HTTP server
            this.server = this.app.listen(config.port, () => {
                console.log(`
╔══════════════════════════════════════════════════════════════╗
║                Calendar Webhook Service                      ║
╠══════════════════════════════════════════════════════════════╣
║ Status: Running                                              ║
║ Port: ${config.port.toString().padEnd(53)} ║
║ Environment: ${config.nodeEnv.padEnd(47)} ║
║ Dashboard: http://localhost:${config.port.toString().padEnd(39)} ║
║ Health Check: http://localhost:${config.port}/health${' '.repeat(25)} ║
╚══════════════════════════════════════════════════════════════╝
                `);
            });

            // Handle server errors
            this.server.on('error', (error) => {
                if (error.code === 'EADDRINUSE') {
                    console.error(`Port ${config.port} is already in use`);
                    process.exit(1);
                } else {
                    console.error('Server error:', error);
                }
            });

            // Graceful shutdown handlers
            process.on('SIGTERM', () => this.gracefulShutdown('SIGTERM'));
            process.on('SIGINT', () => this.gracefulShutdown('SIGINT'));

        } catch (error) {
            console.error('Failed to start webhook service:', error);
            process.exit(1);
        }
    }

    async gracefulShutdown(signal) {
        console.log(`\nReceived ${signal}. Starting graceful shutdown...`);

        // Stop accepting new connections
        if (this.server) {
            this.server.close(() => {
                console.log('✓ HTTP server closed');
            });
        }

        try {
            // Close RabbitMQ connection
            await rabbitmqService.disconnect();
            console.log('✓ RabbitMQ connection closed');

            // Cleanup storage service
            storageService.cleanup();
            console.log('✓ Storage service cleaned up');

            console.log('Graceful shutdown completed');
            process.exit(0);
        } catch (error) {
            console.error('Error during graceful shutdown:', error);
            process.exit(1);
        }
    }

    getUptime() {
        return Math.floor((Date.now() - this.startTime) / 1000);
    }

    getApp() {
        return this.app;
    }
}

// Create and export application instance
const webhookApp = new WebhookApplication();

// Start the application if this file is run directly
if (require.main === module) {
    webhookApp.start().catch(error => {
        console.error('Failed to start application:', error);
        process.exit(1);
    });
}

module.exports = webhookApp;