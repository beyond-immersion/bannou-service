namespace BeyondImmersion.BannouService.Accounts;

public static class SqlScripts
{
    public const string AddUser = @"
INSERT INTO `Users` (`Username`, `SecurityToken`, `Email`, `EmailVerified`, `TwoFactorEnabled`)
VALUES (@Username, @SecurityToken, @Email, @EmailVerified, @TwoFactorEnabled);

SET @lastUserId = LAST_INSERT_ID();

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'), @Username, @PasswordData);

SELECT * FROM `Users` WHERE `Id` = @lastUserId;";

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

    public const string GetUser_ById = @"
SELECT * FROM `Users` WHERE `Id` = @UserId;";

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
WHERE u.`Email` = @UserId
GROUP BY u.`Id`;";

    public const string GetUser_ByEmail = @"
SELECT * FROM `Users` WHERE `Email` = @UserId;";

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
WHERE u.`Username` = @UserId
GROUP BY u.`Id`;";

    public const string GetUser_ByUsername = @"
SELECT * FROM `Users` WHERE `Username` = @UserId;";

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

    public const string GetUser_ByProviderId = @"
SELECT u.*
FROM `Users` u
JOIN `UserLogins` ul ON u.`Id` = ul.`UserId`
JOIN `LoginProviders` lp ON ul.`LoginProviderId` = lp.`Id`
WHERE lp.`Name` = @ProviderName AND ul.`LoginProviderUserId` = @UserId";

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

    public const string GetUser_ByIdentityClaim = @"
SELECT u.*
FROM `Users` u
JOIN `UserClaims` uc ON u.`Id` = uc.`UserId`
JOIN `ClaimTypes` ct ON uc.`TypeId` = ct.`Id`
WHERE ct.`Name` = 'Identity' AND uc.`Value` = @UserId";

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

    public const string CreateTable_LoginProviders = @"
CREATE TABLE IF NOT EXISTS `LoginProviders` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;";

    public const string CreateTable_LoginProviders_Initialize = @"
CREATE TABLE IF NOT EXISTS `LoginProviders` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;
INSERT IGNORE INTO `LoginProviders` (`Name`) VALUES ('Google'), ('Steam'), ('Password');";

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

    public const string CreateTable_ClaimTypes = @"
CREATE TABLE IF NOT EXISTS `ClaimTypes` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;";

    public const string CreateTable_ClaimTypes_Initialize = @"
CREATE TABLE IF NOT EXISTS `ClaimTypes` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;
INSERT IGNORE INTO `ClaimTypes` (`Name`) VALUES ('Role'), ('App'), ('Scope'), ('Identity'), ('Profile');";

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
