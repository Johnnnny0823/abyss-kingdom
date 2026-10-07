# 伺服器容器映像（Oracle Cloud Ampere A1 是 ARM64，這個 Dockerfile 兩種架構都能建置）
# 在專案根目錄執行：docker build -f server/Dockerfile -t abyss-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY shared/ shared/
COPY server/ server/
RUN dotnet publish server/AbyssKingdom.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "AbyssKingdom.Server.dll"]
