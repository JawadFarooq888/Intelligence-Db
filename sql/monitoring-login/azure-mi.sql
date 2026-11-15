/*
  IntelligenceDB - MONITORING login (read-only)
  Platform: Azure SQL Managed Instance

  Same permissions as on-premises: VIEW SERVER STATE, VIEW ANY DEFINITION, VIEW ANY DATABASE,
  CONNECT ANY DATABASE, and read-only msdb roles for Agent jobs and backup history.

  How to use:
    1. Choose SQL authentication (replace the password) OR Microsoft Entra ID (recommended).
    2. Run the whole script as an admin of the managed instance. Safe to run more than once.
*/
USE [master];
GO

-- Option A: SQL authentication
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_Monitor')
BEGIN
    CREATE LOGIN [IntelligenceDB_Monitor]
        WITH PASSWORD = N'<<CHANGE_ME_Use_A_Strong_Password_1!>>',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = [master];
END;
GO

-- Option B: Microsoft Entra ID (service principal, managed identity, user or group).
-- Use instead of Option A and replace [IntelligenceDB_Monitor] below with the Entra name:
-- CREATE LOGIN [intelligencedb-monitor-app] FROM EXTERNAL PROVIDER;

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

PRINT N'IntelligenceDB monitoring login is ready. Use "Test connection" in IntelligenceDB to verify.';
