import React, { createContext, useContext, useReducer, ReactNode } from 'react';
import { AppState, CalendarEvent, ConnectionStatus } from '../types/CalendarEvent';
import { UdpMessage } from '../types/UdpMessage';

/**
 * Action types for state management
 */
type AppAction =
  | { type: 'SET_LOADING'; payload: boolean }
  | { type: 'SET_ERROR'; payload: string | null }
  | { type: 'SET_EVENTS'; payload: CalendarEvent[] }
  | { type: 'ADD_EVENT'; payload: CalendarEvent }
  | { type: 'UPDATE_EVENT'; payload: CalendarEvent }
  | { type: 'DELETE_EVENT'; payload: string }
  | { type: 'SET_CONNECTION_STATUS'; payload: ConnectionStatus }
  | { type: 'ADD_UDP_MESSAGE'; payload: UdpMessage };

/**
 * Extended app state including UDP messages
 */
interface ExtendedAppState extends AppState {
  udpMessages: UdpMessage[];
}

/**
 * Initial state
 */
const initialState: ExtendedAppState = {
  events: [],
  udpMessages: [],
  connectionStatus: {
    database: false,
    udpListener: false,
  },
  loading: false,
  error: null,
};

/**
 * State reducer
 */
function appReducer(state: ExtendedAppState, action: AppAction): ExtendedAppState {
  switch (action.type) {
    case 'SET_LOADING':
      return { ...state, loading: action.payload };
    
    case 'SET_ERROR':
      return { ...state, error: action.payload };
    
    case 'SET_EVENTS':
      return { ...state, events: action.payload };
    
    case 'ADD_EVENT':
      return { ...state, events: [action.payload, ...state.events] };
    
    case 'UPDATE_EVENT':
      return {
        ...state,
        events: state.events.map(event =>
          event.eventID === action.payload.eventID ? action.payload : event
        ),
      };
    
    case 'DELETE_EVENT':
      return {
        ...state,
        events: state.events.filter(event => event.eventID !== action.payload),
      };
    
    case 'SET_CONNECTION_STATUS':
      return { ...state, connectionStatus: action.payload };
    
    case 'ADD_UDP_MESSAGE':
      // Keep only last 1000 messages (circular buffer)
      return {
        ...state,
        udpMessages: [action.payload, ...state.udpMessages].slice(0, 1000),
      };
      
    default:
      return state;
  }
}

/**
 * Context type
 */
interface AppContextType {
  state: ExtendedAppState;
  dispatch: React.Dispatch<AppAction>;
}

/**
 * Create context
 */
const AppContext = createContext<AppContextType | undefined>(undefined);

/**
 * Context provider component
 */
interface AppProviderProps {
  children: ReactNode;
}

export function AppProvider({ children }: AppProviderProps) {
  const [state, dispatch] = useReducer(appReducer, initialState);

  return (
    <AppContext.Provider value={{ state, dispatch }}>
      {children}
    </AppContext.Provider>
  );
}

/**
 * Custom hook to use app context
 */
export function useAppContext() {
  const context = useContext(AppContext);
  if (context === undefined) {
    throw new Error('useAppContext must be used within an AppProvider');
  }
  return context;
}