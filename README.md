# Calendar Sync Service

A robust, enterprise-grade calendar synchronization service that provides real-time synchronization between Google Calendar and local systems using a hybrid approach combining webhooks and polling strategies.

## 🏗️ Architecture Overview

The Calendar Sync Service is built using a microservices architecture with the following key components:

### Core Components

1. **Calendar Sync Service (.NET 8 Windows Service)**
   - Main synchronization engine
   - Hybrid sync orchestrator (webhook + polling)
   - Google Calendar API integration
   - Database operations and event processing
   - UDP notification system

2. **Webhook Service (Node.js/Express)**
   - Receives Google Calendar webhook notifications
   - Publishes events to RabbitMQ message queue
   - Health monitoring and web dashboard
   - HTTPS support for production

3. **RabbitMQ Message Broker**
   - Handles asynchronous message processing
   - Ensures reliable event delivery
   - Provides message persistence and durability

4. **SQL Server Database**
   - Stores calendar events and sync state
   - Maintains sync tokens for incremental updates
   - Event history and audit trail

### Architecture Diagram

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  Google Calendar│    │   Webhook Service│    │   RabbitMQ      │
│                 │───▶│   (Node.js)      │───▶│   Message Queue │
│   Webhook API   │    │   Port: 3000     │    │   Port: 5672    │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                                         │
                                                         ▼
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   SQL Server    │◀───│ Calendar Sync    │◀───│   Event         │
│   Database      │    │ Service (.NET)   │    │   Processing    │
│                 │    │ Windows Service  │    │                 │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                │
                                ▼
                       ┌─────────────────┐
                       │ UDP Notification│
                       │ Service         │
                       │ Port: 11004     │
                       └─────────────────┘
```

## 🚀 Key Features

- **Hybrid Synchronization**: Combines real-time webhooks with reliable polling fallback
- **High Availability**: Automatic failover between sync strategies
- **Event Processing**: Debounced event handling to prevent duplicate processing
- **Health Monitoring**: Comprehensive health checks for all components
- **Scalable Architecture**: Microservices design with message queue integration
- **Security**: HTTPS support, rate limiting, and secure credential management
- **Monitoring**: Structured logging with Serilog and UDP notifications
- **Docker Support**: Complete containerization with Docker Compose

## 📋 Prerequisites

- **.NET 8 SDK** (for Calendar Sync Service)
- **Node.js 18+** (for Webhook Service)
- **Docker & Docker Compose** (for containerized deployment)
- **SQL Server** (local or remote instance)
- **Google Cloud Project** with Calendar API enabled
- **Service Account Key** for Google Calendar API access

## 🛠️ Installation & Setup

### 1. Clone the Repository

```bash
git clone <repository-url>
cd CalendarSyncService
```

### 2. Google Calendar API Setup

1. Create a Google Cloud Project
2. Enable the Google Calendar API
3. Create a Service Account
4. Download the service account key JSON file
5. Place the key file in the project directory
6. Update the path in `appsettings.json`

### 3. Database Setup

1. Create a SQL Server database
2. Run the database schema script:

```sql
-- Execute the contents of database.sql
```

3. Update the connection string in `appsettings.json`

### 4. Configuration

#### Calendar Sync Service Configuration (`appsettings.json`)

```json
{
  "Google": {
    "ServiceAccountKeyPath": "path/to/your/service-account-key.json",
    "CalendarId": "your-calendar-id@group.calendar.google.com"
  },
  "Database": {
    "ConnectionString": "Server=your_server;Database=CalendarDB;Trusted_Connection=True;"
  },
  "Sync": {
    "NormalPollingIntervalMinutes": 30,
    "FallbackPollingIntervalMinutes": 5
  },
  "Notification": {
    "UdpHost": "127.0.0.1",
    "UdpPort": 11004
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "admin",
    "Password": "password",
    "QueueName": "calendar_events"
  },
  "Webhook": {
    "Enabled": true,
    "ServiceUrl": "https://your-public-domain.com",  // MUST be a public HTTPS URL for Google Calendar webhooks
    "HealthCheckIntervalSeconds": 30,
    "FallbackToPolling": true,
    "DebounceDelayMs": 2000
  }
}
```

#### Environment Variables (`.env`)

```env
# RabbitMQ Configuration
RABBITMQ_USER=admin
RABBITMQ_PASS=password
RABBITMQ_QUEUE=calendar_events

# Node.js Service Configuration
NODE_ENV=development
WEBHOOK_SERVICE_PORT=3000

# SSL Configuration (for production)
SSL_CERT_PATH=/certs/cert.pem
SSL_KEY_PATH=/certs/key.pem
```

#### ⚠️ **Important: Webhook Service URL Requirements**

**Google Calendar webhooks require a publicly accessible HTTPS URL:**

**Development Environment:**
- Use **ngrok**: `ngrok http 3000` → `https://abc123.ngrok.io`
- Use **Cloudflare Tunnel**: `cloudflared tunnel --url http://localhost:3000` → `https://xyz.trycloudflare.com`
- Update `ServiceUrl` in `appsettings.json` with the public HTTPS URL

**Production Environment:**
- Deploy the webhook-service Docker container to a cloud provider
- Configure proper SSL certificates and domain
- Ensure the URL is publicly accessible via HTTPS

```json
// Example production configuration
"Webhook": {
  "ServiceUrl": "https://your-domain.com/webhook",  // Public HTTPS URL
  "Enabled": true
}
```

## 🐳 Docker Deployment

### Quick Start with Docker Compose

```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down
```

### Services Overview

- **RabbitMQ**: `http://localhost:15672` (admin/password)
- **Webhook Service**: `http://localhost:3000`
- **Calendar Sync Service**: Runs as background service

