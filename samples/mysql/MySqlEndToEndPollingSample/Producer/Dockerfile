FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /code
COPY ["samples/mysql/MySqlEndToEndPollingSample/Producer/Producer.csproj", "samples/mysql/MySqlEndToEndPollingSample/Producer/"]
COPY ["samples/mysql/MySqlEndToEndPollingSample/ProducerShared/ProducerShared.csproj", "samples/mysql/MySqlEndToEndPollingSample/ProducerShared/"]
COPY ["src/Core/Core.csproj", "src/Core/"]
COPY ["src/Core.OpenTelemetry/Core.OpenTelemetry.csproj", "src/Core.OpenTelemetry/"]
COPY ["src/MySql/MySql.csproj", "src/MySql/"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "samples/mysql/MySqlEndToEndPollingSample/Producer/Producer.csproj"
COPY . .
WORKDIR "/code/samples/mysql/MySqlEndToEndPollingSample/Producer"
RUN dotnet publish "Producer.csproj" -c Release -o /app/publish # TODO figure out why --no-restore is not working

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Producer.dll"]