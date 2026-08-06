#!/bin/sh
# Cria o banco de IDENTIDADE além do de índice (que o POSTGRES_DB já criou).
#
# São separados porque têm naturezas diferentes: o índice é derivado do vault e apagá-lo para reconstruir
# é operação de rotina; a identidade não é reconstruível a partir de nada. Um "DROP DATABASE" de rotina
# não pode ter como levar as contas junto por descuido de quem digitou.
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-SQL
    CREATE DATABASE learn_identidade OWNER $POSTGRES_USER;
SQL
