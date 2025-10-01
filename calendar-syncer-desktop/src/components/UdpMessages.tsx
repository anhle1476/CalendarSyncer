import { Clear, FileDownload, Pause, PlayArrow } from "@mui/icons-material";
import {
	Box,
	Chip,
	FormControl,
	IconButton,
	InputLabel,
	List,
	ListItem,
	ListItemText,
	MenuItem,
	Paper,
	Select,
	Tooltip,
	Typography,
} from "@mui/material";
import { useEffect, useRef, useState } from "react";
import { useAppContext } from "../context/AppContext";
import { UdpMessage, UdpMessageType } from "../types/UdpMessage";

/**
 * Mock UDP messages for Phase 1 testing
 */
const mockUdpMessages: UdpMessage[] = [
	{
		type: "EVENT_CHANGE",
		timestamp: new Date(),
		rawMessage: "EVENT_CHANGE|updated|1|2024-01-15T14:30:25Z",
		parsed: {
			changeType: "updated",
			eventId: "1",
			timestamp: "2024-01-15T14:30:25Z",
		},
	},
	{
		type: "SYNC_STATUS",
		timestamp: new Date(Date.now() - 5000),
		rawMessage: "SYNC_STATUS|completed|primary|5|2024-01-15T14:30:20Z",
		parsed: {
			status: "completed",
			calendarId: "primary",
			eventCount: 5,
			timestamp: "2024-01-15T14:30:20Z",
		},
	},
	{
		type: "EVENT_CHANGE",
		timestamp: new Date(Date.now() - 70000),
		rawMessage: "EVENT_CHANGE|created|3|2024-01-15T14:29:15Z",
		parsed: {
			changeType: "created",
			eventId: "3",
			timestamp: "2024-01-15T14:29:15Z",
		},
	},
];

/**
 * UDP Messages component
 */
