FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["MovieReporter.Web/MovieReporter.Web.csproj", "MovieReporter.Web/"]
COPY ["MovieReporter.Core/MovieReporter.Core.csproj", "MovieReporter.Core/"]

RUN dotnet restore "MovieReporter.Web/MovieReporter.Web.csproj"

COPY . .

RUN dotnet publish "MovieReporter.Web/MovieReporter.Web.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV MovieReporter__EnableHttpsRedirection=false
ENV MOVIE_REPORTER_SOURCE_LIBRARY=
ENV HOME=/home/app

USER root
RUN mkdir -p /home/app/Downloads \
    && chown -R ${APP_UID:-1654}:0 /app /home/app

USER $APP_UID

EXPOSE 8080
VOLUME ["/home/app/Downloads"]

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MovieReporter.Web.dll"]
