-- Grants managed identities access to the Needly database.
-- Azure SQL uses Microsoft Entra-only authentication, so these are contained users with no passwords.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'needly-prod-001')
    CREATE USER [needly-prod-001] FROM EXTERNAL PROVIDER;
GO
ALTER ROLE db_datareader ADD MEMBER [needly-prod-001];
GO
ALTER ROLE db_datawriter ADD MEMBER [needly-prod-001];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-needly-github-deploy')
    CREATE USER [id-needly-github-deploy] FROM EXTERNAL PROVIDER;
GO
-- The release workflow applies EF Core migrations, which requires schema ownership.
ALTER ROLE db_owner ADD MEMBER [id-needly-github-deploy];
GO
