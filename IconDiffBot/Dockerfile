FROM microsoft/aspnetcore-build AS build

WORKDIR /src
COPY IconDiffBot/* IconDiffBot/

WORKDIR /src/IconDiffBot
RUN dotnet restore
RUN dotnet build -c Docker

WORKDIR /src/IconDiffBot
RUN dotnet publish -c Docker -o /app && cp entrypoint.sh /app/

FROM microsoft/aspnetcore AS base

#deps for System.Drawing
RUN apt-get update && apt-get install -y --no-install-recommends \
	libc6 \
	&& rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

RUN mkdir /config_data && mv appsettings.Docker.json /config_data/appsettings.Production.json
VOLUME ["/config_data"]

EXPOSE 80
ENTRYPOINT ["./entrypoint.sh"]
