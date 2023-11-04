namespace BeyondImmersion.BannouService.Accounts;

public static class SqlScripts
{
    /// <summary>
    /// Add new user account.
    /// 
    /// Named Parameters:
    /// - @SecurityToken        string
    /// - @Email                string?
    /// - @EmailVerified        bool?
    /// - @TwoFactorEnabled     bool?
    /// - @Region               string?
    /// - @Username             string?
    /// - @PasswordData         string?
    /// - @GoogleUserId         string?
    /// - @GoogleData           string?
    /// - @SteamUserId          string?
    /// - @SteamData            string?
    /// </summary>
    public const string AddUser = @"
INSERT INTO `Users` (`Username`, `SecurityToken`, `Email`, `Region`, `EmailVerified`, `TwoFactorEnabled`)
VALUES (@Username, @SecurityToken, @Email, @Region, @EmailVerified, @TwoFactorEnabled);
SET @lastUserId := LAST_INSERT_ID();

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
SELECT 
    @lastUserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'),
    @Username,
    @PasswordData
WHERE @Username IS NOT NULL AND @PasswordData IS NOT NULL;

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
SELECT 
    @lastUserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Google'),
    @GoogleUserId,
    @GoogleData
WHERE @GoogleUserId IS NOT NULL AND @GoogleData IS NOT NULL;

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
SELECT 
    @lastUserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Steam'),
    @SteamUserId,
    @SteamData
WHERE @SteamUserId IS NOT NULL AND @SteamData IS NOT NULL;

SELECT * FROM `Users` WHERE `Id` = @lastUserId;";

