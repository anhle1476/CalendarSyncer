/**
 * UDP Message types and interfaces
 */
export type UdpMessageType = 'EVENT_CHANGE' | 'SYNC_STATUS';

/**
 * Event change message structure
 */
export interface EventChangeMessage {
  changeType: 'created' | 'updated' | 'deleted';
  eventId: string;
  timestamp: string;
}

/**
 * Sync status message structure
 */
export interface SyncStatusMessage {
  status: 'started' | 'completed' | 'failed';
  calendarId: string;
  eventCount: number;
  timestamp: string;
}

/**
 * Main UDP message interface
 */
export interface UdpMessage {
  type: UdpMessageType;
  timestamp: Date;
  rawMessage: string;
  parsed: EventChangeMessage | SyncStatusMessage;
}