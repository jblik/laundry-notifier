FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY laundry-notifier.fsproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "laundry-notifier.dll"]
