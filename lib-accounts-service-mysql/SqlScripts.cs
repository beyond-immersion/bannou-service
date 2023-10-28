namespace BeyondImmersion.BannouService.Accounts;

public static class SqlScripts
{
    public const string Create_User = @"
INSERT INTO `Users` (`Username`, `SecurityToken`, `Email`, `EmailVerified`, `TwoFactorEnabled`)
VALUES (@Username, @SecurityToken, @Email, @EmailVerified, @TwoFactorEnabled);

SET @lastUserId = LAST_INSERT_ID();

INSERT INTO `UserLogins` (`UserId`, `LoginProviderId`, `LoginProviderUserId`, `LoginProviderData`)
VALUES (@lastUserId, (SELECT `Id` FROM `LoginProviders` WHERE `Name` = 'Password'), @Username, @PasswordData);

SELECT * FROM `Users` WHERE `Id` = @lastUserId;";

    public const string Get_ByGuid = @"
SELECT * FROM `Users` WHERE `Id` = @UserId;";

    public const string Get_ByEmail = @"
SELECT * FROM `Users` WHERE `Email` = @UserId;";

    public const string Get_ByUsername = @"
SELECT * FROM `Users` WHERE `Username` = @UserId;";

    public const string Get_ByGoogleId = @"
SELECT u.*
FROM `Users` u
JOIN `UserLogins` ul ON u.`Id` = ul.`UserId`
JOIN `LoginProviders` lp ON ul.`LoginProviderId` = lp.`Id`
WHERE lp.`Name` = 'Google' AND ul.`LoginProviderUserId` = @UserId";

    public const string Get_BySteamId = @"
SELECT u.*
FROM `Users` u
JOIN `UserLogins` ul ON u.`Id` = ul.`UserId`
JOIN `LoginProviders` lp ON ul.`LoginProviderId` = lp.`Id`
WHERE lp.`Name` = 'Steam' AND ul.`LoginProviderUserId` = @UserId";

    public const string Get_ByIdentityClaim = @"";

    public const string Create_UsersTable = @"
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

    public const string Create_LoginProvidersTable = @"
CREATE TABLE IF NOT EXISTS `LoginProviders` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;";

    public const string CreateAndInitialize_LoginProvidersTable = @"
CREATE TABLE IF NOT EXISTS `LoginProviders` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;
INSERT IGNORE INTO `LoginProviders` (`Name`) VALUES ('Google'), ('Steam'), ('Password');";

    public const string Create_UserLoginsTable = @"
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

    public const string Create_ClaimTypesTable = @"
CREATE TABLE IF NOT EXISTS `ClaimTypes` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;";

    public const string CreateAndInitialize_ClaimTypesTable = @"
CREATE TABLE IF NOT EXISTS `ClaimTypes` (
    `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(255) UNIQUE NOT NULL
) ENGINE = InnoDB;
INSERT IGNORE INTO `ClaimTypes` (`Name`) VALUES ('Role'), ('Application'), ('Scope'), ('Identity'), ('Profile');";

    public const string Create_UserClaimsTable = @"
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