## 💻 Development

### Running in Development Mode

#### 1. Start Infrastructure Services

```bash
# Start RabbitMQ and other dependencies
docker-compose up rabbitmq -d
```

#### 2. Run Webhook Service

```bash
cd webhook-service
npm install
npm run dev
```

#### 3. Run Calendar Sync Service

```bash
cd CalendarSync
dotnet restore
dotnet run
```

### Project Structure

```
CalendarSyncService/
├── CalendarSync/                 # .NET Windows Service
│   ├── EventProcessing/         # Event processing logic
│   ├── Models/                  # Data models and settings
│   ├── Services/                # Core services (Google, Database, RabbitMQ)
│   ├── Strategies/              # Synchronization strategies and orchestration
│   │   ├── HybridSyncOrchestrator.cs    # Main orchestrator coordinating sync strategies
│   │   ├── WebhookSyncStrategy.cs       # Real-time webhook-based synchronization
│   │   ├── PollingSyncStrategy.cs       # Interval-based polling synchronization
│   │   ├── ICalendarSyncStrategy.cs     # Strategy interface
│   │   └── ISyncOrchestrator.cs         # Orchestrator interface
│   ├── Utilities/               # Helper utilities
│   └── Worker.cs                # Main background service
├── webhook-service/             # Node.js webhook receiver
│   ├── src/
│   │   ├── routes/             # Express routes
│   │   └── services/           # Business logic services
│   ├── public/                 # Web dashboard assets
│   └── app.js                  # Main application
├── rabbitmq/                   # RabbitMQ configuration
├── certs/                      # SSL certificates
├── docker-compose.yml          # Container orchestration
└── database.sql               # Database schema
```

### Key Design Patterns

1. **Strategy Pattern**: Different sync strategies (Webhook, Polling, Hybrid)
2. **Observer Pattern**: Health status monitoring and notifications
3. **Circuit Breaker**: Automatic failover between sync strategies when health check fails

## 🔧 Configuration Options

### Synchronization Architecture

The system uses a sophisticated hybrid synchronization approach managed by the **HybridSyncOrchestrator** located in the `Strategies/` folder:

#### **HybridSyncOrchestrator**
- **Primary Role**: Orchestrates and coordinates the use of two complementary synchronization strategies
- **Strategy Management**: Intelligently switches between strategies based on system health and availability
- **Health Monitoring**: Continuously monitors the health of webhook service and RabbitMQ infrastructure

#### **WebhookSyncStrategy** 
- **Priority**: Primary synchronization method (preferred when available)
- **Operation**: Receives real-time notifications from Google Calendar via webhook endpoints
- **Dependency**: Requires webhook service and RabbitMQ to be operational
- **Advantage**: Provides immediate synchronization with minimal latency

#### **PollingSyncStrategy**
- **Role**: Continuous backup synchronization method that always runs
- **Adaptive Intervals**: Dynamically adjusts polling frequency based on system health:
  - **Normal Operation**: 30 minutes interval when webhook and RabbitMQ are healthy
  - **Fallback Mode**: Reduces to 5 minutes (1 minute in development) when webhook service or RabbitMQ is unavailable
- **Reliability**: Ensures synchronization continues even when webhook infrastructure fails

### Health Monitoring

The system continuously monitors:
- Google Calendar API connectivity
- RabbitMQ connection status
- Webhook service health
- Database connectivity
- Sync operation success rates

### Notification System

- **UDP Notifications**: Real-time status updates via UDP protocol for planned desktop UI application to display synchronized calendar data and sync status
- **Structured Logging**: Comprehensive logging with Serilog
- **Health Endpoints**: HTTP health check endpoints

## 🚨 Troubleshooting

### Common Issues

1. **Google Calendar API Authentication**
   - Verify service account key path
   - Check API permissions and scopes
   - Ensure Calendar API is enabled

2. **RabbitMQ Connection Issues**
   - Verify RabbitMQ is running
   - Check connection credentials
   - Confirm network connectivity

3. **Webhook Registration Failures**
   - Ensure webhook URL is publicly accessible
   - Check HTTPS certificate validity
   - Verify Google Calendar webhook requirements

4. **Database Connection Problems**
   - Validate connection string
   - Check SQL Server accessibility
   - Verify database schema exists

### Logs and Monitoring

- **Calendar Sync Service Logs**: `logs/calendar-sync-*.txt`
- **Webhook Service Logs**: Console output or container logs
- **RabbitMQ Management UI**: `http://localhost:15672`
- **Health Check Endpoint**: `http://localhost:3000/health`

## 📊 Performance Considerations

- **Debouncing**: Prevents duplicate event processing
- **Incremental Sync**: Uses sync tokens for efficient updates
- **Connection Pooling**: Optimized database connections
- **Message Queuing**: Asynchronous event processing
- **Health Checks**: Proactive monitoring and failover

## 🔒 Security

- **Service Account Authentication**: Secure Google API access
- **HTTPS Support**: Encrypted webhook communications
- **Rate Limiting**: Protection against abuse
- **Input Validation**: Secure webhook payload processing
- **Credential Management**: Environment-based configuration

## 📝 API Documentation

### Webhook Endpoints

- `POST /webhook/calendar` - Google Calendar webhook receiver
- `GET /health` - Health check endpoint
- `GET /api/stats` - Service statistics
- `GET /` - Web dashboard

### UDP Notification Format

```
SYNC_STATUS|<status>|<calendar_id>|<event_count>|<timestamp>
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🆘 Support

For support and questions:
- Check the troubleshooting section
- Review the logs for error details
- Create an issue in the repository
- Consult the Google Calendar API documentation