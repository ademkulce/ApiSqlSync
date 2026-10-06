SET XACT_ABORT ON;

-- Farklı işler aynı kaynak ID'sini kullanabilir.
IF OBJECT_ID(N'dbo.SyncRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SyncRecords
    (
        JobId nvarchar(128) NOT NULL,
        Id bigint NOT NULL,
        UpdatedAt bigint NOT NULL,
        Payload nvarchar(max) NOT NULL,

        SyncedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_SyncRecords_SyncedAtUtc
            DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_SyncRecords
            PRIMARY KEY (JobId, Id),

        CONSTRAINT CK_SyncRecords_Payload
            CHECK (ISJSON(Payload) = 1)
    );
END;

-- İlerleme bilgisi, ilgili iş için ayrı tutulur.
IF OBJECT_ID(N'dbo.SyncCheckpoints', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SyncCheckpoints
    (
        JobId nvarchar(128) NOT NULL,
        UpdatedAt bigint NOT NULL,
        LastId bigint NOT NULL,

        SavedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_SyncCheckpoints_SavedAtUtc
            DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_SyncCheckpoints
            PRIMARY KEY (JobId)
    );
END;