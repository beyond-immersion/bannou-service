FROM mcr.microsoft.com/dotnet/sdk:7.0 as intermediate
COPY ./ /app/

RUN apt-get update && apt-get install -y libxml2-utils

WORKDIR /app
RUN bash build-libs.sh
WORKDIR /

RUN dotnet dev-certs https
RUN dotnet publish -o /app/bannou-service/publish -c Release /app/bannou-service/

FROM mcr.microsoft.com/dotnet/aspnet:7.0
COPY --from=intermediate /app/bannou-service/publish /app
COPY --from=intermediate /app/libs /app/libs
COPY --from=intermediate /root/.dotnet/corefx/cryptography/x509stores/my/* /root/.dotnet/corefx/cryptography/x509stores/my/
COPY --from=intermediate /root/.dotnet/corefx/cryptography/x509stores/my/* /certificates/

WORKDIR /app
EXPOSE 80
EXPOSE 443

ENV ASPNETCORE_URLS=http://+:80;https://+:443

RUN apt-get update && apt-get install -y curl
HEALTHCHECK CMD curl -k --fail https://localhost/health || exit 1

CMD ["dotnet", "bannou-service.dll"]