    /// <summary>
    /// Add new user account, and stores any included claims.
    /// 
    /// Named Parameters:
    /// - @SecurityToken        string
    /// - @Email                string?
    /// - @EmailVerified        bool?
    /// - @TwoFactorEnabled     bool?
    /// - @Region               string?
    /// - @Username             string?
    /// - @PasswordData         string?
    /// - @GoogleUserId         string?
    /// - @GoogleData           string?
    /// - @SteamUserId          string?
    /// - @SteamData            string?
    /// - @RoleClaims           (string,string,...)?
    /// - @AppClaims            (string,string,...)?
    /// - @ScopeClaims          (string,string,...)?
    /// - @IdentityClaims       (string,string,...)?
    /// - @ProfileClaims        (string,string,...)?
    /// </summary>
    public const string AddUser_WithClaims = @"
INSERT INTO `Users` (`Username`, `SecurityToken`, `Email`, `Region`, `EmailVerified`, `TwoFactorEnabled`)
VALUES (@Username, @SecurityToken, @Email, @Region, @EmailVerified, @TwoFactorEnabled);
SET @lastUserId := LAST_INSERT_ID();

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
SELECT
    @lastUserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'),
    @Username,
    @PasswordData
WHERE @Username IS NOT NULL AND @PasswordData IS NOT NULL;

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
SELECT
    @lastUserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Google'),
    @GoogleUserId,
    @GoogleData
WHERE @GoogleUserId IS NOT NULL AND @GoogleData IS NOT NULL;

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
SELECT
    @lastUserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Steam'),
    @SteamUserId,
    @SteamData
WHERE @SteamUserId IS NOT NULL AND @SteamData IS NOT NULL;

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Role');
INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @lastUserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@RoleClaims, CONCAT('$[', idx, ']')))
FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
WHERE JSON_UNQUOTE(JSON_EXTRACT(@RoleClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
AND @RoleClaims IS NOT NULL;

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'App');
INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @lastUserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AppClaims, CONCAT('$[', idx, ']')))
FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
WHERE JSON_UNQUOTE(JSON_EXTRACT(@AppClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
AND @AppClaims IS NOT NULL;

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Scope');
INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @lastUserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@ScopeClaims, CONCAT('$[', idx, ']')))
FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
WHERE JSON_UNQUOTE(JSON_EXTRACT(@ScopeClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
AND @ScopeClaims IS NOT NULL;

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Identity');
INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @lastUserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@IdentityClaims, CONCAT('$[', idx, ']')))
FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
WHERE JSON_UNQUOTE(JSON_EXTRACT(@IdentityClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
AND @IdentityClaims IS NOT NULL;

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Profile');
INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @lastUserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@ProfileClaims, CONCAT('$[', idx, ']')))
FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
WHERE JSON_UNQUOTE(JSON_EXTRACT(@ProfileClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
AND @ProfileClaims IS NOT NULL;

SELECT * FROM `Users` WHERE `Id` = @lastUserId;";

    /// <summary>
    /// Update existing user account.
    /// 
    /// Named Parameters:
    /// - @UserId               int
    /// - @Email                string?
    /// - @EmailVerified        bool?
    /// - @TwoFactorEnabled     bool?
    /// - @Region               string?
    /// - @Username             string?
    /// - @PasswordData         string?
    /// - @GoogleUserId         string?
    /// - @GoogleData           string?
    /// - @SteamUserId          string?
    /// - @SteamData            string?
    /// </summary>
    public const string UpdateUser = @"
UPDATE `Users`
SET 
    `Username` = IFNULL(@Username, `Username`),
    `Email` = IFNULL(@Email, `Email`),
    `Region` = IFNULL(@Region, `Region`),
    `EmailVerified` = IFNULL(@EmailVerified, `EmailVerified`),
    `TwoFactorEnabled` = IFNULL(@TwoFactorEnabled, `TwoFactorEnabled`)
WHERE `Id` = @UserId;

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (
    @UserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'),
    @Username,
    @PasswordData
)
WHERE @Username IS NOT NULL
    AND @PasswordData IS NOT NULL
ON DUPLICATE KEY UPDATE 
    `LoginProviderData` = VALUES(`LoginProviderData`);

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (
    @UserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Google'),
    @GoogleUserId,
    @GoogleData
)
WHERE @GoogleUserId IS NOT NULL
    AND @GoogleData IS NOT NULL
ON DUPLICATE KEY UPDATE 
    `LoginProviderUserId` = VALUES(`LoginProviderUserId`),
    `LoginProviderData` = VALUES(`LoginProviderData`);

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (
    @UserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Steam'),
    @SteamUserId,
    @SteamData
)
WHERE @SteamUserId IS NOT NULL
    AND @SteamData IS NOT NULL
ON DUPLICATE KEY UPDATE 
    `LoginProviderUserId` = VALUES(`LoginProviderUserId`),
    `LoginProviderData` = VALUES(`LoginProviderData`);

SELECT * FROM `Users` WHERE `Id` = @UserId;";

    /// <summary>
    /// Update existing user account.
    /// 
    /// Named Parameters:
    /// - @UserId               int
    /// - @Email                string?
    /// - @EmailVerified        bool?
    /// - @TwoFactorEnabled     bool?
    /// - @Region               string?
    /// - @Username             string?
    /// - @PasswordData         string?
    /// - @GoogleUserId         string?
    /// - @GoogleData           string?
    /// - @SteamUserId          string?
    /// - @SteamData            string?
    /// - @RemoveRoleClaims     (string,string,...)?
    /// - @AddRoleClaims        (string,string,...)?
    /// - @RemoveAppClaims      (string,string,...)?
    /// - @AddAppClaims         (string,string,...)?
    /// - @RemoveScopeClaims    (string,string,...)?
    /// - @AddScopeClaims       (string,string,...)?
    /// - @RemoveIdentityClaims (string,string,...)?
    /// - @AddIdentityClaims    (string,string,...)?
    /// - @RemoveProfileClaims  (string,string,...)?
    /// - @AddProfileClaims     (string,string,...)?
    /// </summary>
    public const string UpdateUser_WithClaims = @"
UPDATE `Users`
SET 
    `Username` = IFNULL(@Username, `Username`),
    `Email` = IFNULL(@Email, `Email`),
    `Region` = IFNULL(@Region, `Region`),
    `EmailVerified` = IFNULL(@EmailVerified, `EmailVerified`),
    `TwoFactorEnabled` = IFNULL(@TwoFactorEnabled, `TwoFactorEnabled`)
WHERE `Id` = @UserId;

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (
    @UserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'),
    @Username,
    @PasswordData
)
WHERE @Username IS NOT NULL AND @PasswordData IS NOT NULL
ON DUPLICATE KEY UPDATE 
    `LoginProviderData` = VALUES(`LoginProviderData`);

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (
    @UserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Google'),
    @GoogleUserId,
    @GoogleData
)
WHERE @GoogleUserId IS NOT NULL AND @GoogleData IS NOT NULL
ON DUPLICATE KEY UPDATE 
    `LoginProviderUserId` = VALUES(`LoginProviderUserId`),
    `LoginProviderData` = VALUES(`LoginProviderData`);

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (
    @UserId,
    (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Steam'),
    @SteamUserId,
    @SteamData
)
WHERE @SteamUserId IS NOT NULL AND @SteamData IS NOT NULL
ON DUPLICATE KEY UPDATE 
    `LoginProviderUserId` = VALUES(`LoginProviderUserId`),
    `LoginProviderData` = VALUES(`LoginProviderData`);

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Role');
DELETE FROM `UserClaims`
WHERE `UserId` = @UserId
    AND `TypeId` = @ClaimTypeId
    AND `Value` IN (
        SELECT JSON_UNQUOTE(JSON_EXTRACT(@RemoveRoleClaims, CONCAT('$[', idx, ']')))
        FROM JSON_TABLE(
            JSON_ARRAY(@RemoveRoleClaims),
            ""$[*]"" COLUMNS(
                idx FOR ORDINALITY,
                val JSON PATH '$'
            )
        )
    )
    AND @RemoveRoleClaims IS NOT NULL;

INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @UserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AddRoleClaims, CONCAT('$[', idx, ']')))
FROM JSON_TABLE(
    JSON_ARRAY(@AddRoleClaims),
    ""$[*]"" COLUMNS(
        idx FOR ORDINALITY,
        val JSON PATH '$'
    )
)
WHERE JSON_UNQUOTE(JSON_EXTRACT(@AddRoleClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
    AND @AddRoleClaims IS NOT NULL
ON DUPLICATE KEY UPDATE `Value` = VALUES(`Value`);

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'App');
DELETE FROM `UserClaims`
WHERE `UserId` = @UserId
    AND `TypeId` = @ClaimTypeId
    AND `Value` IN (
        SELECT JSON_UNQUOTE(JSON_EXTRACT(@RemoveAppClaims, CONCAT('$[', idx, ']')))
        FROM JSON_TABLE(
            JSON_ARRAY(@RemoveAppClaims),
            ""$[*]"" COLUMNS(
                idx FOR ORDINALITY,
                val JSON PATH '$'
            )
        )
    )
    AND @RemoveAppClaims IS NOT NULL;

INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @UserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AddAppClaims, CONCAT('$[', idx, ']')))
FROM JSON_TABLE(
    JSON_ARRAY(@AddAppClaims),
    ""$[*]"" COLUMNS(
        idx FOR ORDINALITY,
        val JSON PATH '$'
    )
)
WHERE JSON_UNQUOTE(JSON_EXTRACT(@AddAppClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
    AND @AddAppClaims IS NOT NULL
ON DUPLICATE KEY UPDATE `Value` = VALUES(`Value`);

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Scope');
DELETE FROM `UserClaims`
WHERE `UserId` = @UserId
    AND `TypeId` = @ClaimTypeId
    AND `Value` IN (
        SELECT JSON_UNQUOTE(JSON_EXTRACT(@RemoveScopeClaims, CONCAT('$[', idx, ']')))
        FROM JSON_TABLE(
            JSON_ARRAY(@RemoveScopeClaims),
            ""$[*]"" COLUMNS(
                idx FOR ORDINALITY,
                val JSON PATH '$'
            )
        )
    )
    AND @RemoveScopeClaims IS NOT NULL;

INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @UserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AddScopeClaims, CONCAT('$[', idx, ']')))
FROM JSON_TABLE(
    JSON_ARRAY(@AddScopeClaims),
    ""$[*]"" COLUMNS(
        idx FOR ORDINALITY,
        val JSON PATH '$'
    )
)
WHERE JSON_UNQUOTE(JSON_EXTRACT(@AddScopeClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
    AND @AddScopeClaims IS NOT NULL
ON DUPLICATE KEY UPDATE `Value` = VALUES(`Value`);

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Identity');
DELETE FROM `UserClaims`
WHERE `UserId` = @UserId
    AND `TypeId` = @ClaimTypeId
    AND `Value` IN (
        SELECT JSON_UNQUOTE(JSON_EXTRACT(@RemoveIdentityClaims, CONCAT('$[', idx, ']')))
        FROM JSON_TABLE(
            JSON_ARRAY(@RemoveIdentityClaims),
            ""$[*]"" COLUMNS(
                idx FOR ORDINALITY,
                val JSON PATH '$'
            )
        )
    )
    AND @RemoveIdentityClaims IS NOT NULL;

INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @UserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AddIdentityClaims, CONCAT('$[', idx, ']')))
FROM JSON_TABLE(
    JSON_ARRAY(@AddIdentityClaims),
    ""$[*]"" COLUMNS(
        idx FOR ORDINALITY,
        val JSON PATH '$'
    )
)
WHERE JSON_UNQUOTE(JSON_EXTRACT(@AddIdentityClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
    AND @AddIdentityClaims IS NOT NULL
ON DUPLICATE KEY UPDATE `Value` = VALUES(`Value`);

SET @ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Profile');
DELETE FROM `UserClaims`
WHERE `UserId` = @UserId
    AND `TypeId` = @ClaimTypeId
    AND `Value` IN (
        SELECT JSON_UNQUOTE(JSON_EXTRACT(@RemoveProfileClaims, CONCAT('$[', idx, ']')))
        FROM JSON_TABLE(
            JSON_ARRAY(@RemoveProfileClaims),
            ""$[*]"" COLUMNS(
                idx FOR ORDINALITY,
                val JSON PATH '$'
            )
        )
    )
    AND @RemoveProfileClaims IS NOT NULL;

INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
SELECT @UserId, @ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AddProfileClaims, CONCAT('$[', idx, ']')))
FROM JSON_TABLE(
    JSON_ARRAY(@AddProfileClaims),
    ""$[*]"" COLUMNS(
        idx FOR ORDINALITY,
        val JSON PATH '$'
    )
)
WHERE JSON_UNQUOTE(JSON_EXTRACT(@AddProfileClaims, CONCAT('$[', idx, ']'))) IS NOT NULL
    AND @AddProfileClaims IS NOT NULL
