/* eslint-disable @typescript-eslint/no-non-null-assertion */
import * as sql from "mssql";
import { CalendarEvent } from "../types/CalendarEvent";

/**
 * Database service interface
 */
export interface DatabaseService {
	getEvents(offset: number, limit: number): Promise<CalendarEvent[]>;
	getEventsCount(): Promise<number>;
	getEventById(eventId: string): Promise<CalendarEvent | null>;
	searchEvents(query: string): Promise<CalendarEvent[]>;
	testConnection(): Promise<boolean>;
}

/**
 * Database configuration matching appsettings.Development.json
 * Connection String: "Server=localhost;Database=CalendarSync;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;"
 */
const dbConfig: sql.config = {
	server: "localhost",
	database: "CalendarSync",
	options: {
		trustedConnection: true,
		encrypt: false,
		trustServerCertificate: true,
	},
	connectionTimeout: 30000,
	requestTimeout: 30000,
	pool: {
		max: 10,
		min: 0,
		idleTimeoutMillis: 30000,
	},
};

/**
 * Database service implementation with enhanced error handling and connection monitoring
 */
export class SqlServerDatabaseService implements DatabaseService {
	private pool: sql.ConnectionPool | null = null;
	private isConnected = false;
	private connectionAttempts = 0;
	private maxRetries = 3;

	/**
	 * Initialize database connection pool with retry logic
	 */
	async initialize(): Promise<void> {
		if (this.isConnected && this.pool) {
			return;
		}

		try {
			this.pool = new sql.ConnectionPool(dbConfig);

			// Add connection event handlers
			this.pool.on("connect", () => {
				console.log("Database connection established successfully");
				this.isConnected = true;
				this.connectionAttempts = 0;
			});

			this.pool.on("error", (err) => {
				console.error("Database connection error:", err);
				this.isConnected = false;
			});

			await this.pool.connect();
		} catch (error) {
			this.connectionAttempts++;
			console.error(
				`Database connection failed (attempt ${this.connectionAttempts}/${this.maxRetries}):`,
				error
			);

			if (this.connectionAttempts < this.maxRetries) {
				console.log(`Retrying connection in 2 seconds...`);
				await new Promise((resolve) => setTimeout(resolve, 2000));
				return this.initialize();
			}

			throw new Error(
				`Failed to connect to database after ${this.maxRetries} attempts: ${error}`
			);
		}
	}

	/**
	 * Get connection status
	 */
	getConnectionStatus(): { connected: boolean; attempts: number } {
		return {
			connected: this.isConnected,
			attempts: this.connectionAttempts,
		};
	}

	/**
	 * Test database connection
	 */
	async testConnection(): Promise<boolean> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			await request.query("SELECT 1 as test");
			return true;
		} catch (error) {
			console.error("Database connection test failed:", error);
			this.isConnected = false;
			return false;
		}
	}

	/**
	 * Get paginated events with enhanced error handling
	 */
	async getEvents(offset: number, limit: number): Promise<CalendarEvent[]> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			request.input("offset", sql.Int, offset);
			request.input("limit", sql.Int, Math.min(limit, 100)); // Limit max results to 100

			const result = await request.query(`
        SELECT * FROM CalendarEvents 
        ORDER BY StartTime DESC 
        OFFSET @offset ROWS 
        FETCH NEXT @limit ROWS ONLY
      `);

			return result.recordset.map(this.mapRowToEvent);
		} catch (error) {
			console.error("Error fetching events:", error);
			this.isConnected = false;
			throw new Error(`Failed to fetch events: ${error}`);
		}
	}

	/**
	 * Get total events count with enhanced error handling
	 */
	async getEventsCount(): Promise<number> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			const result = await request.query(
				"SELECT COUNT(*) as count FROM CalendarEvents"
			);

			return result.recordset[0].count || 0;
		} catch (error) {
			console.error("Error getting events count:", error);
			this.isConnected = false;
			throw new Error(`Failed to get events count: ${error}`);
		}
	}

	/**
	 * Get event by ID with enhanced error handling
	 */
	async getEventById(eventId: string): Promise<CalendarEvent | null> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			request.input("eventId", sql.NVarChar, eventId);

			const result = await request.query(
				"SELECT * FROM CalendarEvents WHERE EventID = @eventId"
			);

			if (result.recordset.length === 0) {
				return null;
			}

			return this.mapRowToEvent(result.recordset[0]);
		} catch (error) {
			console.error("Error fetching event by ID:", error);
			this.isConnected = false;
			throw new Error(`Failed to fetch event by ID: ${error}`);
		}
	}

	/**
	 * Search events with enhanced error handling and input validation
	 */
	async searchEvents(query: string): Promise<CalendarEvent[]> {
		try {
			if (!query || query.trim().length === 0) {
				return [];
			}

			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			const searchTerm = `%${query.trim()}%`;
			request.input("query", sql.NVarChar, searchTerm);

			const result = await request.query(`
        SELECT * FROM CalendarEvents 
        WHERE Summary LIKE @query 
           OR Description LIKE @query 
           OR Location LIKE @query
           OR OrganizerEmail LIKE @query
        ORDER BY StartTime DESC
      `);

			return result.recordset.map(this.mapRowToEvent);
		} catch (error) {
			console.error("Error searching events:", error);
			this.isConnected = false;
			throw new Error(`Failed to search events: ${error}`);
		}
	}

	/**
	 * Map database row to CalendarEvent interface with null safety
	 */
	private mapRowToEvent(row: any): CalendarEvent {
		return {
			eventID: row.EventID || "",
			calendarID: row.CalendarID || "",
			summary: row.Summary || "",
			description: row.Description || "",
			startTime: row.StartTime ? new Date(row.StartTime) : new Date(),
			endTime: row.EndTime ? new Date(row.EndTime) : new Date(),
			createdTime: row.CreatedTime ? new Date(row.CreatedTime) : new Date(),
			updatedTime: row.UpdatedTime ? new Date(row.UpdatedTime) : new Date(),
			location: row.Location || null,
			status: row.Status || "",
			organizerEmail: row.OrganizerEmail || null,
			attendees: row.Attendees || null,
			recurrence: row.Recurrence || null,
		};
	}

	/**
	 * Close database connection and cleanup
	 */
	async close(): Promise<void> {
		try {
			if (this.pool) {
				await this.pool.close();
				this.pool = null;
				this.isConnected = false;
				this.connectionAttempts = 0;
				console.log("Database connection closed successfully");
			}
		} catch (error) {
			console.error("Error closing database connection:", error);
		}
	}
}

// Export singleton instance
export const databaseService = new SqlServerDatabaseService();
