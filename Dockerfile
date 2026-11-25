# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["AUBDigitalAcademy.csproj", "./"]
RUN dotnet restore "AUBDigitalAcademy.csproj"

# Copy the entire source tree and publish
COPY . .
RUN dotnet publish "AUBDigitalAcademy.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# Create folder for persistent ASP.NET Core Data Protection keys
RUN mkdir -p /app/App_Data/DataProtection-Keys

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "AUBDigitalAcademy.dll"]
