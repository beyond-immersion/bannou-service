#!/bin/bash
# Grant permissions script that uses environment variables
# The MySQL container automatically creates MYSQL_USER with MYSQL_PASSWORD
# This script grants the necessary permissions for Docker network access
# and creates additional databases needed by Dapr state stores

echo "Granting permissions for user: $MYSQL_USER"

mysql -u root -p"$MYSQL_ROOT_PASSWORD" <<-EOSQL
    -- MySQL 8 compatibility with native password authentication for Docker networks
    CREATE USER IF NOT EXISTS '$MYSQL_USER'@'localhost' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';
    CREATE USER IF NOT EXISTS '$MYSQL_USER'@'172.%.%.%' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';
    CREATE USER IF NOT EXISTS '$MYSQL_USER'@'192.168.%.%' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';

    -- Ensure the main user uses native password authentication
    ALTER USER '$MYSQL_USER'@'%' IDENTIFIED WITH mysql_native_password BY '$MYSQL_PASSWORD';

    -- Create additional databases for Dapr state stores
    -- (accounts is created by MYSQL_DATABASE env var, these are additional)
    CREATE DATABASE IF NOT EXISTS characters;
    CREATE DATABASE IF NOT EXISTS realms;
    CREATE DATABASE IF NOT EXISTS locations;
    CREATE DATABASE IF NOT EXISTS relationships;
    CREATE DATABASE IF NOT EXISTS relationship_types;
    CREATE DATABASE IF NOT EXISTS species;
    CREATE DATABASE IF NOT EXISTS servicedata;
    CREATE DATABASE IF NOT EXISTS subscriptions;
    CREATE DATABASE IF NOT EXISTS bannou_state;

    -- Grant full privileges on accounts database (created by MYSQL_DATABASE)
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON \`$MYSQL_DATABASE\`.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on characters database
    GRANT ALL PRIVILEGES ON characters.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON characters.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON characters.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON characters.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on realms database
    GRANT ALL PRIVILEGES ON realms.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON realms.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON realms.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON realms.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on locations database
    GRANT ALL PRIVILEGES ON locations.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON locations.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON locations.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON locations.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on relationships database
    GRANT ALL PRIVILEGES ON relationships.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON relationships.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON relationships.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON relationships.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on relationship_types database
    GRANT ALL PRIVILEGES ON relationship_types.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON relationship_types.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON relationship_types.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON relationship_types.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on servicedata database
    GRANT ALL PRIVILEGES ON servicedata.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON servicedata.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON servicedata.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON servicedata.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on subscriptions database
    GRANT ALL PRIVILEGES ON subscriptions.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON subscriptions.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON subscriptions.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON subscriptions.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on species database
    GRANT ALL PRIVILEGES ON species.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON species.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON species.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON species.* TO '$MYSQL_USER'@'192.168.%.%';

    -- Grant full privileges on bannou_state database (lib-state plugin)
    GRANT ALL PRIVILEGES ON bannou_state.* TO '$MYSQL_USER'@'%';
    GRANT ALL PRIVILEGES ON bannou_state.* TO '$MYSQL_USER'@'localhost';
    GRANT ALL PRIVILEGES ON bannou_state.* TO '$MYSQL_USER'@'172.%.%.%';
    GRANT ALL PRIVILEGES ON bannou_state.* TO '$MYSQL_USER'@'192.168.%.%';

    FLUSH PRIVILEGES;
EOSQL

echo "Permissions granted successfully for user: $MYSQL_USER"
echo "Databases created: accounts (via env), characters, realms, locations, relationships, relationship_types, species, servicedata, subscriptions, bannou_state"
