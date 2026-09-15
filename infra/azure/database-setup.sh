#!/usr/bin/env bash
# Create the two databases Processing Service owns on the shared MySQL Flexible Server, plus the
# per-environment account that can reach each one (SCRUM-70 AC: "Staging and production use
# separate databases"; SCRUM-71 AC: "credentials scoped only to its own database").
#
#   ./infra/azure/database-setup.sh
#
# Every password is typed at the prompt, never passed as an argument or an environment variable:
# a command-line password lands in the shell history and in the process list, where the next
# person to run `history` finds the credential for production. Press Enter at either application
# password to have a strong one generated instead.
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

# Either client works; mysqlsh is what ships with MySQL Workbench on Windows.
if command -v mysql >/dev/null 2>&1; then
  client=mysql
elif command -v mysqlsh >/dev/null 2>&1; then
  client=mysqlsh
else
  echo "need the 'mysql' or 'mysqlsh' client on PATH" >&2
  exit 1
fi

# Read from the terminal rather than stdin, so prompting still works if the script is piped.
tty_dev=/dev/tty
[ -r "$tty_dev" ] || { echo "no terminal available to read passwords from" >&2; exit 1; }

ask_secret() {  # ask_secret <prompt> -> echoes the value
  local prompt="$1" value
  read -rsp "$prompt" value < "$tty_dev"
  echo >&2
  printf '%s' "$value"
}

# Alphanumeric only: ';' separates fields in an ADO.NET connection string and "'" ends a SQL
# string literal, so a generated password containing either would break something downstream.
generate_password() {
  LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 24
}

admin_password=$(ask_secret "MySQL password for $MYSQL_ADMIN_USER@$MYSQL_HOST: ")
[ -n "$admin_password" ] || { echo "the admin password is required" >&2; exit 1; }

generated=""
staging_password=$(ask_secret "New password for $STAGING_USER (staging) [Enter to generate]: ")
if [ -z "$staging_password" ]; then
  staging_password=$(generate_password)
  generated="yes"
fi

prod_password=$(ask_secret "New password for $PROD_USER (production) [Enter to generate]: ")
if [ -z "$prod_password" ]; then
  prod_password=$(generate_password)
  generated="yes"
fi

# The three accounts must not share a password. Staging and production sharing one means a leaked
# staging credential reaches production data; either sharing the admin's means a leaked
# application credential is admin over every database on the server, including intake and auth.
if [ "$staging_password" = "$prod_password" ]; then
  echo "refusing: staging and production must not share a password" >&2
  exit 1
fi
if [ "$staging_password" = "$admin_password" ] || [ "$prod_password" = "$admin_password" ]; then
  echo "refusing: an application account must not reuse the server admin password — the whole" >&2
  echo "point of these accounts is that they reach one database and nothing else" >&2
  exit 1
fi

# A single quote in a password would otherwise end the SQL string literal.
sql_quote() { printf '%s' "$1" | sed "s/'/''/g"; }

# Not GRANT ALL: the application needs DDL because EF Core applies its own migrations, but it has
# no business with GRANT, FILE or anything outside its own schema.
app_grants="SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, INDEX, REFERENCES"

sql=$(cat <<SQL
CREATE DATABASE IF NOT EXISTS \`$STAGING_DB\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS \`$PROD_DB\`    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE USER IF NOT EXISTS '$STAGING_USER'@'%' IDENTIFIED BY '$(sql_quote "$staging_password")';
CREATE USER IF NOT EXISTS '$PROD_USER'@'%'    IDENTIFIED BY '$(sql_quote "$prod_password")';

-- Re-running with a new password is how a rotation is applied.
ALTER USER '$STAGING_USER'@'%' IDENTIFIED BY '$(sql_quote "$staging_password")';
ALTER USER '$PROD_USER'@'%'    IDENTIFIED BY '$(sql_quote "$prod_password")';

GRANT $app_grants ON \`$STAGING_DB\`.* TO '$STAGING_USER'@'%';
GRANT $app_grants ON \`$PROD_DB\`.*    TO '$PROD_USER'@'%';
FLUSH PRIVILEGES;

SELECT SCHEMA_NAME AS created FROM information_schema.SCHEMATA
 WHERE SCHEMA_NAME IN ('$STAGING_DB', '$PROD_DB');
SQL
)

# MYSQL_PWD keeps the admin password off the command line, where `ps` would show it.
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

echo
echo "== done"
echo "   staging:    $STAGING_DB / $STAGING_USER"
echo "   production: $PROD_DB / $PROD_USER"

if [ -n "$generated" ]; then
  echo
  echo "== generated passwords — copy them into a password manager now, they are not stored"
  echo "   $STAGING_USER : $staging_password"
  echo "   $PROD_USER : $prod_password"
fi

cat <<'DONE'

Both databases are empty until something deploys against them; Program.cs applies the migrations
on first start outside Production. Feed these passwords to provision.sh in
PROCESSING_DB_CONNECTION, and clear your scrollback afterwards.
DONE
