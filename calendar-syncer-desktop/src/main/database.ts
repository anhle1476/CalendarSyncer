import * as sql from "mssql";
import { CalendarEvent } from "../types/CalendarEvent";
import * as dotenv from "dotenv";

// Load environment variables
dotenv.config();

export interface DatabaseService {
	getEvents(offset: number, limit: number): Promise<CalendarEvent[]>;
	getEventsCount(): Promise<number>;
	getEventById(eventId: string): Promise<CalendarEvent | null>;
	searchEvents(query: string): Promise<CalendarEvent[]>;
	testConnection(): Promise<boolean>;
}

/**
 * Database configuration for SQL Server connection
 * Configuration matches Windows service appsettings.Development.json
 */
const dbConfig: sql.config = {
	server: process.env.DB_SERVER || "localhost",
	database: process.env.DB_DATABASE || "CalendarSync",
	options: {
		trustedConnection: process.env.DB_TRUSTED_CONNECTION === "true" || true,
		encrypt: process.env.DB_ENCRYPT === "true" || false,
		trustServerCertificate: process.env.DB_TRUST_SERVER_CERTIFICATE === "true" || true,
	},
	connectionTimeout: parseInt(process.env.DB_CONNECTION_TIMEOUT || "30000"),
	requestTimeout: parseInt(process.env.DB_REQUEST_TIMEOUT || "30000"),
	pool: {
		max: parseInt(process.env.DB_POOL_MAX || "10"),
		min: parseInt(process.env.DB_POOL_MIN || "0"),
		idleTimeoutMillis: parseInt(process.env.DB_POOL_IDLE_TIMEOUT || "30000"),
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
			console.log("Database already connected");
			return;
		}

		try {
			console.log("Initializing database connection...");
			this.pool = new sql.ConnectionPool(dbConfig);

			this.pool.on("connect", () => {
				console.log("Database connected successfully");
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
				console.log(`Retrying connection in 5 seconds...`);
				setTimeout(() => this.initialize(), 5000);
			} else {
				throw new Error(
					`Failed to connect to database after ${this.maxRetries} attempts`
				);
			}
		}
	}

	/**
	 * Get connection status information
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
			const result = await this.pool!.request().query("SELECT 1 as test");
			return result.recordset.length > 0;
		} catch (error) {
			console.error("Connection test failed:", error);
			return false;
		}
	}

	/**
	 * Get paginated events from database
	 */
	async getEvents(offset: number, limit: number): Promise<CalendarEvent[]> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			request.input("offset", sql.Int, offset);
			request.input("limit", sql.Int, limit);

			const result = await request.query(`
				SELECT 
					EventID, CalendarID, Summary, Description, StartTime, EndTime, 
					CreatedTime, UpdatedTime, Location, Status, OrganizerEmail, 
					Attendees, Recurrence
				FROM CalendarEvents 
				ORDER BY StartTime DESC
				OFFSET @offset ROWS
				FETCH NEXT @limit ROWS ONLY
			`);

			return result.recordset.map((row) => this.mapRowToEvent(row));
		} catch (error) {
			console.error("Error fetching events:", error);
			throw error;
		}
	}

	/**
	 * Get total count of events
	 */
	async getEventsCount(): Promise<number> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const result = await this.pool!
				.request()
				.query("SELECT COUNT(*) as count FROM CalendarEvents");

			return result.recordset[0].count;
		} catch (error) {
			console.error("Error getting events count:", error);
			throw error;
		}
	}

	/**
	 * Get event by ID
	 */
	async getEventById(eventId: string): Promise<CalendarEvent | null> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			request.input("eventId", sql.NVarChar, eventId);

			const result = await request.query(`
				SELECT 
					EventID, CalendarID, Summary, Description, StartTime, EndTime, 
					CreatedTime, UpdatedTime, Location, Status, OrganizerEmail, 
					Attendees, Recurrence
				FROM CalendarEvents 
				WHERE EventID = @eventId
			`);

			if (result.recordset.length === 0) {
				return null;
			}

			return this.mapRowToEvent(result.recordset[0]);
		} catch (error) {
			console.error("Error fetching event by ID:", error);
			throw error;
		}
	}

	/**
	 * Search events by query
	 */
	async searchEvents(query: string): Promise<CalendarEvent[]> {
		try {
			if (!this.pool || !this.isConnected) {
				await this.initialize();
			}

			const request = this.pool!.request();
			request.input("query", sql.NVarChar, `%${query}%`);

			const result = await request.query(`
				SELECT 
					EventID, CalendarID, Summary, Description, StartTime, EndTime, 
					CreatedTime, UpdatedTime, Location, Status, OrganizerEmail, 
					Attendees, Recurrence
				FROM CalendarEvents 
				WHERE (
					Summary LIKE @query 
					OR Description LIKE @query 
					OR Location LIKE @query
				)
				ORDER BY StartTime DESC
			`);

			return result.recordset.map((row) => this.mapRowToEvent(row));
		} catch (error) {
			console.error("Error searching events:", error);
			throw error;
		}
	}

	/**
	 * Map database row to CalendarEvent object
	 */
	private mapRowToEvent(row: any): CalendarEvent {
		return {
			eventID: row.EventID,
			calendarID: row.CalendarID,
			summary: row.Summary,
			description: row.Description,
			startTime: new Date(row.StartTime),
			endTime: new Date(row.EndTime),
			createdTime: new Date(row.CreatedTime),
			updatedTime: new Date(row.UpdatedTime),
			location: row.Location,
			status: row.Status,
			organizerEmail: row.OrganizerEmail,
			attendees: row.Attendees, // Keep as string (JSON format)
			recurrence: row.Recurrence, // Keep as string (JSON format)
		};
	}

	/**
	 * Close database connection
	 */
	async close(): Promise<void> {
		if (this.pool) {
			try {
				await this.pool.close();
				this.isConnected = false;
				console.log("Database connection closed");
			} catch (error) {
				console.error("Error closing database connection:", error);
			}
		}
	}
}

export const databaseService = new SqlServerDatabaseService();