export function UdpMessages() {
	const { state, dispatch } = useAppContext();
	const [filter, setFilter] = useState<UdpMessageType | "all">("all");
	const [isPaused, setIsPaused] = useState(false);
	const listRef = useRef<HTMLDivElement>(null);

	// Initialize with mock data for Phase 1
	useEffect(() => {
		mockUdpMessages.forEach((message) => {
			dispatch({ type: "ADD_UDP_MESSAGE", payload: message });
		});
	}, [dispatch]);

	// Auto-scroll to latest messages (when not paused)
	useEffect(() => {
		if (!isPaused && listRef.current) {
			listRef.current.scrollTop = 0;
		}
	}, [state.udpMessages, isPaused]);

	/**
	 * Filter messages based on selected type
	 */
	const filteredMessages = state.udpMessages.filter(
		(message) => filter === "all" || message.type === filter
	);

	/**
	 * Clear all messages
	 */
	const handleClearMessages = () => {
		// In Phase 3, this will clear the actual UDP messages
		console.log("Clear messages - to be implemented in Phase 3");
	};

	/**
	 * Export messages to file
	 */
	const handleExportMessages = () => {
		const data = filteredMessages.map((msg) => ({
			timestamp: msg.timestamp.toISOString(),
			type: msg.type,
			rawMessage: msg.rawMessage,
		}));

		const blob = new Blob([JSON.stringify(data, null, 2)], {
			type: "application/json",
		});
		const url = URL.createObjectURL(blob);
		const a = document.createElement("a");
		a.href = url;
		a.download = `udp-messages-${new Date().toISOString().split("T")[0]}.json`;
		document.body.appendChild(a);
		a.click();
		document.body.removeChild(a);
		URL.revokeObjectURL(url);
	};

	/**
	 * Format message for display
	 */
	const formatMessage = (message: UdpMessage): string => {
		const time = message.timestamp.toLocaleTimeString("en-US", {
			hour12: false,
			hour: "2-digit",
			minute: "2-digit",
			second: "2-digit",
		});

		if (message.type === "EVENT_CHANGE") {
			const parsed = message.parsed as any;
			return `[${time}] EVENT_CHANGE: Event "${parsed.eventId}" ${parsed.changeType}`;
		} else if (message.type === "SYNC_STATUS") {
			const parsed = message.parsed as any;
			return `[${time}] SYNC_STATUS: Sync ${parsed.status} - ${parsed.eventCount} events processed`;
		}

		return `[${time}] ${message.rawMessage}`;
	};

	/**
	 * Get message color based on type
	 */
	const getMessageColor = (message: UdpMessage): string => {
		if (message.type === "EVENT_CHANGE") {
			const parsed = message.parsed as any;
			switch (parsed.changeType) {
				case "created":
					return "success.main";
				case "updated":
					return "warning.main";
				case "deleted":
					return "error.main";
				default:
					return "text.primary";
			}
		} else if (message.type === "SYNC_STATUS") {
			const parsed = message.parsed as any;
			switch (parsed.status) {
				case "completed":
					return "success.main";
				case "failed":
					return "error.main";
				case "started":
					return "info.main";
				default:
					return "text.primary";
			}
		}
		return "text.primary";
	};

	return (
		<Paper sx={{ height: "100%", display: "flex", flexDirection: "column" }}>
			{/* Header */}
			<Box sx={{ p: 2, borderBottom: 1, borderColor: "divider" }}>
				<Box
					sx={{
						display: "flex",
						justifyContent: "space-between",
						alignItems: "center",
						mb: 2,
					}}
				>
					<Typography variant="h6">UDP Messages</Typography>
					<Box sx={{ display: "flex", gap: 1 }}>
						<Tooltip
							title={isPaused ? "Resume Auto-scroll" : "Pause Auto-scroll"}
						>
							<IconButton
								size="small"
								onClick={() => setIsPaused(!isPaused)}
								color={isPaused ? "warning" : "default"}
							>
								{isPaused ? <PlayArrow /> : <Pause />}
							</IconButton>
						</Tooltip>
						<Tooltip title="Export Messages">
							<IconButton size="small" onClick={handleExportMessages}>
								<FileDownload />
							</IconButton>
						</Tooltip>
						<Tooltip title="Clear Messages">
							<IconButton size="small" onClick={handleClearMessages}>
								<Clear />
							</IconButton>
						</Tooltip>
					</Box>
				</Box>

				{/* Filter */}
				<FormControl size="small" sx={{ minWidth: 120 }}>
					<InputLabel>Filter</InputLabel>
					<Select
						value={filter}
						label="Filter"
						onChange={(e) =>
							setFilter(e.target.value as UdpMessageType | "all")
						}
					>
						<MenuItem value="all">All Messages</MenuItem>
						<MenuItem value="EVENT_CHANGE">Event Changes</MenuItem>
						<MenuItem value="SYNC_STATUS">Sync Status</MenuItem>
					</Select>
				</FormControl>

				{/* Stats */}
				<Box sx={{ display: "flex", gap: 1, mt: 1 }}>
					<Chip
						label={`Total: ${state.udpMessages.length}`}
						size="small"
						variant="outlined"
					/>
					<Chip
						label={`Filtered: ${filteredMessages.length}`}
						size="small"
						variant="outlined"
					/>
					{isPaused && <Chip label="Paused" size="small" color="warning" />}
				</Box>
			</Box>

			{/* Messages List */}
			<Box
				ref={listRef}
				sx={{
					flexGrow: 1,
					overflow: "auto",
					maxHeight: "calc(100% - 140px)",
				}}
			>
				{filteredMessages.length === 0 ? (
					<Box sx={{ p: 3, textAlign: "center" }}>
						<Typography color="text.secondary">
							No messages to display
						</Typography>
					</Box>
				) : (
					<List dense>
						{filteredMessages.map((message, index) => (
							<ListItem key={`${message.timestamp.getTime()}-${index}`}>
								<ListItemText
									primary={
										<Typography
											variant="body2"
											component="div"
											sx={{
												fontFamily: "monospace",
												fontSize: "0.875rem",
												color: getMessageColor(message),
											}}
										>
											{formatMessage(message)}
										</Typography>
									}
									secondary={
										<Typography
											variant="caption"
											sx={{
												fontFamily: "monospace",
												color: "text.secondary",
											}}
										>
											Raw: {message.rawMessage}
										</Typography>
									}
								/>
							</ListItem>
						))}
					</List>
				)}
			</Box>
		</Paper>
	);
}
