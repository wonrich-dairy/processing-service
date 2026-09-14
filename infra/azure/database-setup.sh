#!/usr/bin/env bash
# Create the two databases Processing Service owns on the shared MySQL Flexible Server, plus the
# per-environment account that can reach each one (SCRUM-70 AC: "Staging and production use
# separate databases"; SCRUM-71 AC: "credentials scoped only to its own database").
#
#   STAGING_DB_PASSWORD='...' PROD_DB_PASSWORD='...' ./infra/azure/database-setup.sh
#
# The server admin password is read from the terminal, not from an argument or an env var, so it
# never reaches argv, shell history or a CI log. The two application passwords come from the
# environment because the same values have to go into provision.sh afterwards.
#
# Idempotent: re-running creates nothing twice and re-applies the grants.
set -euo pipefail

MYSQL_HOST="${MYSQL_HOST:-mcc-db.mysql.database.azure.com}"
MYSQL_PORT="${MYSQL_PORT:-3306}"
MYSQL_ADMIN_USER="${MYSQL_ADMIN_USER:-mccadmin}"

# Must match the database names in docs/environments.md and docs/database.md.
STAGING_DB="${STAGING_DB:-processingdb}"
PROD_DB="${PROD_DB:-processingdb_prod}"
STAGING_USER="${STAGING_USER:-processing_app}"
PROD_USER="${PROD_USER:-processing_app_prod}"

: "${STAGING_DB_PASSWORD:?set STAGING_DB_PASSWORD to the password for $STAGING_USER}"
: "${PROD_DB_PASSWORD:?set PROD_DB_PASSWORD to the password for $PROD_USER}"

if [ "$STAGING_DB_PASSWORD" = "$PROD_DB_PASSWORD" ]; then
  echo "refusing: staging and production must not share a password — a leaked staging" >&2
  echo "credential would then reach production data" >&2
  exit 1
fi

# Either client works; mysqlsh is what ships with MySQL Workbench on Windows.
if command -v mysql >/dev/null 2>&1; then
  client=mysql
elif command -v mysqlsh >/dev/null 2>&1; then
  client=mysqlsh
else
  echo "need the 'mysql' or 'mysqlsh' client on PATH" >&2
  exit 1
fi

read -rsp "MySQL password for $MYSQL_ADMIN_USER@$MYSQL_HOST: " admin_password
echo

# A single quote in a password would otherwise end the SQL string literal.
sql_quote() { printf "%s" "$1" | sed "s/'/''/g"; }
staging_pw=$(sql_quote "$STAGING_DB_PASSWORD")
prod_pw=$(sql_quote "$PROD_DB_PASSWORD")

# Not GRANT ALL: the application needs DDL because EF Core applies its own migrations, but it has
# no business with GRANT, FILE or anything outside its own schema.
app_grants="SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, INDEX, REFERENCES"

sql=$(cat <<SQL
CREATE DATABASE IF NOT EXISTS \`$STAGING_DB\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS \`$PROD_DB\`    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE USER IF NOT EXISTS '$STAGING_USER'@'%' IDENTIFIED BY '$staging_pw';
CREATE USER IF NOT EXISTS '$PROD_USER'@'%'    IDENTIFIED BY '$prod_pw';

GRANT $app_grants ON \`$STAGING_DB\`.* TO '$STAGING_USER'@'%';
GRANT $app_grants ON \`$PROD_DB\`.*    TO '$PROD_USER'@'%';
FLUSH PRIVILEGES;

SELECT SCHEMA_NAME FROM information_schema.SCHEMATA
 WHERE SCHEMA_NAME IN ('$STAGING_DB', '$PROD_DB');
SQL
)

# The password goes to the client on stdin/env, never as an argument.
case "$client" in
  mysql)
    MYSQL_PWD="$admin_password" mysql \
      --host="$MYSQL_HOST" --port="$MYSQL_PORT" --user="$MYSQL_ADMIN_USER" \
      --ssl-mode=REQUIRED --batch <<<"$sql"
    ;;
  mysqlsh)
    MYSQL_PWD="$admin_password" mysqlsh --sql --quiet-start=2 \
      --host="$MYSQL_HOST" --port="$MYSQL_PORT" --user="$MYSQL_ADMIN_USER" \
      --ssl-mode=REQUIRED <<<"$sql"
    ;;
esac

cat <<DONE

== done
   staging:    $STAGING_DB / $STAGING_USER
   production: $PROD_DB / $PROD_USER

Both are empty until something deploys against them; Program.cs applies the migrations on first
start outside Production. Feed the same passwords to provision.sh as PROCESSING_DB_CONNECTION.
DONE
