const amqp = require('amqplib');

class RabbitMQService {
    constructor() {
        this.connection = null;
        this.channel = null;
        this.isConnected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;
        this.reconnectDelay = 5000; // 5 seconds
        
        this.config = {
            url: process.env.RABBITMQ_URL || 'amqp://admin:password@localhost:5672',
            queue: process.env.RABBITMQ_QUEUE || 'calendar_events',
            exchange: 'calendar_exchange',
            routingKey: 'calendar.event.changed'
        };
    }

    async connect() {
        try {
            console.log('Connecting to RabbitMQ...');
            
            this.connection = await amqp.connect(this.config.url);
            this.channel = await this.connection.createChannel();
            
            // Setup connection event handlers
            this.connection.on('error', this.handleConnectionError.bind(this));
            this.connection.on('close', this.handleConnectionClose.bind(this));
            
            // Declare exchange
            await this.channel.assertExchange(this.config.exchange, 'direct', {
                durable: true
            });
            
            // Declare queue
            await this.channel.assertQueue(this.config.queue, {
                durable: true,
                arguments: {
                    'x-message-ttl': 86400000, // 24 hours TTL
                    'x-dead-letter-exchange': `${this.config.exchange}.dlx`
                }
            });
            
            // Bind queue to exchange
            await this.channel.bindQueue(
                this.config.queue, 
                this.config.exchange, 
                this.config.routingKey
            );
            
            this.isConnected = true;
            this.reconnectAttempts = 0;
            
            console.log('RabbitMQ connected successfully');
            console.log(`Queue: ${this.config.queue}`);
            console.log(`Exchange: ${this.config.exchange}`);
            
            return true;
        } catch (error) {
            console.error('RabbitMQ connection failed:', error.message);
            this.isConnected = false;
            throw error;
        }
    }

    async disconnect() {
        try {
            if (this.channel) {
                await this.channel.close();
                this.channel = null;
            }
            
            if (this.connection) {
                await this.connection.close();
                this.connection = null;
            }
            
            this.isConnected = false;
            console.log('RabbitMQ disconnected');
        } catch (error) {
            console.error('Error disconnecting from RabbitMQ:', error.message);
        }
    }

    async publishMessage(message) {
        if (!this.isConnected || !this.channel) {
            throw new Error('RabbitMQ not connected');
        }

        try {
            const messageBuffer = Buffer.from(JSON.stringify(message));
            
            const published = this.channel.publish(
                this.config.exchange,
                this.config.routingKey,
                messageBuffer,
                {
                    persistent: true,
                    timestamp: Date.now(),
                    messageId: require('uuid').v4()
                }
            );

            if (!published) {
                throw new Error('Failed to publish message to RabbitMQ');
            }

            console.log('Message published to RabbitMQ:', {
                eventType: message.eventType,
                calendarId: message.calendarId
            });

            return true;
        } catch (error) {
            console.error('Error publishing message:', error.message);
            throw error;
        }
    }

    async testConnection() {
        try {
            if (!this.isConnected) {
                return false;
            }

            // Try to check queue info
            const queueInfo = await this.channel.checkQueue(this.config.queue);
            console.log('RabbitMQ health check passed:', {
                queue: this.config.queue,
                messageCount: queueInfo.messageCount,
                consumerCount: queueInfo.consumerCount
            });
            
            return true;
        } catch (error) {
            console.error('RabbitMQ health check failed:', error.message);
            this.isConnected = false;
            return false;
        }
    }

    handleConnectionError(error) {
        console.error('RabbitMQ connection error:', error.message);
        this.isConnected = false;
        this.attemptReconnect();
    }

    handleConnectionClose() {
        console.log('RabbitMQ connection closed');
        this.isConnected = false;
        this.attemptReconnect();
    }

    async attemptReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.error('Max reconnection attempts reached. Giving up.');
            return;
        }

        this.reconnectAttempts++;
        console.log(`Attempting to reconnect to RabbitMQ (${this.reconnectAttempts}/${this.maxReconnectAttempts})...`);

        setTimeout(async () => {
            try {
                await this.connect();
            } catch (error) {
                console.error('Reconnection failed:', error.message);
            }
        }, this.reconnectDelay);
    }

    getStatus() {
        return {
            connected: this.isConnected,
            reconnectAttempts: this.reconnectAttempts,
            config: {
                queue: this.config.queue,
                exchange: this.config.exchange,
                routingKey: this.config.routingKey
            }
        };
    }
}

module.exports = new RabbitMQService();