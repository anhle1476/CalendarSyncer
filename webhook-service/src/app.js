const express = require('express');
const cors = require('cors');
const helmet = require('helmet');
const morgan = require('morgan');
const path = require('path');
require('dotenv').config();

const webhookRoutes = require('./routes/webhook');
const apiRoutes = require('./routes/api');
const healthRoutes = require('./routes/health');
const rabbitmqService = require('./services/rabbitmq');
const storage = require('./services/storage');

const app = express();
const PORT = process.env.PORT || 3000;

// Security middleware
app.use(helmet());
app.use(cors());

// Logging middleware
app.use(morgan('combined'));

// Body parsing middleware
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true }));

// Serve static files
app.use(express.static(path.join(__dirname, '../public')));

// Routes
app.use('/health', healthRoutes);
app.use('/webhook', webhookRoutes);
app.use('/api', apiRoutes);

// Root route - serve dashboard
app.get('/', (req, res) => {
    res.sendFile(path.join(__dirname, '../public/index.html'));
});

// Error handling middleware
app.use((err, req, res, next) => {
    console.error('Error:', err);
    res.status(500).json({
        error: 'Internal Server Error',
        message: process.env.NODE_ENV === 'development' ? err.message : 'Something went wrong'
    });
});

// 404 handler
app.use((req, res) => {
    res.status(404).json({
        error: 'Not Found',
        message: 'The requested resource was not found'
    });
});

// Graceful shutdown
process.on('SIGTERM', async () => {
    console.log('SIGTERM received, shutting down gracefully');
    await rabbitmqService.disconnect();
    process.exit(0);
});

process.on('SIGINT', async () => {
    console.log('SIGINT received, shutting down gracefully');
    await rabbitmqService.disconnect();
    process.exit(0);
});

// Start server
async function startServer() {
    try {
        // Initialize RabbitMQ connection
        await rabbitmqService.connect();
        console.log('RabbitMQ connected successfully');

        // Initialize storage
        storage.initialize();
        console.log('Storage initialized');

        // Start HTTP server
        app.listen(PORT, () => {
            console.log(`Calendar Webhook Service running on port ${PORT}`);
            console.log(`Environment: ${process.env.NODE_ENV}`);
            console.log(`Dashboard: http://localhost:${PORT}`);
            console.log(`Health Check: http://localhost:${PORT}/health`);
            console.log(`Webhook Endpoint: http://localhost:${PORT}/webhook/calendar`);
        });
    } catch (error) {
        console.error('Failed to start server:', error);
        process.exit(1);
    }
}

startServer();