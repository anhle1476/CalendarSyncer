// See the Electron documentation for details on how to use preload scripts:
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts

import { contextBridge, ipcRenderer } from 'electron';
import { CalendarEvent } from './types/CalendarEvent';

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  database: {
    getEvents: (offset: number, limit: number): Promise<CalendarEvent[]> => 
      ipcRenderer.invoke('database:getEvents', offset, limit),
    getEventsCount: (): Promise<number> => 
      ipcRenderer.invoke('database:getEventsCount'),
    getEventById: (eventId: string): Promise<CalendarEvent | null> => 
      ipcRenderer.invoke('database:getEventById', eventId),
    searchEvents: (query: string): Promise<CalendarEvent[]> => 
      ipcRenderer.invoke('database:searchEvents', query),
    testConnection: (): Promise<boolean> => 
      ipcRenderer.invoke('database:testConnection'),
  }
});
