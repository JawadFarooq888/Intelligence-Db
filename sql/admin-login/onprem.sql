/*
  IntelligenceDB - ADMIN login (optional, for control actions only)
  Platform: SQL Server on-premises / VM (2016 and newer)

  IntelligenceDB uses this login ONLY for actions a user explicitly confirms or approves
  (start/stop jobs, kill a session, index maintenance, ...). Every use is written to the audit log.
  Destructive actions (DROP INDEX, KILL, config change, job delete) are never run automatically.

  Least-privilege grants:
    VIEW SERVER STATE, VIEW ANY DEFINITION, CONNECT ANY DATABASE -> see what it acts on
    ALTER ANY CONNECTION   -> KILL a session
    ALTER SETTINGS         -> sp_configure changes (always behind approval)
    msdb SQLAgentOperatorRole -> start / stop / enable / disable jobs
    db_ddladmin in each user database -> ALTER INDEX, UPDATE STATISTICS
  Note: editing the schedule of a job owned by another login requires sysadmin (see the end of the script).

  How to use: replace the password, run the whole script as a sysadmin. Safe to run more than once.
  Re-run it after creating new databases so db_ddladmin is granted there too.
*/
USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_Admin')
BEGIN
    CREATE LOGIN [IntelligenceDB_Admin]
        WITH PASSWORD = N'<<CHANGE_ME_Use_A_Different_Strong_Password_2!>>',
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF,
             DEFAULT_DATABASE = [master];
END;
GO

GRANT VIEW SERVER STATE    TO [IntelligenceDB_Admin];
GRANT VIEW ANY DEFINITION  TO [IntelligenceDB_Admin];
GRANT VIEW ANY DATABASE    TO [IntelligenceDB_Admin];
GRANT CONNECT ANY DATABASE TO [IntelligenceDB_Admin];
GRANT ALTER ANY CONNECTION TO [IntelligenceDB_Admin];
GRANT ALTER SETTINGS       TO [IntelligenceDB_Admin];
GO

USE [msdb];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_Admin')
    CREATE USER [IntelligenceDB_Admin] FOR LOGIN [IntelligenceDB_Admin];
GO

ALTER ROLE [SQLAgentOperatorRole] ADD MEMBER [IntelligenceDB_Admin];
ALTER ROLE [db_datareader]        ADD MEMBER [IntelligenceDB_Admin];
GO

-- db_ddladmin in every online, writable user database (index and statistics maintenance).
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
        ALTER ROLE [db_ddladmin] ADD MEMBER [IntelligenceDB_Admin];';
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

/*
  Optional - simpler but much more powerful:
  sysadmin can do everything above plus edit any job's schedule. Only use it if your policy allows.

  ALTER SERVER ROLE [sysadmin] ADD MEMBER [IntelligenceDB_Admin];
*/

PRINT N'IntelligenceDB admin login is ready. Add it as the "Admin login" of the server in IntelligenceDB.';
