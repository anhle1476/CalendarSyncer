/**
 * Calendar Event interface based on the database schema
 */
export interface CalendarEvent {
  eventID: string;
  calendarID: string;
  summary: string;
  description: string;
  startTime: Date;
  endTime: Date;
  createdTime: Date;
  updatedTime: Date;
  location?: string;
  status: string;
  organizerEmail?: string;
  attendees?: string;
  recurrence?: string;
}

/**
 * Database connection status interface
 */
export interface ConnectionStatus {
  database: boolean;
  udpListener: boolean;
}

/**
 * Application state interface
 */
export interface AppState {
  events: CalendarEvent[];
  connectionStatus: ConnectionStatus;
  loading: boolean;
  error: string | null;
}