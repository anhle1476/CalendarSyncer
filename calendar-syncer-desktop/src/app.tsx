import React from 'react';
import { createRoot } from 'react-dom/client';
import { ThemeProvider, createTheme } from '@mui/material/styles';
import { CssBaseline, Box } from '@mui/material';
import { AppProvider } from './context/AppContext';
import { Layout } from './components/Layout';
import { EventsTable } from './components/EventsTable';
import { UdpMessages } from './components/UdpMessages';

// Create Material-UI theme
const theme = createTheme({
  palette: {
    mode: 'light',
    primary: {
      main: '#1976d2',
    },
    secondary: {
      main: '#dc004e',
    },
  },
});

// Main App component
function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AppProvider>
        <Layout>
          <Box sx={{ 
            display: 'flex', 
            gap: 2, 
            height: '100%',
            flexDirection: { xs: 'column', md: 'row' }
          }}>
            {/* Left panel - Events Table */}
            <Box sx={{ 
              flex: { xs: '1', md: '0 0 70%' },
              minHeight: { xs: '50vh', md: 'auto' }
            }}>
              <EventsTable />
            </Box>
            
            {/* Right panel - UDP Messages */}
            <Box sx={{ 
              flex: { xs: '1', md: '0 0 30%' },
              minHeight: { xs: '40vh', md: 'auto' }
            }}>
              <UdpMessages />
            </Box>
          </Box>
        </Layout>
      </AppProvider>
    </ThemeProvider>
  );
}

const root = createRoot(document.body);
root.render(<App />);