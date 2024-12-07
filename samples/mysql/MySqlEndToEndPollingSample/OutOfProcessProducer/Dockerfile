FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /code
COPY ["samples/mysql/MySqlEndToEndPollingSample/OutOfProcessProducer/OutOfProcessProducer.csproj", "samples/mysql/MySqlEndToEndPollingSample/OutOfProcessProducer/"]
COPY ["samples/mysql/MySqlEndToEndPollingSample/ProducerShared/ProducerShared.csproj", "samples/mysql/MySqlEndToEndPollingSample/ProducerShared/"]
COPY ["src/Core/Core.csproj", "src/Core/"]
COPY ["src/MySql/MySql.csproj", "src/MySql/"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "samples/mysql/MySqlEndToEndPollingSample/OutOfProcessProducer/OutOfProcessProducer.csproj"
COPY . .
WORKDIR "/code/samples/mysql/MySqlEndToEndPollingSample/OutOfProcessProducer"
RUN dotnet build "OutOfProcessProducer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OutOfProcessProducer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OutOfProcessProducer.dll"]