ON DUPLICATE KEY UPDATE `Value` = VALUES(`Value`);

SELECT * FROM `Users` WHERE `Id` = @UserId;";

    /// <summary>
    /// Get user by Guid, and include any claims they have.
    /// 
    /// Named Parameters:
    /// - @UserId           string
    /// </summary>
    public const string GetUser_ById_WithClaims = @"
SELECT u.*,
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Role' THEN uc.`Value` END) AS 'Role',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'App' THEN uc.`Value` END) AS 'App',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Scope' THEN uc.`Value` END) AS 'Scope',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Identity' THEN uc.`Value` END) AS 'Identity',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Profile' THEN uc.`Value` END) AS 'Profile'
FROM `Users` u
LEFT JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
LEFT JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE u.`Id` = @UserId
GROUP BY u.`Id`;";

    /// <summary>
    /// Get user by Id.
    /// 
    /// Named Parameters:
    /// - @UserId           string
    /// </summary>
    public const string GetUser_ById = @"
SELECT * FROM `Users` WHERE `Id` = @UserId;";

    /// <summary>
    /// Get user by Email, and include any claims they have.
    /// 
    /// Named Parameters:
    /// - @Email            string
    /// </summary>
    public const string GetUser_ByEmail_WithClaims = @"
SELECT u.*,
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Role' THEN uc.`Value` END) AS 'Role',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'App' THEN uc.`Value` END) AS 'App',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Scope' THEN uc.`Value` END) AS 'Scope',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Identity' THEN uc.`Value` END) AS 'Identity',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Profile' THEN uc.`Value` END) AS 'Profile'
FROM `Users` u
LEFT JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
LEFT JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE u.`Email` = @Email
GROUP BY u.`Id`;";

    /// <summary>
    /// Get user by Email.
    /// 
    /// Named Parameters:
    /// - @Email            string
    /// </summary>
    public const string GetUser_ByEmail = @"
