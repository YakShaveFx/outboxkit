FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /code
COPY ["samples/mysql/MySqlEndToEndPollingSample/Producer/Producer.csproj", "samples/mysql/MySqlEndToEndPollingSample/Producer/"]
COPY ["src/Core/Core.csproj", "src/Core/"]
COPY ["src/MySql/MySql.csproj", "src/MySql/"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "samples/mysql/MySqlEndToEndPollingSample/Producer/Producer.csproj"
COPY . .
WORKDIR "/code/samples/mysql/MySqlEndToEndPollingSample/Producer"
RUN dotnet build "Producer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Producer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Producer.dll"]