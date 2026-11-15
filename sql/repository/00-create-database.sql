/*
  IntelligenceDB - repository database (IntelligenceDB_Repo)

  Only needed if you do NOT want the API to create the database itself.
  (By default the API creates the database and all tables on first start using EF Core migrations,
   when its login is allowed to create databases.)

  Steps for a locked-down server:
    1. Run this script as a sysadmin (replace the password).
    2. Point ConnectionStrings__Repository at IntelligenceDB_Repo with the IntelligenceDB_App login.
    3. Start the API: it creates the schema (auth, inv, audit, ops, log) inside the existing database.
       Or generate a reviewable script instead:
         dotnet ef migrations script --idempotent \
           --project src/IntelligenceDB.Infrastructure --startup-project src/IntelligenceDB.Api \
           -o sql/repository/01-schema.sql
       and set Repository__AutoMigrate=false.
*/
USE [master];
GO

IF DB_ID(N'IntelligenceDB_Repo') IS NULL
BEGIN
    CREATE DATABASE [IntelligenceDB_Repo];
END;
GO

-- Readers never block writers (the collector writes constantly while the portal reads).
ALTER DATABASE [IntelligenceDB_Repo] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [IntelligenceDB_Repo] SET RECOVERY FULL;   -- back it up (see README: "Backing up the repository")
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IntelligenceDB_App')
BEGIN
    CREATE LOGIN [IntelligenceDB_App]
        WITH PASSWORD = N'<<CHANGE_ME_Repository_App_Password_3!>>',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = [IntelligenceDB_Repo];
END;
GO

USE [IntelligenceDB_Repo];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IntelligenceDB_App')
    CREATE USER [IntelligenceDB_App] FOR LOGIN [IntelligenceDB_App];
GO

-- db_ddladmin is needed for migrations (schema upgrades). Remove it after setup if you apply
-- migration scripts manually (Repository__AutoMigrate=false).
ALTER ROLE [db_ddladmin]   ADD MEMBER [IntelligenceDB_App];
ALTER ROLE [db_datareader] ADD MEMBER [IntelligenceDB_App];
ALTER ROLE [db_datawriter] ADD MEMBER [IntelligenceDB_App];
GO

PRINT N'IntelligenceDB_Repo is ready.';
