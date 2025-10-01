import React, { useState, useEffect, useCallback } from 'react';
import {
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TablePagination,
  TextField,
  Box,
  Typography,
  CircularProgress,
  Alert,
  IconButton,
  Tooltip,
} from '@mui/material';
import { OpenInNew, Refresh } from '@mui/icons-material';
import { useAppContext } from '../context/AppContext';
import { CalendarEvent } from '../types/CalendarEvent';

// Access the electronAPI from the global window object
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



/**
 * Events table component with real database integration
 */
export function EventsTable() {
  const { state, dispatch } = useAppContext();
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(25);
  const [searchQuery, setSearchQuery] = useState('');
  const [totalCount, setTotalCount] = useState(0);
  const [isSearching, setIsSearching] = useState(false);

  /**
   * Load events from database
   */
  const loadEvents = useCallback(async (offset = 0, limit = 25, search = '') => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      // Test database connection first
      const isConnected = await window.electronAPI.database.testConnection();
      dispatch({ 
        type: 'SET_CONNECTION_STATUS', 
        payload: { 
          ...state.connectionStatus, 
          database: isConnected 
        } 
      });

      if (!isConnected) {
        throw new Error('Database connection failed');
      }

      let events: CalendarEvent[];
      let count: number;

      if (search.trim()) {
        // Search mode
        events = await window.electronAPI.database.searchEvents(search);
        count = events.length;
        // For search, we show all results and handle pagination client-side
        setTotalCount(count);
        dispatch({ type: 'SET_EVENTS', payload: events });
      } else {
        // Normal pagination mode
        events = await window.electronAPI.database.getEvents(offset, limit);
        count = await window.electronAPI.database.getEventsCount();
        setTotalCount(count);
        dispatch({ type: 'SET_EVENTS', payload: events });
      }

    } catch (error) {
      console.error('Error loading events:', error);
      dispatch({ type: 'SET_ERROR', payload: `Failed to load events: ${error}` });
      dispatch({ 
        type: 'SET_CONNECTION_STATUS', 
        payload: { 
          ...state.connectionStatus, 
          database: false 
        } 
      });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, [dispatch, state.connectionStatus]);

  /**
   * Initialize database connection and load initial data
   */
  useEffect(() => {
    const initializeDatabase = async () => {
      try {
        // No need to initialize database service in renderer process
        // The main process handles database initialization
        await loadEvents(0, rowsPerPage);
      } catch (error) {
        console.error('Failed to load events:', error);
        dispatch({ type: 'SET_ERROR', payload: 'Failed to connect to database. Please check your connection settings.' });
      }
    };

    initializeDatabase();
  }, [loadEvents, rowsPerPage]);

  /**
   * Handle search with debouncing
   */
  useEffect(() => {
    const timeoutId = setTimeout(() => {
      if (searchQuery !== '') {
        setIsSearching(true);
        loadEvents(0, rowsPerPage, searchQuery).finally(() => {
          setIsSearching(false);
        });
      } else {
        // Reset to normal pagination when search is cleared
        setIsSearching(false);
        loadEvents(page * rowsPerPage, rowsPerPage);
      }
      setPage(0); // Reset to first page when searching
    }, 500); // 500ms debounce

    return () => clearTimeout(timeoutId);
  }, [searchQuery, loadEvents, page, rowsPerPage]);

  /**
   * Handle page change
   */
  const handleChangePage = useCallback((_event: unknown, newPage: number) => {
    setPage(newPage);
    if (!searchQuery) {
      // Only load new data for normal pagination, not for search results
      loadEvents(newPage * rowsPerPage, rowsPerPage);
    }
  }, [loadEvents, rowsPerPage, searchQuery]);

  /**
   * Handle rows per page change
   */
  const handleChangeRowsPerPage = useCallback((event: React.ChangeEvent<HTMLInputElement>) => {
    const newRowsPerPage = parseInt(event.target.value, 10);
    setRowsPerPage(newRowsPerPage);
    setPage(0);
    if (!searchQuery) {
      loadEvents(0, newRowsPerPage);
    }
  }, [loadEvents, searchQuery]);

  /**
   * Open event in Google Calendar
   */
  const openInGoogleCalendar = useCallback((event: CalendarEvent) => {
    const startDate = event.startTime.toISOString().replace(/[-:]/g, '').split('.')[0] + 'Z';
    const endDate = event.endTime.toISOString().replace(/[-:]/g, '').split('.')[0] + 'Z';
    const url = `https://calendar.google.com/calendar/render?action=TEMPLATE&text=${encodeURIComponent(event.summary)}&dates=${startDate}/${endDate}&details=${encodeURIComponent(event.description)}&location=${encodeURIComponent(event.location || '')}`;
    window.open(url, '_blank');
  }, []);

  /**
   * Format date for display
   */
  const formatDate = useCallback((date: Date) => {
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);
  }, []);

  /**
   * Handle refresh button click
   */
  const handleRefresh = useCallback(async () => {
    await loadEvents(searchQuery ? 0 : page * rowsPerPage, rowsPerPage, searchQuery);
  }, [loadEvents, page, rowsPerPage, searchQuery]);

  // Get events to display (handle search pagination client-side)
  const eventsToDisplay = searchQuery 
    ? state.events.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)
    : state.events;

  return (
    <Paper sx={{ width: '100%', height: '100%', display: 'flex', flexDirection: 'column' }}>
      {/* Header */}
      <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6">Calendar Events</Typography>
          <Tooltip title="Refresh Events">
            <IconButton onClick={handleRefresh} disabled={state.loading}>
              <Refresh />
            </IconButton>
          </Tooltip>
        </Box>
        
        {/* Search */}
        <TextField
          fullWidth
          variant="outlined"
          placeholder="Search events by title, description, location, or organizer..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          sx={{ mb: 2 }}
          InputProps={{
            endAdornment: isSearching && <CircularProgress size={20} />
          }}
        />
        
        {/* Status */}
        {state.error && (
          <Alert severity="error" sx={{ mb: 1 }}>
            {state.error}
          </Alert>
        )}
      </Box>

      {/* Loading */}
      {state.loading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 3 }}>
          <CircularProgress />
        </Box>
      )}

      {/* Table */}
      {!state.loading && (
        <>
          <TableContainer sx={{ flexGrow: 1 }}>
            <Table stickyHeader>
              <TableHead>
                <TableRow>
                  <TableCell>Summary</TableCell>
                  <TableCell>Start Time</TableCell>
                  <TableCell>End Time</TableCell>
                  <TableCell>Location</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Organizer</TableCell>
                  <TableCell align="center">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {eventsToDisplay.map((event) => (
                  <TableRow key={event.eventID} hover>
                    <TableCell>
                      <Typography variant="body2" fontWeight="medium">
                        {event.summary}
                      </Typography>
                      {event.description && (
                        <Typography variant="caption" color="text.secondary">
                          {event.description.length > 50
                            ? `${event.description.substring(0, 50)}...`
                            : event.description}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell>{formatDate(event.startTime)}</TableCell>
                    <TableCell>{formatDate(event.endTime)}</TableCell>
                    <TableCell>{event.location || '-'}</TableCell>
                    <TableCell>
                      <Typography
                        variant="caption"
                        sx={{
                          px: 1,
                          py: 0.5,
                          borderRadius: 1,
                          backgroundColor: event.status === 'confirmed' ? 'success.light' : 'warning.light',
                          color: event.status === 'confirmed' ? 'success.dark' : 'warning.dark',
                        }}
                      >
                        {event.status}
                      </Typography>
                    </TableCell>
                    <TableCell>{event.organizerEmail || '-'}</TableCell>
                    <TableCell align="center">
                      <Tooltip title="Open in Google Calendar">
                        <IconButton
                          size="small"
                          onClick={() => openInGoogleCalendar(event)}
                        >
                          <OpenInNew fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          {/* Pagination */}
          <TablePagination
            rowsPerPageOptions={[10, 25, 50, 100]}
            component="div"
            count={searchQuery ? state.events.length : totalCount}
            rowsPerPage={rowsPerPage}
            page={page}
            onPageChange={handleChangePage}
            onRowsPerPageChange={handleChangeRowsPerPage}
          />
        </>
      )}
    </Paper>
  );
}