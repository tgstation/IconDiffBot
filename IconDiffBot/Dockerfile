FROM microsoft/aspnetcore-build AS build

WORKDIR /src
COPY IconDiffBot/* IconDiffBot/
COPY IconDiffBot.Tests/* IconDiffBot.Tests/

WORKDIR /src/IconDiffBot.Tests
RUN dotnet restore
RUN dotnet test -c Docker

WORKDIR /src/IconDiffBot
RUN dotnet publish -c Docker -o /app && cp entrypoint.sh /app/

FROM microsoft/aspnetcore AS base

WORKDIR /app
COPY --from=build /app .

RUN mkdir /config_data && mv appsettings.Docker.json /config_data/appsettings.Production.json
VOLUME ["/config_data"]

EXPOSE 80
ENTRYPOINT ["./entrypoint.sh"]