SELECT * FROM `Users` WHERE `Email` = @Email;";

    /// <summary>
    /// Get user by Username, and include any claims they have.
    /// 
    /// Named Parameters:
    /// - @Username         string
    /// </summary>
    public const string GetUser_ByUsername_WithClaims = @"
SELECT u.*,
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Role' THEN uc.`Value` END) AS 'Role',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'App' THEN uc.`Value` END) AS 'App',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Scope' THEN uc.`Value` END) AS 'Scope',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Identity' THEN uc.`Value` END) AS 'Identity',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Profile' THEN uc.`Value` END) AS 'Profile'
FROM `Users` u
LEFT JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
LEFT JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE u.`Username` = @Username
GROUP BY u.`Id`;";

    /// <summary>
    /// Get user by Username.
    /// 
    /// Named Parameters:
    /// - @Username         string
    /// </summary>
    public const string GetUser_ByUsername = @"
SELECT * FROM `Users` WHERE `Username` = @Username;";

    /// <summary>
    /// Get user by provider name (Google, Steam, etc) and ID for that provider, and include any claims they have.
    /// 
    /// Named Parameters:
    /// - @ProviderName     string
    /// - @UserId           string
    /// </summary>
    public const string GetUser_ByProviderId_WithClaims = @"
SELECT u.*,
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Role' THEN uc.`Value` END) AS 'Role',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'App' THEN uc.`Value` END) AS 'App',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Scope' THEN uc.`Value` END) AS 'Scope',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Identity' THEN uc.`Value` END) AS 'Identity',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Profile' THEN uc.`Value` END) AS 'Profile'
FROM `Users` u
JOIN `UserLogins` ul ON u.`Id` = ul.`UserId`
JOIN `LoginProviders` lp ON ul.`LoginProviderId` = lp.`Id`
LEFT JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
LEFT JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE lp.`Name` = @ProviderName AND ul.`LoginProviderUserId` = @UserId
GROUP BY u.`Id`";

    /// <summary>
    /// Get user by provider name (Google, Steam, etc) and ID for that provider.
    /// 
    /// Named Parameters:
    /// - @ProviderName     string
    /// - @UserId           string
    /// </summary>
    public const string GetUser_ByProviderId = @"
