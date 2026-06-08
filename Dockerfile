FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/EHealth.Lab.Api/EHealth.Lab.Api.csproj EHealth.Lab.Api/
RUN dotnet restore EHealth.Lab.Api/EHealth.Lab.Api.csproj

COPY src/EHealth.Lab.Api/ EHealth.Lab.Api/
RUN dotnet publish EHealth.Lab.Api/EHealth.Lab.Api.csproj \
    -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out ./
EXPOSE 3002
ENV ASPNETCORE_URLS=http://+:3002
ENTRYPOINT ["dotnet", "EHealth.Lab.Api.dll"]
