# ===================================================================
# Dockerfile Otimizado para Produção - PDF OCR API SaaS
# Multi-stage build para imagem menor e mais rápida
# ===================================================================

# ===================================================================
# STAGE 1: Build
# ===================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copiar apenas arquivos de projeto primeiro (melhor cache)
COPY ["pdf-ocr-api.csproj", "./"]
RUN dotnet restore "pdf-ocr-api.csproj"

# Copiar código fonte
COPY . .

# Build em Release
RUN dotnet build "pdf-ocr-api.csproj" -c Release -o /app/build

# ===================================================================
# STAGE 2: Publish
# ===================================================================
FROM build AS publish
RUN dotnet publish "pdf-ocr-api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

# ===================================================================
# STAGE 3: Runtime (Final)
# ===================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# ===================================================================
# Instalar dependências necessárias + PDFium
# ===================================================================
RUN apt-get update && apt-get install -y \
    # Bibliotecas para System.Drawing (necessário para PDFiumSharp)
    libgdiplus \
    # Dependências do PDFium
    wget \
    unzip \
    # Utilitários
    curl \
    # Limpar cache
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# ===================================================================
# Baixar e instalar binários nativos do PDFium
# ===================================================================
RUN mkdir -p /app/runtimes/linux-x64/native && \
    cd /tmp && \
    wget https://github.com/bblanchon/pdfium-binaries/releases/latest/download/pdfium-linux-x64.tgz && \
    tar -xzf pdfium-linux-x64.tgz && \
    cp lib/libpdfium.so /app/runtimes/linux-x64/native/libpdfium.so && \
    rm -rf /tmp/*

# ===================================================================
# Criar diretórios de trabalho
# ===================================================================
RUN mkdir -p /tmp/ocr_jobs && \
    chmod 777 /tmp/ocr_jobs

# ===================================================================
# Copiar aplicação publicada
# ===================================================================
COPY --from=publish /app/publish .

# Nota: tessdata já vem incluído no pacote NuGet Tesseract

# ===================================================================
# Variáveis de ambiente
# ===================================================================
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TMP=/tmp \
    TEMP=/tmp

# ===================================================================
# Criar usuário não-root (segurança)
# ===================================================================
RUN groupadd -r appuser && \
    useradd -r -g appuser appuser && \
    chown -R appuser:appuser /app /tmp/ocr_jobs

USER appuser

# ===================================================================
# Expor porta
# ===================================================================
EXPOSE 8080

# ===================================================================
# Health check
# ===================================================================
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/ || exit 1

# ===================================================================
# Iniciar aplicação
# ===================================================================
ENTRYPOINT ["dotnet", "pdf-ocr-api.dll"]