/*
  IntelligenceDB - ADMIN login (optional, for control actions only)
  Platform: Google Cloud SQL for SQL Server

  Run as the built-in "sqlserver" user. Used ONLY for actions a user explicitly confirms or approves;
  every use is audited. Instance settings are managed with Cloud SQL database flags, so ALTER SETTINGS
  is not granted. Each grant is wrapped in TRY/CATCH and prints a message if Cloud SQL refuses it.
*/
USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_Admin')
BEGIN
    CREATE LOGIN [IntelligenceDB_Admin]
        WITH PASSWORD = N'<<CHANGE_ME_Use_A_Different_Strong_Password_2!>>',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = [master];
END;
GO

BEGIN TRY GRANT VIEW SERVER STATE    TO [IntelligenceDB_Admin]; END TRY
BEGIN CATCH PRINT N'VIEW SERVER STATE not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY GRANT VIEW ANY DEFINITION  TO [IntelligenceDB_Admin]; END TRY
BEGIN CATCH PRINT N'VIEW ANY DEFINITION not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY GRANT ALTER ANY CONNECTION TO [IntelligenceDB_Admin]; END TRY
BEGIN CATCH PRINT N'ALTER ANY CONNECTION not granted (kill session will be unavailable): ' + ERROR_MESSAGE(); END CATCH;
GO

USE [msdb];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_Admin')
    CREATE USER [IntelligenceDB_Admin] FOR LOGIN [IntelligenceDB_Admin];
GO

BEGIN TRY ALTER ROLE [SQLAgentOperatorRole] ADD MEMBER [IntelligenceDB_Admin]; END TRY
BEGIN CATCH PRINT N'SQLAgentOperatorRole not granted: ' + ERROR_MESSAGE(); END CATCH;

BEGIN TRY ALTER ROLE [db_datareader] ADD MEMBER [IntelligenceDB_Admin]; END TRY
BEGIN CATCH PRINT N'msdb db_datareader not granted: ' + ERROR_MESSAGE(); END CATCH;
GO

USE [master];
GO
DECLARE @db sysname, @sql nvarchar(max);
DECLARE dbs CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.databases
    WHERE database_id > 4 AND state_desc = N'ONLINE' AND is_read_only = 0;
OPEN dbs;
FETCH NEXT FROM dbs INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'USE ' + QUOTENAME(@db) + N';
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''IntelligenceDB_Admin'')
            CREATE USER [IntelligenceDB_Admin] FOR LOGIN [IntelligenceDB_Admin];
        ALTER ROLE [db_ddladmin] ADD MEMBER [IntelligenceDB_Admin];
        GRANT VIEW DATABASE STATE TO [IntelligenceDB_Admin];';
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
GO

PRINT N'Done. Add this login as the "Admin login" of the server in IntelligenceDB and run Test connection.';
