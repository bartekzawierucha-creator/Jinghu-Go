# syntax=docker/dockerfile:1
# Build context: repository root.   docker build -f infrastructure/docker/api.Dockerfile -t jianghu-api .
# Probes: the orchestrator calls GET /health/live and /health/ready over HTTP (the runtime image has no curl).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY content/ content/
COPY server/ server/
RUN dotnet publish server/src/Api/JianghuGo.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "JianghuGo.Api.dll"]
