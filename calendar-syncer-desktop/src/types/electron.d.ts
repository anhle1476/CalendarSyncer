import { CalendarEvent } from './CalendarEvent';

declare global {
  interface Window {
    electronAPI: {
      database: {
        getEvents: (offset: number, limit: number) => Promise<CalendarEvent[]>;
        getEventsCount: () => Promise<number>;
        getEventById: (eventId: string) => Promise<CalendarEvent | null>;
        searchEvents: (query: string) => Promise<CalendarEvent[]>;
        testConnection: () => Promise<boolean>;
      };
    };
  }
}

export {};