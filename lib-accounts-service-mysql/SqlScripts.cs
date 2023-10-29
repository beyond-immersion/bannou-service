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
    /// - @Username             string?
    /// - @PasswordData         string?
    /// - @GoogleUserId         string?
    /// - @GoogleData           string?
    /// - @SteamUserId          string?
    /// - @SteamData            string?
    /// </summary>
    public const string AddUser = @"
INSERT INTO `Users` (`Username`, `SecurityToken`, `Email`, `EmailVerified`, `TwoFactorEnabled`)
VALUES (@Username, @SecurityToken, @Email, @EmailVerified, @TwoFactorEnabled);
SET @@lastUserId := LAST_INSERT_ID();

IF (@Username IS NOT NULL AND @PasswordData IS NOT NULL) THEN
    INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
    VALUES (@@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'), @Username, @PasswordData);
END IF;

IF (@GoogleUserId IS NOT NULL AND @GoogleData IS NOT NULL) THEN
    INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
    VALUES (@@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Google'), @GoogleUserId, @GoogleData);
END IF;

IF (@SteamUserId IS NOT NULL AND @SteamData IS NOT NULL) THEN
    INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
    VALUES (@@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Steam'), @SteamUserId, @SteamData);
END IF;

SELECT * FROM `Users` WHERE `Id` = @@lastUserId;";

    /// <summary>
    /// Add new user account, and stores any included claims.
    /// 
    /// Named Parameters:
    /// - @SecurityToken        string
    /// - @Email                string?
    /// - @EmailVerified        bool?
    /// - @TwoFactorEnabled     bool?
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
INSERT INTO `Users` (`Username`, `SecurityToken`, `Email`, `EmailVerified`, `TwoFactorEnabled`)
VALUES (@Username, @SecurityToken, @Email, @EmailVerified, @TwoFactorEnabled);
SET @@lastUserId := LAST_INSERT_ID();

IF (@Username IS NOT NULL AND @PasswordData IS NOT NULL) THEN
    INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
    VALUES (@@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'), @Username, @PasswordData);
END IF;

IF (@GoogleUserId IS NOT NULL AND @GoogleData IS NOT NULL) THEN
    INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
    VALUES (@@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Google'), @GoogleUserId, @GoogleData);
END IF;

IF (@SteamUserId IS NOT NULL AND @SteamData IS NOT NULL) THEN
    INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
    VALUES (@@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Steam'), @SteamUserId, @SteamData);
END IF;

IF (@RoleClaims IS NOT NULL) THEN
    SET @@ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Role');
    INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
    SELECT @@lastUserId, @@ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@RoleClaims, CONCAT('$[', idx, ']')))
    FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
    WHERE JSON_UNQUOTE(JSON_EXTRACT(@RoleClaims, CONCAT('$[', idx, ']'))) IS NOT NULL;
END IF;

IF (@AppClaims IS NOT NULL) THEN
    SET @@ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'App');
    INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
    SELECT @@lastUserId, @@ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@AppClaims, CONCAT('$[', idx, ']')))
    FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
    WHERE JSON_UNQUOTE(JSON_EXTRACT(@AppClaims, CONCAT('$[', idx, ']'))) IS NOT NULL;
END IF;

IF (@ScopeClaims IS NOT NULL) THEN
    SET @@ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Scope');
    INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
    SELECT @@lastUserId, @@ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@ScopeClaims, CONCAT('$[', idx, ']')))
    FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
    WHERE JSON_UNQUOTE(JSON_EXTRACT(@ScopeClaims, CONCAT('$[', idx, ']'))) IS NOT NULL;
END IF;

IF (@IdentityClaims IS NOT NULL) THEN
    SET @@ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Identity');
    INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
    SELECT @@lastUserId, @@ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@IdentityClaims, CONCAT('$[', idx, ']')))
    FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
    WHERE JSON_UNQUOTE(JSON_EXTRACT(@IdentityClaims, CONCAT('$[', idx, ']'))) IS NOT NULL;
END IF;

IF (@ProfileClaims IS NOT NULL) THEN
    SET @@ClaimTypeId = (SELECT `Id` FROM `ClaimTypes` WHERE `Name` = 'Profile');
    INSERT INTO `UserClaims` (`UserId`, `TypeId`, `Value`)
    SELECT @@lastUserId, @@ClaimTypeId, JSON_UNQUOTE(JSON_EXTRACT(@ProfileClaims, CONCAT('$[', idx, ']')))
    FROM (SELECT 0 AS idx UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) AS indexes
    WHERE JSON_UNQUOTE(JSON_EXTRACT(@ProfileClaims, CONCAT('$[', idx, ']'))) IS NOT NULL;
END IF;

SELECT * FROM `Users` WHERE `Id` = @@lastUserId;";

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
    /// Get user by Guid.
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
    /// Create `LoginProviders` reference table and initialize it with default supported providers.
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
    `LoginProviderUserId` VARCHAR(255) UNIQUE NOT NULL,
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
    /// Create `ClaimTypes` reference table and initialize it with default supported claim types.
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
    FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
    FOREIGN KEY (`TypeId`) REFERENCES `ClaimTypes`(`Id`) ON DELETE CASCADE,
    UNIQUE (`UserId`, `TypeId`, `Value`)
) ENGINE = InnoDB;";
}
