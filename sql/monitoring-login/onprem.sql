/*
  IntelligenceDB - MONITORING login (read-only)
  Platform: SQL Server on-premises / VM (2016 and newer, Windows or Linux)

  What it grants:
    VIEW SERVER STATE       -> performance DMVs (waits, sessions, CPU, memory, I/O)
    VIEW ANY DEFINITION     -> object/index metadata (no data access)
    VIEW ANY DATABASE       -> see all databases in sys.databases
    CONNECT ANY DATABASE    -> read database-level DMVs (index usage, Query Store) without a user per database
    msdb SQLAgentReaderRole + db_datareader -> Agent jobs and backup history
  It does NOT grant: reading user table data, changing anything, sysadmin.

  How to use:
    1. Replace the password (or use the Windows / gMSA variant and change the login name everywhere).
    2. Run the whole script as a sysadmin. Safe to run more than once.
*/
USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_Monitor')
BEGIN
    CREATE LOGIN [IntelligenceDB_Monitor]
        WITH PASSWORD = N'<<CHANGE_ME_Use_A_Strong_Password_1!>>',
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF,
             DEFAULT_DATABASE = [master];
END;
GO

-- Windows / gMSA alternative (use instead of the block above, then replace the login name below):
-- CREATE LOGIN [CONTOSO\gmsa-intelligencedb$] FROM WINDOWS WITH DEFAULT_DATABASE = [master];

GRANT VIEW SERVER STATE    TO [IntelligenceDB_Monitor];
GRANT VIEW ANY DEFINITION  TO [IntelligenceDB_Monitor];
GRANT VIEW ANY DATABASE    TO [IntelligenceDB_Monitor];
GRANT CONNECT ANY DATABASE TO [IntelligenceDB_Monitor];
GO

USE [msdb];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_Monitor')
    CREATE USER [IntelligenceDB_Monitor] FOR LOGIN [IntelligenceDB_Monitor];
GO

ALTER ROLE [SQLAgentReaderRole] ADD MEMBER [IntelligenceDB_Monitor];
ALTER ROLE [db_datareader]      ADD MEMBER [IntelligenceDB_Monitor];
GO

/*
  Optional: allow IntelligenceDB to read the SQL Server error log.
  securityadmin is a powerful role (it can grant server permissions), so only enable this if you accept that.

  ALTER SERVER ROLE [securityadmin] ADD MEMBER [IntelligenceDB_Monitor];
*/

PRINT N'IntelligenceDB monitoring login is ready. Use "Test connection" in IntelligenceDB to verify.';
