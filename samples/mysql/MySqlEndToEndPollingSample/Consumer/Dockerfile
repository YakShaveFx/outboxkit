FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /code
COPY ["samples/mysql/MySqlEndToEndPollingSample/Consumer/Consumer.csproj", "samples/mysql/MySqlEndToEndPollingSample/Consumer/"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "samples/mysql/MySqlEndToEndPollingSample/Consumer/Consumer.csproj"
COPY . .
WORKDIR "/code/samples/mysql/MySqlEndToEndPollingSample/Consumer"
RUN dotnet build "Consumer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Consumer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Consumer.dll"]