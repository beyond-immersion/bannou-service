#!/bin/bash
# Grant permissions script that uses environment variables
# The MySQL container automatically creates MYSQL_USER with MYSQL_PASSWORD
# This script grants the necessary permissions for Docker network access

echo "Granting permissions for user: $MYSQL_USER"

mysql -u root -p"$MYSQL_ROOT_PASSWORD" <<-EOSQL
    -- MySQL 8 compatibility with native password authentication for Docker networks
    CREATE USER IF NOT EXISTS '$MYSQL_USER'@'localhost' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';
    CREATE USER IF NOT EXISTS '$MYSQL_USER'@'172.%.%.%' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';
    CREATE USER IF NOT EXISTS '$MYSQL_USER'@'192.168.%.%' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';

    -- Ensure the main user uses native password authentication
    ALTER USER '$MYSQL_USER'@'%' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';

    -- Grant full privileges from all possible connection sources
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'192.168.%.%';

    FLUSH PRIVILEGES;
EOSQL

echo "Permissions granted successfully for user: $MYSQL_USER"
