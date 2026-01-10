# ============================================
# OCR API - Build and Run Script (Windows)
# ============================================

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("local", "docker", "docker-compose")]
    [string]$Mode = "local",
    
    [Parameter(Mandatory=$false)]
    [int]$Port = 8080,
    
    [Parameter(Mandatory=$false)]
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

# Cores
function Write-Success { param($Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Info { param($Message) Write-Host "ℹ $Message" -ForegroundColor Cyan }
function Write-Error-Custom { param($Message) Write-Host "✗ $Message" -ForegroundColor Red }
function Write-Warning-Custom { param($Message) Write-Host "⚠ $Message" -ForegroundColor Yellow }

Write-Host "==========================================" -ForegroundColor Blue
Write-Host "🚀 OCR API - Build and Run" -ForegroundColor Blue
Write-Host "==========================================" -ForegroundColor Blue
Write-Host "Modo: $Mode"
Write-Host "Porta: $Port"
Write-Host ""

# ============================================
# VERIFICAR PRÉ-REQUISITOS
# ============================================
Write-Info "Verificando pré-requisitos..."

# Verificar .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Success ".NET SDK instalado: $dotnetVersion"
} catch {
    Write-Error-Custom ".NET SDK não encontrado!"
    Write-Host "Instale: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 1
}

# Verificar Docker (para modos docker)
if ($Mode -in @("docker", "docker-compose")) {
    try {
        $dockerVersion = docker --version
        Write-Success "Docker instalado: $dockerVersion"
        
        # Verificar se está rodando Windows containers
        $dockerInfo = docker info 2>&1
        if ($dockerInfo -match "linux") {
            Write-Warning-Custom "Docker está em modo Linux!"
            Write-Host "Precisa trocar para Windows containers:" -ForegroundColor Yellow
            Write-Host "  1. Botão direito no ícone do Docker Desktop" -ForegroundColor Yellow
            Write-Host "  2. 'Switch to Windows containers...'" -ForegroundColor Yellow
            
            $confirm = Read-Host "Deseja continuar mesmo assim? (y/n)"
            if ($confirm -ne "y") {
                exit 1
            }
        }
    } catch {
        Write-Error-Custom "Docker não encontrado!"
        Write-Host "Instale: https://www.docker.com/products/docker-desktop/" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ""

# ============================================
# LIMPEZA (se solicitado)
# ============================================
if ($Clean) {
    Write-Info "Limpando artefatos anteriores..."
    
    if (Test-Path "bin") { Remove-Item -Recurse -Force "bin" }
    if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" }
    if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }
    
    Write-Success "Limpeza concluída"
    Write-Host ""
}

# ============================================
# MODO LOCAL
# ============================================
if ($Mode -eq "local") {
    Write-Info "Iniciando em modo LOCAL..."
    
    # Restaurar dependências
    Write-Info "Restaurando dependências..."
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Falha ao restaurar dependências"
        exit 1
    }
    Write-Success "Dependências restauradas"
    
    # Build
    Write-Info "Compilando projeto..."
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Falha no build"
        exit 1
    }
    Write-Success "Build concluído"
    
    # Criar diretório de trabalho
    $tempDir = "C:\temp\ocr_jobs"
    if (-not (Test-Path $tempDir)) {
        New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
        Write-Success "Diretório temporário criado: $tempDir"
    }
    
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Green
    Write-Success "API iniciando..."
    Write-Host "🌐 URL: http://localhost:$Port" -ForegroundColor Cyan
    Write-Host "📚 Swagger: http://localhost:$Port/swagger" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Pressione Ctrl+C para parar" -ForegroundColor Yellow
    Write-Host ""
    
    # Rodar aplicação
    dotnet run --urls "http://localhost:$Port"
}