SELECT u.*
FROM `Users` u
JOIN `UserLogins` ul ON u.`Id` = ul.`UserId`
JOIN `LoginProviders` lp ON ul.`LoginProviderId` = lp.`Id`
WHERE lp.`Name` = @ProviderName AND ul.`LoginProviderUserId` = @UserId";

    /// <summary>
    /// Get user by identity claim, and include any claims they have.
    /// 
    /// Named Parameters:
    /// - @UserId           string
    /// </summary>
    public const string GetUser_ByIdentityClaim_WithClaims = @"
SELECT u.*,
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Role' THEN uc.`Value` END) AS 'Role',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'App' THEN uc.`Value` END) AS 'App',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Scope' THEN uc.`Value` END) AS 'Scope',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Identity' THEN uc.`Value` END) AS 'Identity',
    GROUP_CONCAT(CASE WHEN ct.`Name` = 'Profile' THEN uc.`Value` END) AS 'Profile'
FROM `Users` u
JOIN `UserClaims` identityUc ON u.`Id` = identityUc.`UserId`
JOIN `ClaimTypes` identityCt ON identityUc.`TypeId` = identityCt.`Id`
LEFT JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
LEFT JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE identityCt.`Name` = 'Identity' AND identityUc.`Value` = @UserId
GROUP BY u.`Id`";

    /// <summary>
    /// Get user by identity claim.
    /// 
    /// Named Parameters:
    /// - @UserId           string
    /// </summary>
    public const string GetUser_ByIdentityClaim = @"
