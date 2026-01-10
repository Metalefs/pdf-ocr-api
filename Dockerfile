# ===================================================================
# Dockerfile Otimizado para Windows Server
# Usa instalação manual do Tesseract para imagem mais leve
# ===================================================================

# ===================================================================
# Estágio BASE: Runtime com Tesseract pré-instalado
# ===================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022 AS base
WORKDIR /app
EXPOSE 8080

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop';"]


# Criar diretórios de trabalho
RUN New-Item -ItemType Directory -Force -Path C:\temp\ocr_jobs | Out-Null

# ===================================================================
# Estágio BUILD: Compilação
# ===================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS build
WORKDIR /src

# Restaurar dependências (cache layer)
COPY ["pdf-ocr-api.csproj", "./"]
RUN dotnet restore "pdf-ocr-api-api.csproj"

# Copiar código e compilar
COPY . .
RUN dotnet build "pdf-ocr-api.csproj" -c Release -o /app/build

# ===================================================================
# Estágio PUBLISH: Publicação
# ===================================================================
FROM build AS publish
RUN dotnet publish "pdf-ocr-api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ===================================================================
# Estágio FINAL: Produção
# ===================================================================
FROM base AS final
WORKDIR /app

# Copiar binários publicados
COPY --from=publish /app/publish .

# Variáveis de ambiente
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TMP=C:\temp \
    TEMP=C:\temp

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD powershell -NoProfile -Command \
        "try { \
            $res = Invoke-WebRequest -Uri http://localhost:8080/ -UseBasicParsing -TimeoutSec 5; \
            exit ($res.StatusCode -eq 200 ? 0 : 1); \
        } catch { exit 1; }"

# Ponto de entrada
ENTRYPOINT ["dotnet", "pdf-ocr-api.dll"]