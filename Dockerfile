# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY HomelabDashboard.sln .
COPY src/HomelabDashboard/HomelabDashboard.csproj src/HomelabDashboard/
COPY tests/HomelabDashboard.Tests/HomelabDashboard.Tests.csproj tests/HomelabDashboard.Tests/
RUN dotnet restore HomelabDashboard.sln

COPY . .
RUN dotnet publish src/HomelabDashboard/HomelabDashboard.csproj -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "HomelabDashboard.dll"]