SELECT u.*
FROM `Users` u
JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE ct.`Name` = 'Identity' AND uc.`Value` = @UserId";

    /// <summary>
    /// Create `Users` table.
    /// </summary>
    public const string CreateTable_Users = @"
CREATE TABLE IF NOT EXISTS `Users` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Username` VARCHAR(255) UNIQUE NULL,
    `Email` VARCHAR(255) UNIQUE NULL,
    `EmailVerified` BOOLEAN NOT NULL DEFAULT FALSE,
    `TwoFactorEnabled` BOOLEAN NOT NULL DEFAULT FALSE,
    `Region` VARCHAR(255) NULL,
    `LockoutEnd` TIMESTAMP NULL,
    `SecurityToken` VARCHAR(255) NOT NULL,
    `LastLoginAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    `CreatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `DeletedAt` TIMESTAMP NULL
) ENGINE = InnoDB;";

    /// <summary>
    /// Create `LoginProviders` reference table.
    /// </summary>
    public const string CreateTable_LoginProviders = @"
CREATE TABLE IF NOT EXISTS `LoginProviders` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;";

    /// <summary>
    /// Create `LoginProviders` reference table and initialize it with defaults.
    /// </summary>
    public const string CreateTable_LoginProviders_Initialize = @"
CREATE TABLE IF NOT EXISTS `LoginProviders` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;
INSERT IGNORE INTO `LoginProviders` (`Name`) VALUES ('Google'), ('Steam'), ('Password');";

    /// <summary>
    /// Create `UserLogins` table.
    /// </summary>
    public const string CreateTable_UserLogins = @"
CREATE TABLE IF NOT EXISTS `UserLogins` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT UNSIGNED NOT NULL,
    `LoginProviderId` INT UNSIGNED NOT NULL,
    `LoginProviderUserId` VARCHAR(255) NOT NULL,
    `LoginProviderData` VARCHAR(512) NOT NULL,
    INDEX(`LoginProviderUserId`),
    FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
    FOREIGN KEY (`LoginProviderId`) REFERENCES `LoginProviders`(`Id`) ON DELETE CASCADE,
    UNIQUE(`UserId`, `LoginProviderId`),
    UNIQUE(`LoginProviderId`, `LoginProviderUserId`)
) ENGINE = InnoDB;";

    /// <summary>
    /// Create `ClaimTypes` reference table.
    /// </summary>
    public const string CreateTable_ClaimTypes = @"
CREATE TABLE IF NOT EXISTS `ClaimTypes` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;";

    /// <summary>
    /// Create `ClaimTypes` reference table and initialize it with defaults.
    /// </summary>
    public const string CreateTable_ClaimTypes_Initialize = @"
CREATE TABLE IF NOT EXISTS `ClaimTypes` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;
INSERT IGNORE INTO `ClaimTypes` (`Name`) VALUES ('Role'), ('App'), ('Scope'), ('Identity'), ('Profile');";

    /// <summary>
    /// Create `UserClaims` table.
    /// </summary>
    public const string CreateTable_UserClaims = @"
CREATE TABLE IF NOT EXISTS `UserClaims` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT UNSIGNED NOT NULL,
    `TypeId` INT UNSIGNED NOT NULL,
    `Value` VARCHAR(512) NOT NULL,
    INDEX (`UserId`),
    INDEX (`Value`),
    INDEX (`UserId`, `TypeId`),
    FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
    FOREIGN KEY (`TypeId`) REFERENCES `ClaimTypes`(`Id`) ON DELETE CASCADE,
    UNIQUE (`UserId`, `TypeId`, `Value`)
) ENGINE = InnoDB;";

}
