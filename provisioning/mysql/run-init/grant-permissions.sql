-- Ensure Franklin user has proper authentication and permissions for Docker networks
-- MySQL 8 compatibility with native password authentication
ALTER USER 'Franklin'@'%' IDENTIFIED WITH mysql_native_password BY 'DevPassword';
CREATE USER IF NOT EXISTS 'Franklin'@'localhost' IDENTIFIED WITH mysql_native_password BY 'DevPassword';
CREATE USER IF NOT EXISTS 'Franklin'@'172.%.%.%' IDENTIFIED WITH mysql_native_password BY 'DevPassword';  
CREATE USER IF NOT EXISTS 'Franklin'@'192.168.%.%' IDENTIFIED WITH mysql_native_password BY 'DevPassword';

-- Grant full privileges from all possible connection sources
GRANT ALL PRIVILEGES ON `accounts`.* TO 'Franklin'@'%';
GRANT ALL PRIVILEGES ON `accounts`.* TO 'Franklin'@'localhost';
GRANT ALL PRIVILEGES ON `accounts`.* TO 'Franklin'@'172.%.%.%';
GRANT ALL PRIVILEGES ON `accounts`.* TO 'Franklin'@'192.168.%.%';

FLUSH PRIVILEGES;
