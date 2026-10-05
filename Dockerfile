FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY KeycloakMvc/KeycloakMvc.csproj KeycloakMvc/
RUN dotnet restore KeycloakMvc/KeycloakMvc.csproj
COPY KeycloakMvc/ KeycloakMvc/
RUN dotnet publish KeycloakMvc/KeycloakMvc.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "KeycloakMvc.dll"]
