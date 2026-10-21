FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/TaskWorker.Core/TaskWorker.Core.csproj src/TaskWorker.Core/
COPY src/TaskWorker.Infrastructure/TaskWorker.Infrastructure.csproj src/TaskWorker.Infrastructure/
COPY src/TaskWorker.Api/TaskWorker.Api.csproj src/TaskWorker.Api/

RUN dotnet restore src/TaskWorker.Api/TaskWorker.Api.csproj

COPY src/ src/

RUN dotnet publish src/TaskWorker.Api/TaskWorker.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "TaskWorker.Api.dll"]
