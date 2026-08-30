FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HookLens.slnx ./
COPY src ./src

RUN dotnet restore src/HookLens/HookLens.csproj
RUN dotnet publish src/HookLens/HookLens.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__HookLens="Data Source=/data/hooklens.db"

COPY --from=build /app/publish .

EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "HookLens.dll"]
