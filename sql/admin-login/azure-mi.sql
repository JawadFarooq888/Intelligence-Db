/*
  IntelligenceDB - ADMIN login (optional, for control actions only)
  Platform: Azure SQL Managed Instance

  Used ONLY for actions a user explicitly confirms or approves. Every use is audited.
  Same least-privilege model as on-premises (see sql/admin-login/onprem.sql for the explanation).
*/
USE [master];
GO

-- Option A: SQL authentication
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_Admin')
BEGIN
    CREATE LOGIN [IntelligenceDB_Admin]
        WITH PASSWORD = N'<<CHANGE_ME_Use_A_Different_Strong_Password_2!>>',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = [master];
END;
GO

-- Option B: Microsoft Entra ID (replace [IntelligenceDB_Admin] below with the Entra name):
-- CREATE LOGIN [intelligencedb-admin-app] FROM EXTERNAL PROVIDER;

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

PRINT N'IntelligenceDB admin login is ready. Add it as the "Admin login" of the server in IntelligenceDB.';
