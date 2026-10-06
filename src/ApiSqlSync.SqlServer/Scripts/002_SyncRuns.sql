SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SyncRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SyncRuns
    (
        RunId uniqueidentifier NOT NULL,
        JobId nvarchar(128) NOT NULL,
        Runner nvarchar(32) NOT NULL,

        StartedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_SyncRuns_StartedAtUtc
            DEFAULT SYSUTCDATETIME(),

        FinishedAtUtc datetime2(7) NULL,

        Status nvarchar(16) NOT NULL,

        ProcessedCount bigint NULL,
        ErrorType nvarchar(256) NULL,

        CONSTRAINT PK_SyncRuns
            PRIMARY KEY (RunId),

        CONSTRAINT CK_SyncRuns_Status
            CHECK
            (
                Status IN
                (
                    N'Running',
                    N'Succeeded',
                    N'Failed',
                    N'Cancelled'
                )
            ),

        CONSTRAINT CK_SyncRuns_ProcessedCount
            CHECK
            (
                ProcessedCount IS NULL
                OR ProcessedCount >= 0
            ),

        CONSTRAINT CK_SyncRuns_FinishedAtUtc
            CHECK
            (
                (Status = N'Running' AND FinishedAtUtc IS NULL)
                OR
                (Status <> N'Running' AND FinishedAtUtc IS NOT NULL)
            )
    );

    -- Bir işin son turlarını okumak için kullanacağız.
    CREATE INDEX IX_SyncRuns_JobId_StartedAtUtc
        ON dbo.SyncRuns (JobId, StartedAtUtc DESC);
END;