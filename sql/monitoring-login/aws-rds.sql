/*
  IntelligenceDB - MONITORING login (read-only)
  Platform: Amazon RDS for SQL Server

  Run as the RDS MASTER USER (RDS does not give anyone sysadmin).
  Some grants depend on the RDS engine version; each one is wrapped in TRY/CATCH and prints
  a message instead of stopping. IntelligenceDB's "Test connection" shows exactly what works.
*/
USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_Monitor')
BEGIN
    CREATE LOGIN [IntelligenceDB_Monitor]
        WITH PASSWORD = N'<<CHANGE_ME_Use_A_Strong_Password_1!>>',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = [master];
END;
GO

BEGIN TRY GRANT VIEW SERVER STATE   TO [IntelligenceDB_Monitor]; END TRY
BEGIN CATCH PRINT N'VIEW SERVER STATE not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY GRANT VIEW ANY DEFINITION TO [IntelligenceDB_Monitor]; END TRY
BEGIN CATCH PRINT N'VIEW ANY DEFINITION not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY GRANT VIEW ANY DATABASE   TO [IntelligenceDB_Monitor]; END TRY
BEGIN CATCH PRINT N'VIEW ANY DATABASE not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY GRANT CONNECT ANY DATABASE TO [IntelligenceDB_Monitor]; END TRY
BEGIN CATCH PRINT N'CONNECT ANY DATABASE not granted (set @CreateDatabaseUsers = 1 below instead): ' + ERROR_MESSAGE(); END CATCH;
GO

-- msdb: Agent jobs and backup history (read-only)
USE [msdb];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_Monitor')
    CREATE USER [IntelligenceDB_Monitor] FOR LOGIN [IntelligenceDB_Monitor];
GO

BEGIN TRY ALTER ROLE [SQLAgentReaderRole] ADD MEMBER [IntelligenceDB_Monitor]; END TRY
BEGIN CATCH PRINT N'SQLAgentReaderRole not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY ALTER ROLE [db_datareader] ADD MEMBER [IntelligenceDB_Monitor]; END TRY
BEGIN CATCH PRINT N'msdb db_datareader not granted: ' + ERROR_MESSAGE(); END CATCH;
GO

-- Optional: read the error log through the RDS procedure.
BEGIN TRY
    EXEC (N'USE [rdsadmin]; GRANT EXECUTE ON [dbo].[rds_read_error_log] TO [IntelligenceDB_Monitor];');
END TRY
BEGIN CATCH PRINT N'rds_read_error_log not granted (optional): ' + ERROR_MESSAGE(); END CATCH;
GO

/*
  Only needed if CONNECT ANY DATABASE could not be granted:
  create a read-only user in every online user database.
*/
USE [master];
GO
DECLARE @CreateDatabaseUsers bit = 0;   -- set to 1 to run

IF @CreateDatabaseUsers = 1
BEGIN
    DECLARE @db sysname, @sql nvarchar(max);
    DECLARE dbs CURSOR LOCAL FAST_FORWARD FOR
        SELECT name FROM sys.databases
        WHERE database_id > 4 AND name <> N'rdsadmin' AND state_desc = N'ONLINE' AND is_read_only = 0;
    OPEN dbs;
    FETCH NEXT FROM dbs INTO @db;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'USE ' + QUOTENAME(@db) + N';
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''IntelligenceDB_Monitor'')
                CREATE USER [IntelligenceDB_Monitor] FOR LOGIN [IntelligenceDB_Monitor];
            GRANT VIEW DATABASE STATE TO [IntelligenceDB_Monitor];';
        BEGIN TRY
            EXEC sys.sp_executesql @sql;
        END TRY
        BEGIN CATCH
            PRINT N'Database ' + @db + N': ' + ERROR_MESSAGE();
        END CATCH;
        FETCH NEXT FROM dbs INTO @db;
    END;
    CLOSE dbs;
    DEALLOCATE dbs;
END;
GO

PRINT N'Done. Use "Test connection" in IntelligenceDB to see which features are available.';
