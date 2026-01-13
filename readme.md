# ============================================
# README.md
# ============================================
# 📄 TextLayer OCR - SaaS

Ferramenta profissional de OCR para PDFs que **preserva formulários e campos preenchíveis**.

## 🚀 Features

- ⚡ **Rápido**: Processa PDFs em segundos
- 🔒 **Privado**: Arquivos deletados automaticamente
- 💎 **Grátis**: Sem limites para uso pessoal
- 📝 **Preserva Formulários**: Mantém campos editáveis intactos
- 🌐 **API REST**: Integração fácil para desenvolvedores

## 🛠️ Stack Tecnológica

**Backend:**
- ASP.NET Core 8
- Tesseract OCR (reconhecimento de texto)
- iText 7 (manipulação de PDF)
- PDFium (renderização)

**Infraestrutura:**
- Railway (hosting backend)
- Vercel (hosting frontend)
- Docker (containerização)

## 📦 Instalação Local

### Pré-requisitos

```bash
# .NET 8 SDK
# Tesseract OCR
# Docker (opcional)
```

### Instalação Tesseract

**Windows:**
```bash
choco install tesseract
```

**macOS:**
```bash
brew install tesseract tesseract-lang
```

**Linux:**
```bash
sudo apt install tesseract-ocr tesseract-ocr-por
```

### Rodar Projeto

```bash
# Clone o repositório
git clone https://github.com/seu-usuario/ocr-saas.git
cd ocr-saas

# Restaurar dependências
dotnet restore

# Rodar aplicação
dotnet run

# Acessar: http://localhost:5000
# Swagger: http://localhost:5000/swagger
```

### Rodar com Docker

```bash
# Build
docker build -t ocr-api .

# Run
docker run -p 8080:8080 ocr-api

# Acessar: http://localhost:8080
```

## 🔌 API Endpoints

### 1. Upload e Processar (Síncrono)
```bash
POST /api/process-sync
Content-Type: multipart/form-data

# Retorna: PDF processado diretamente
```

### 2. Upload e Processar (Assíncrono)
```bash
POST /api/process
Content-Type: multipart/form-data

# Retorna: { jobId, statusUrl }
```

### 3. Consultar Status
```bash
GET /api/jobs/{jobId}/status

# Retorna: { status, logs, downloadUrl }
```

### 4. Download
```bash
GET /api/jobs/{jobId}/download

# Retorna: PDF processado
```

## 📖 Documentação Completa

Acesse: `/swagger` para documentação interativa da API.

## 🧪 Testes

```bash
# Testar endpoint de health
curl http://localhost:5000/

# Testar upload (síncrono)
curl -X POST http://localhost:5000/api/process-sync \
  -F "file=@teste.pdf" \
  --output resultado.pdf
```

## 🚢 Deploy

Ver guia completo em: [DEPLOY_GUIDE.md](DEPLOY_GUIDE.md)

**Resumo:**
1. Push para GitHub
2. Conectar Railway ao repositório
3. Deploy automático!

## 💰 Custos

- **Desenvolvimento**: $0 (open source)
- **Hosting (Railway)**: $0-5/mês (free tier 500h)
- **Frontend (Vercel)**: $0 (grátis)
- **Total**: ~$5/mês para 100 usuários

## 📄 Licença

MIT License - Use comercialmente sem problemas!

## 🤝 Contribuições

PRs são bem-vindos! Para mudanças grandes, abra uma issue primeiro.

## 📧 Contato

- Twitter: @seu_usuario
- Email: contato@exemplo.com

## ⭐ Mostre Suporte

Se este projeto te ajudou, considere dar uma ⭐!

---