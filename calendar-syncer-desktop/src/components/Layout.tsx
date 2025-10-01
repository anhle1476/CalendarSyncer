import {
	Alert,
	AppBar,
	Box,
	Chip,
	LinearProgress,
	Snackbar,
	Toolbar,
	Typography,
} from "@mui/material";
import { green, red } from "@mui/material/colors";
import React from "react";
import { useAppContext } from "../context/AppContext";

/**
 * Layout component props
 */
interface LayoutProps {
	children: React.ReactNode;
}

/**
 * Main layout component with header and split view
 */
export function Layout({ children }: LayoutProps) {
	const { state, dispatch } = useAppContext();

	/**
	 * Handle error dismissal
	 */
	const handleErrorClose = () => {
		dispatch({ type: "SET_ERROR", payload: null });
	};

	return (
		<Box
			sx={{
				flexGrow: 1,
				height: "100vh",
				display: "flex",
				flexDirection: "column",
			}}
		>
			{/* Header */}
			<AppBar position="static" sx={{ backgroundColor: "#1976d2" }}>
				<Toolbar>
					<Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
						Calendar Sync Monitor
					</Typography>

					{/* Connection Status Indicators */}
					<Box sx={{ display: "flex", gap: 1 }}>
						<Chip
							label="Database"
							size="small"
							sx={{
								backgroundColor: state.connectionStatus.database
									? green[500]
									: red[500],
								color: "white",
							}}
						/>
						<Chip
							label="UDP Listener"
							size="small"
							sx={{
								backgroundColor: state.connectionStatus.udpListener
									? green[500]
									: red[500],
								color: "white",
							}}
						/>
					</Box>
				</Toolbar>

				{/* Loading Progress Bar */}
				{state.loading && (
					<LinearProgress
						sx={{
							position: "absolute",
							bottom: 0,
							left: 0,
							right: 0,
							height: 2,
						}}
					/>
				)}
			</AppBar>

			{/* Main Content */}
			<Box
				sx={{
					flexGrow: 1,
					p: 2,
					height: "calc(100vh - 64px)",
					width: "100%",
				}}
			>
				<Box sx={{ height: "100%", width: "100%" }}>{children}</Box>
			</Box>

			{/* Error Snackbar */}
			<Snackbar
				open={!!state.error}
				autoHideDuration={6000}
				onClose={handleErrorClose}
				anchorOrigin={{ vertical: "bottom", horizontal: "center" }}
			>
				<Alert
					onClose={handleErrorClose}
					severity="error"
					sx={{ width: "100%" }}
				>
					{state.error}
				</Alert>
			</Snackbar>
		</Box>
	);
}
