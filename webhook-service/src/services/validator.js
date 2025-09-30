const Joi = require('joi');

class ValidatorService {
    constructor() {
        // Google Calendar webhook notification schema
        this.webhookSchema = Joi.object({
            // Headers validation
            'x-goog-channel-id': Joi.string().required(),
            'x-goog-channel-token': Joi.string().optional(),
            'x-goog-channel-expiration': Joi.string().optional(),
            'x-goog-resource-id': Joi.string().required(),
            'x-goog-resource-uri': Joi.string().uri().required(),
            'x-goog-resource-state': Joi.string().valid('sync', 'exists', 'not_exists').required(),
            'x-goog-message-number': Joi.string().optional()
        });

        // Simplified event message schema for RabbitMQ - only essential fields
        this.eventMessageSchema = Joi.object({
            calendarId: Joi.string().required(),
            eventType: Joi.string().required(),
            timestamp: Joi.string().required(),
            resourceId: Joi.string().required(),
            channelId: Joi.string().required(),
            resourceState: Joi.string().required()
        }).unknown(true); // Allow additional fields without validation
    }

    /**
     * Validate Google Calendar webhook headers
     * @param {Object} headers - Request headers
     * @returns {Object} Validation result
     */
    validateWebhookHeaders(headers) {
        try {
            // Normalize header names to lowercase
            const normalizedHeaders = {};
            Object.keys(headers).forEach(key => {
                normalizedHeaders[key.toLowerCase()] = headers[key];
            });

            // Validate required Google headers
            const { error, value } = this.webhookSchema.validate(normalizedHeaders, {
                allowUnknown: true,
                stripUnknown: false
            });

            if (error) {
                return {
                    isValid: false,
                    error: error.details[0].message,
                    details: error.details
                };
            }

            return {
                isValid: true,
                headers: value
            };
        } catch (error) {
            return {
                isValid: false,
                error: 'Validation error: ' + error.message,
                details: [error.message]
            };
        }
    }

    /**
     * Validate event message for RabbitMQ
     * @param {Object} message - Event message
     * @returns {Object} Validation result
     */
    validateEventMessage(message) {
        try {
            const { error, value } = this.eventMessageSchema.validate(message);

            if (error) {
                return {
                    isValid: false,
                    error: error.details[0].message,
                    details: error.details
                };
            }

            return {
                isValid: true,
                message: value
            };
        } catch (error) {
            return {
                isValid: false,
                error: 'Message validation error: ' + error.message,
                details: [error.message]
            };
        }
    }

    /**
     * Extract calendar ID from resource URI
     * @param {string} resourceUri - Google Calendar resource URI
     * @returns {string|null} Calendar ID or null if not found
     */
    extractCalendarId(resourceUri) {
        try {
            // Google Calendar resource URI format:
            // https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events?alt=json
            const match = resourceUri.match(/\/calendars\/([^\/]+)\/events/);
            return match ? decodeURIComponent(match[1]) : null;
        } catch (error) {
            console.error('Error extracting calendar ID:', error.message);
            return null;
        }
    }

    /**
     * Determine event type from resource state and context
     * @param {string} resourceState - Google resource state
     * @param {Object} context - Additional context
     * @returns {string} Event type
     */
    determineEventType(resourceState, context = {}) {
        switch (resourceState) {
            case 'sync':
                return 'sync';
            case 'exists':
                return context.isNew ? 'created' : 'updated';
            case 'not_exists':
                return 'deleted';
            default:
                return 'updated'; // Default fallback
        }
    }

    /**
     * Create optimized event message from Google webhook headers
     * Only includes essential x-goog-* header information
     * @param {Object} headers - Validated webhook headers
     * @returns {Object} Optimized event message for RabbitMQ
     */
    createEventMessage(headers) {
        const calendarId = this.extractCalendarId(headers['x-goog-resource-uri']);
        
        if (!calendarId) {
            throw new Error('Could not extract calendar ID from resource URI');
        }

        const eventType = this.determineEventType(headers['x-goog-resource-state']);

        // Only include essential Google webhook information
        return {
            calendarId: calendarId,
            eventType: eventType,
            timestamp: new Date().toISOString(),
            resourceId: headers['x-goog-resource-id'],
            resourceUri: headers['x-goog-resource-uri'],
            channelId: headers['x-goog-channel-id'],
            channelToken: headers['x-goog-channel-token'] || null,
            messageNumber: headers['x-goog-message-number'] || null,
            resourceState: headers['x-goog-resource-state'],
            receivedAt: new Date().toISOString()
        };
    }

    /**
     * Validate and process webhook request
     * @param {Object} headers - Request headers
     * @param {Object} body - Request body (usually empty for Google webhooks)
     * @returns {Object} Processing result
     */
    processWebhookRequest(headers, body = {}) {
        try {
            // Validate headers
            const headerValidation = this.validateWebhookHeaders(headers);
            if (!headerValidation.isValid) {
                return {
                    success: false,
                    error: headerValidation.error,
                    details: headerValidation.details
                };
            }

            // Create event message
            const eventMessage = this.createEventMessage(headerValidation.headers);

            // Skip detailed validation - just return the message
            return {
                success: true,
                eventMessage: eventMessage,
                headers: headerValidation.headers
            };
        } catch (error) {
            return {
                success: false,
                error: 'Processing error: ' + error.message,
                details: [error.message]
            };
        }
    }

    /**
     * Check if webhook request is a sync message
     * @param {Object} headers - Request headers
     * @returns {boolean} True if sync message
     */
    isSyncMessage(headers) {
        const resourceState = headers['x-goog-resource-state'] || headers['X-Goog-Resource-State'];
        return resourceState === 'sync';
    }

    /**
     * Get validation statistics
     * @returns {Object} Validation statistics
     */
    getValidationStats() {
        return {
            schemas: {
                webhook: 'active',
                eventMessage: 'active'
            }
        };
    }
}

module.exports = new ValidatorService();