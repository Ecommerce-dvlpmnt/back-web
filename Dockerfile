FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["EcommerceBackend.csproj", "./"]
RUN dotnet restore "EcommerceBackend.csproj"

COPY . .
RUN dotnet publish "EcommerceBackend.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN mkdir -p /app/wwwroot

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "EcommerceBackend.dll"]
