# Dominica Learn — imagem de produção.
#
# Multi-estágio por dois motivos concretos: a imagem final não carrega o SDK (~800 MB contra ~110 MB do
# runtime), e o código-fonte não vai junto para o servidor. O restore fica num estágio próprio para que
# mudar uma linha de código não invalide o cache dos pacotes — a diferença entre um build de 15 s e um de
# 3 min, dez vezes por dia.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restauracao
WORKDIR /origem

# só os .csproj primeiro: esta camada só muda quando uma DEPENDÊNCIA muda
COPY Directory.Build.props ./
COPY src/Dominica.Learn.Domain/*.csproj          src/Dominica.Learn.Domain/
COPY src/Dominica.Learn.Application/*.csproj     src/Dominica.Learn.Application/
COPY src/Dominica.Learn.Infrastructure/*.csproj  src/Dominica.Learn.Infrastructure/
COPY src/Dominica.Learn.Web/*.csproj             src/Dominica.Learn.Web/
RUN dotnet restore src/Dominica.Learn.Web/Dominica.Learn.Web.csproj

FROM restauracao AS publicacao
COPY src/ src/
RUN dotnet publish src/Dominica.Learn.Web/Dominica.Learn.Web.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# USUÁRIO SEM PRIVILÉGIO. O contêiner monta o vault do usuário — um processo root aqui é root sobre os
# arquivos dele no host. O uid 64198 é fixo de propósito: o dono do volume no host precisa bater, e um
# uid sorteado a cada build tornaria a permissão do volume um mistério a cada deploy.
# O curl é do HEALTHCHECK lá embaixo, e sem ele o health check falha SEMPRE — a imagem
# mcr.microsoft.com/dotnet/aspnet não traz curl nem wget. O sintoma é um contêiner eternamente
# "unhealthy" com o app funcionando perfeitamente, que é pior que não ter health check: ensina quem
# opera a ignorar o sinal. (O Dockerfile da plataforma instala o curl pelo mesmo motivo.)
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# A PASTA DAS CHAVES PRECISA EXISTIR NA IMAGEM, com o dono certo.
#
# Não é zelo: quando um volume nomeado é montado sobre um caminho que JÁ EXISTE na imagem, o Docker
# copia o conteúdo E O DONO daquele caminho para o volume novo. Se o caminho NÃO existe, ele cria o
# diretório do volume pertencendo ao ROOT — e o processo, que roda como 64198, não consegue escrever.
# A proteção de dados então falha ao gerar a chave, e o que quebra é toda tela com formulário, porque
# o antiforgery depende dela. Criar a pasta aqui é o que faz o volume nascer com o dono certo.
RUN useradd --uid 64198 --create-home --shell /usr/sbin/nologin learn \
 && mkdir -p /dados/vault /home/learn/.aspnet/DataProtection-Keys \
 && chown -R learn:learn /dados /home/learn

COPY --from=publicacao --chown=learn:learn /app ./
USER learn

# O vault é VOLUME: se ficar na camada da imagem, um `docker compose up --build` apaga anos de estudo.
VOLUME ["/dados/vault"]

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
EXPOSE 8080

# O health check do contêiner bate no mesmo endpoint que o proxy usa. Ele confere o VAULT também, e não
# só o banco: volume não montado é a falha mais provável e a mais silenciosa em produção.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/saude || exit 1"]

ENTRYPOINT ["dotnet", "Dominica.Learn.Web.dll"]
