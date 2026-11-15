/*
  IntelligenceDB - MONITORING login (read-only)
  Platform: Azure SQL Database (single database and elastic pool)

  Azure SQL Database does not support USE <database>, so this script has TWO PARTS.
  Connect to the right database before running each part.

  In IntelligenceDB, add one server entry per database you want to monitor
  (host = <server>.database.windows.net, database = that database's name).

  Not available on Azure SQL Database (IntelligenceDB hides these features automatically):
  SQL Agent / msdb, backup history, disk volumes, error log, instance settings.
*/

/* =====================================================================
   PART 1 - connect to the [master] database of the logical server
   ===================================================================== */

-- Option A: SQL authentication login
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'IntelligenceDB_Monitor')
    CREATE LOGIN [IntelligenceDB_Monitor] WITH PASSWORD = N'<<CHANGE_ME_Use_A_Strong_Password_1!>>';
GO

-- Server-level read-only roles (built-in Azure SQL Database server roles).
ALTER SERVER ROLE [##MS_ServerStateReader##] ADD MEMBER [IntelligenceDB_Monitor];
ALTER SERVER ROLE [##MS_DefinitionReader##]  ADD MEMBER [IntelligenceDB_Monitor];
GO

/* =====================================================================
   PART 2 - connect to EACH user database you want to monitor
   ===================================================================== */

-- If you used Option A (login in master):
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_Monitor')
    CREATE USER [IntelligenceDB_Monitor] FOR LOGIN [IntelligenceDB_Monitor];
GO

-- Option B (recommended): Microsoft Entra ID - skip PART 1 and create the user directly here:
-- CREATE USER [intelligencedb-monitor-app] FROM EXTERNAL PROVIDER;
-- Option C: contained database user with a password (no login in master needed):
-- CREATE USER [IntelligenceDB_Monitor] WITH PASSWORD = N'<<CHANGE_ME_Use_A_Strong_Password_1!>>';

GRANT VIEW DATABASE STATE TO [IntelligenceDB_Monitor];
GRANT VIEW DEFINITION     TO [IntelligenceDB_Monitor];
GO

PRINT N'IntelligenceDB monitoring user is ready in this database. Use "Test connection" in IntelligenceDB to verify.';