# ============================================
# MODO DOCKER
# ============================================
elseif ($Mode -eq "docker") {
    Write-Info "Iniciando em modo DOCKER..."
    
    $imageName = "ocr-api:windows"
    $containerName = "ocr-api"
    
    # Parar container anterior se existir
    $existingContainer = docker ps -a --filter "name=$containerName" --format "{{.Names}}" 2>$null
    if ($existingContainer -eq $containerName) {
        Write-Info "Parando container existente..."
        docker stop $containerName 2>$null
        docker rm $containerName 2>$null
        Write-Success "Container anterior removido"
    }
    
    # Build da imagem
    Write-Info "Construindo imagem Docker..."
    Write-Warning-Custom "Isso pode levar 10-15 minutos na primeira vez..."
    
    docker build -t $imageName -f Dockerfile .
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Falha no build da imagem Docker"
        exit 1
    }
    Write-Success "Imagem construída: $imageName"
    
    # Criar diretório para volumes
    $volumeDir = "C:\temp\ocr_jobs"
    if (-not (Test-Path $volumeDir)) {
        New-Item -ItemType Directory -Force -Path $volumeDir | Out-Null
    }
    
    # Rodar container
    Write-Info "Iniciando container..."
    docker run -d `
        --name $containerName `
        -p "${Port}:8080" `
        -e ASPNETCORE_ENVIRONMENT=Production `
        -v "${volumeDir}:C:\temp\ocr_jobs" `
        $imageName
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Falha ao iniciar container"
        exit 1
    }
    
    Write-Success "Container iniciado: $containerName"
    
    # Aguardar container iniciar
    Write-Info "Aguardando container inicializar..."
    Start-Sleep -Seconds 10
    
    # Verificar health
    $maxAttempts = 30
    $attempt = 0
    $healthy = $false
    
    while ($attempt -lt $maxAttempts) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        } catch {
            # Container ainda não está pronto
        }
        
        Start-Sleep -Seconds 2
        $attempt++
        Write-Host "." -NoNewline
    }
    
    Write-Host ""
    
    if ($healthy) {
        Write-Host ""
        Write-Host "==========================================" -ForegroundColor Green
        Write-Success "API rodando no Docker!"
        Write-Host "🌐 URL: http://localhost:$Port" -ForegroundColor Cyan
        Write-Host "📚 Swagger: http://localhost:$Port/swagger" -ForegroundColor Cyan
        Write-Host "🐳 Container: $containerName" -ForegroundColor Cyan
        Write-Host "==========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Comandos úteis:" -ForegroundColor Yellow
        Write-Host "  Ver logs:        docker logs -f $containerName"
        Write-Host "  Parar:           docker stop $containerName"
        Write-Host "  Remover:         docker rm $containerName"
        Write-Host "  Entrar:          docker exec -it $containerName powershell"
        Write-Host ""
    } else {
        Write-Error-Custom "Container não está respondendo!"
        Write-Host "Ver logs: docker logs $containerName" -ForegroundColor Yellow
        exit 1
    }
}

# ============================================
# MODO DOCKER COMPOSE
# ============================================
elseif ($Mode -eq "docker-compose") {
    Write-Info "Iniciando com DOCKER COMPOSE..."
    
    # Verificar se docker-compose existe
    if (-not (Test-Path "docker-compose.yml")) {
        Write-Error-Custom "Arquivo docker-compose.yml não encontrado!"
        exit 1
    }
    
    # Parar containers anteriores
    Write-Info "Parando containers anteriores..."
    docker-compose down 2>$null
    
    # Build e iniciar
    Write-Info "Construindo e iniciando containers..."
    Write-Warning-Custom "Isso pode levar 10-15 minutos na primeira vez..."
    
    docker-compose up -d --build
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Falha no docker-compose"
        exit 1
    }
    
    Write-Success "Containers iniciados"
    
    # Aguardar serviço iniciar
    Write-Info "Aguardando serviços inicializarem..."
    Start-Sleep -Seconds 15
    
    # Verificar health
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 5
        
        Write-Host ""
        Write-Host "==========================================" -ForegroundColor Green
        Write-Success "API rodando via Docker Compose!"
        Write-Host "🌐 URL: http://localhost:$Port" -ForegroundColor Cyan
        Write-Host "📚 Swagger: http://localhost:$Port/swagger" -ForegroundColor Cyan
        Write-Host "==========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Comandos úteis:" -ForegroundColor Yellow
        Write-Host "  Ver logs:        docker-compose logs -f"
        Write-Host "  Parar:           docker-compose down"
        Write-Host "  Restart:         docker-compose restart"
        Write-Host "  Rebuild:         docker-compose up -d --build"
        Write-Host ""
    } catch {
        Write-Warning-Custom "Container iniciou mas não está respondendo ainda"
        Write-Host "Aguarde alguns segundos e tente acessar: http://localhost:$Port" -ForegroundColor Yellow
    }
}

# ============================================
# TESTE RÁPIDO
# ============================================
function Test-Api {
    param([string]$BaseUrl)
    
    Write-Host ""
    Write-Info "Executando teste rápido..."
    
    try {
        # Health check
        $health = Invoke-RestMethod -Uri "$BaseUrl/" -Method Get
        Write-Success "Health check: $($health.status)"
        
        # API Info
        $info = Invoke-RestMethod -Uri "$BaseUrl/api/info" -Method Get
        Write-Success "API Info: $($info.service) v$($info.version)"
        
        Write-Host ""
        Write-Success "Testes básicos passaram!"
    } catch {
        Write-Warning-Custom "Erro nos testes: $($_.Exception.Message)"
    }
}

# Executar teste se API está rodando
if ($Mode -ne "local") {
    Test-Api -BaseUrl "http://localhost:$Port"
}

Write-Host ""
Write-Host "✅ Processo concluído!" -ForegroundColor Green