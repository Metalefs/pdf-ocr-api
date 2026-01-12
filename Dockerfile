# ===================================================================
# STAGE 1: Build & Publish
# ===================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Cache de pacotes Nuget
COPY ["pdf-ocr-api.csproj", "./"]
RUN dotnet restore "pdf-ocr-api.csproj"

COPY . .
RUN dotnet publish "pdf-ocr-api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ===================================================================
# STAGE 2: Runtime
# ===================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Instalação de dependências nativas vitais
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Essencial para SkiaSharp e renderização de texto
    libfontconfig1 \
    libfreetype6 \
    libicu-dev \
    libharfbuzz0b \
    # Essencial para System.Drawing.Common (Legacy)
    libgdiplus \
    # Tesseract OCR
    tesseract-ocr \
    tesseract-ocr-por \
    # Ferramentas de suporte
    curl \
    ca-certificates \
    wget \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# ===================================================================
# Injeção Robusta do PDFium (Ajustado para Dtronix/PDFiumCore)
# ===================================================================
# Baixa o binário nativo e coloca tanto no caminho de runtime quanto na raiz
RUN mkdir -p /app/runtimes/linux-x64/native && \
    wget -q https://github.com/bblanchon/pdfium-binaries/releases/latest/download/pdfium-linux-x64.tgz && \
    tar -xzf pdfium-linux-x64.tgz && \
    cp lib/libpdfium.so /app/runtimes/linux-x64/native/libpdfium.so && \
    cp lib/libpdfium.so /app/libpdfium.so && \
    chmod +x /app/libpdfium.so && \
    rm -rf pdfium-linux-x64.tgz lib

# ===================================================================
# Configurações de Ambiente e Permissões
# ===================================================================
RUN mkdir -p /tmp/ocr_jobs && chmod 777 /tmp/ocr_jobs

COPY --from=build /app/publish .

# Variáveis de ambiente corrigidas
ENV ASPNETCORE_URLS=http://+:8080 \
    # Garante que o Tesseract e o .NET achem as bibliotecas
    LD_LIBRARY_PATH="/app:/app/runtimes/linux-x64/native:${LD_LIBRARY_PATH}" \
    # Desativa o modo invariante para o Tesseract funcionar com caracteres especiais (acentos)
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TESSDATA_PREFIX=/usr/share/tesseract-ocr/5/tessdata/

# Criação de usuário não-root com permissões explícitas
RUN groupadd -r appuser && useradd -r -g appuser appuser && \
    chown -R appuser:appuser /app /tmp/ocr_jobs

USER appuser
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "pdf-ocr-api.dll"]