/*
  IntelligenceDB - ADMIN user (optional, for control actions only)
  Platform: Azure SQL Database

  Used ONLY for actions a user explicitly confirms or approves. Every use is audited.
  Azure SQL Database has no SQL Agent and no instance settings, so only these actions apply:
  kill a session in this database, index and statistics maintenance.

  Two parts: connect to the right database before each part.
*/

/* PART 1 - connect to [master] (skip when using Entra ID or a contained user) */
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'IntelligenceDB_Admin')
    CREATE LOGIN [IntelligenceDB_Admin] WITH PASSWORD = N'<<CHANGE_ME_Use_A_Different_Strong_Password_2!>>';
GO

/* PART 2 - connect to EACH user database IntelligenceDB should maintain */
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_Admin')
    CREATE USER [IntelligenceDB_Admin] FOR LOGIN [IntelligenceDB_Admin];
GO

-- Entra ID alternative (recommended): CREATE USER [intelligencedb-admin-app] FROM EXTERNAL PROVIDER;

GRANT VIEW DATABASE STATE        TO [IntelligenceDB_Admin];
GRANT VIEW DEFINITION            TO [IntelligenceDB_Admin];
GRANT KILL DATABASE CONNECTION   TO [IntelligenceDB_Admin];
ALTER ROLE [db_ddladmin] ADD MEMBER [IntelligenceDB_Admin];
GO

PRINT N'IntelligenceDB admin user is ready in this database.';
