# syntax=docker/dockerfile:1
# Build context: repository root.   docker build -f infrastructure/docker/worker.Dockerfile -t jianghu-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY content/ content/
COPY server/ server/
RUN dotnet publish server/src/Worker/JianghuGo.Worker.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER app
ENTRYPOINT ["dotnet", "JianghuGo.Worker.dll"]
