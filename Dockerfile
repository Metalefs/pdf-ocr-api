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

# Instalação de dependências nativas e fontes
# Aceitar automaticamente a licença da Microsoft para fontes core
RUN echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections

# Instalação de dependências nativas vitais
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 libfreetype6 libicu-dev libharfbuzz0b libgdiplus \
    # Tesseract e Dicionários
    tesseract-ocr tesseract-ocr-por tesseract-ocr-eng tesseract-ocr-ara \
    tesseract-ocr-chi-sim tesseract-ocr-jpn tesseract-ocr-kor tesseract-ocr-osd tesseract-ocr-rus\
    # Fontes e Utilidades
    ttf-mscorefonts-installer fonts-liberation fontconfig \
    curl wget ca-certificates \
    && fc-cache -f -v \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# ===================================================================
# Injeção Robusta do PDFium (Ajustado para Linux)
# ===================================================================
# IMPORTANTE: Criamos links simbólicos pois alguns wrappers procuram 'pdfium' sem o prefixo 'lib'
RUN mkdir -p /app/runtimes/linux-x64/native && \
    wget -q https://github.com/bblanchon/pdfium-binaries/releases/latest/download/pdfium-linux-x64.tgz && \
    tar -xzf pdfium-linux-x64.tgz && \
    cp lib/libpdfium.so /app/runtimes/linux-x64/native/libpdfium.so && \
    ln -s /app/runtimes/linux-x64/native/libpdfium.so /app/runtimes/linux-x64/native/pdfium.so && \
    ln -s /app/runtimes/linux-x64/native/libpdfium.so /app/libpdfium.so && \
    rm -rf pdfium-linux-x64.tgz lib

# ===================================================================
# Configurações de Ambiente e Permissões
# ===================================================================
RUN mkdir -p /tmp/ocr_jobs && chmod 777 /tmp/ocr_jobs

COPY --from=build /app/publish .

# ===================================================================
# Variáveis de Ambiente Corrigidas
# ===================================================================
ENV ASPNETCORE_URLS=http://+:8080 \
    LD_LIBRARY_PATH="/app:/app/runtimes/linux-x64/native:${LD_LIBRARY_PATH}" \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TESSDATA_PREFIX=/usr/share/tesseract-ocr/5/tessdata/ \
    LC_ALL=C.UTF-8 \
    LANG=C.UTF-8

# Criação de usuário não-root com permissões explícitas
RUN groupadd -r appuser && useradd -r -g appuser appuser && \
    chown -R appuser:appuser /app /tmp/ocr_jobs

RUN chmod -R 755 /usr/share/tesseract-ocr/
USER appuser
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "pdf-ocr-api.dll"]