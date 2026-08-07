#!/bin/sh
# Cria os bancos que o POSTGRES_DB não criou (ele criou só o de índice).
#
# São TRÊS bancos, e a divisão não é organização: é uma diferença de natureza que precisa estar
# visível na hora de operar, não escondida numa tabela.
#
#   learn_indice ......... DERIVADO do vault. Apagá-lo para reconstruir é operação de rotina, e a
#                          documentação manda fazer isso quando algo está estranho.
#   learn_identidade ..... as contas. Não se reconstrói a partir de nada.
#   learn_registro ....... horas estudadas e questões resolvidas por matéria. Também não se
#                          reconstrói — e, ao contrário das contas, ninguém consegue redigitar: o
#                          dado existe porque alguém o registrou naquele dia.
#
# Um "DROP DATABASE" de rotina no índice não pode ter como levar os outros dois junto por descuido
# de quem digitou. É para isso que eles são bancos, e não esquemas.
#
# ATENÇÃO, EM STACK QUE JÁ ESTAVA NO AR: o /docker-entrypoint-initdb.d/ só roda quando o diretório
# de dados do Postgres está VAZIO. Num volume que já existe — o caso de qualquer instalação anterior
# ao learn_registro — este arquivo NUNCA será executado de novo, e o app sobe batendo em "database
# learn_registro does not exist" ao migrar. A correção é uma linha:
#
#   docker compose exec banco psql -U "$POSTGRES_USER" -d learn_indice \
#       -c 'CREATE DATABASE learn_registro OWNER '"$POSTGRES_USER"
#
# (No stack da plataforma isso não acontece: lá quem cria os bancos é o serviço learn-bancos, que
# roda a cada deploy e é idempotente — e o deploy.sh ainda confere e cria o que faltar.)
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-SQL
    CREATE DATABASE learn_identidade OWNER $POSTGRES_USER;
    CREATE DATABASE learn_registro OWNER $POSTGRES_USER;
SQL
