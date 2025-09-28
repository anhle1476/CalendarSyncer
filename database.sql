-- Table of events
CREATE TABLE CalendarEvents (
    EventID NVARCHAR(100) PRIMARY KEY,
    CalendarID NVARCHAR(100),
    Summary NVARCHAR(500),
    Description NVARCHAR(MAX),
    StartTime DATETIME,
    EndTime DATETIME,
    CreatedTime DATETIME,
    UpdatedTime DATETIME,
    Location NVARCHAR(200),
    Status NVARCHAR(50),
    OrganizerEmail NVARCHAR(100),
    Attendees NVARCHAR(MAX),
    Recurrence NVARCHAR(MAX)
);

-- Table of sync state
CREATE TABLE CalendarSyncState (
    CalendarID NVARCHAR(100) PRIMARY KEY,
    SyncToken NVARCHAR(200),
    LastSyncTime DATETIME
);