FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN mkdir -p /var/lib/muscleforge/keys && chown "$APP_UID:$APP_UID" /var/lib/muscleforge/keys && chmod 700 /var/lib/muscleforge/keys
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source
COPY . .
RUN dotnet restore src/MuscleForge.Api/MuscleForge.Api.csproj
RUN dotnet publish src/MuscleForge.Api/MuscleForge.Api.csproj -c "$BUILD_CONFIGURATION" --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS runtime
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MuscleForge.Api.dll"]
