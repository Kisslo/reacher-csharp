FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY backend/src backend/src

RUN dotnet restore backend/src/Reacher.Api/Reacher.Api.csproj
RUN dotnet publish backend/src/Reacher.Api/Reacher.Api.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Reacher.Api.dll"]
