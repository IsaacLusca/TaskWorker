FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/TaskWorker.Core/TaskWorker.Core.csproj src/TaskWorker.Core/
COPY src/TaskWorker.Infrastructure/TaskWorker.Infrastructure.csproj src/TaskWorker.Infrastructure/
COPY src/TaskWorker.Worker/TaskWorker.Worker.csproj src/TaskWorker.Worker/

RUN dotnet restore src/TaskWorker.Worker/TaskWorker.Worker.csproj

COPY src/ src/

RUN dotnet publish src/TaskWorker.Worker/TaskWorker.Worker.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "TaskWorker.Worker.dll"]
