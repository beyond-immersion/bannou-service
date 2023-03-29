FROM mcr.microsoft.com/dotnet/sdk:7.0 as intermediate

COPY ./ /app/

WORKDIR /app/
RUN echo bannou: `git rev-parse HEAD` > /REVISION

WORKDIR /
RUN dotnet publish -o /app/bannou-service/publish -c Release /app/bannou-service/

FROM mcr.microsoft.com/dotnet/aspnet:7.0

COPY --from=intermediate /app/bannou-service/publish /app
COPY --from=intermediate /REVISION /REVISION

WORKDIR /app

CMD ["dotnet", "bannou-service.dll"]
