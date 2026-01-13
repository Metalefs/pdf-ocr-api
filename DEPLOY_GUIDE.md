# 🚀 GUIA DE DEPLOY - 2 DIAS

## 📋 ESTRUTURA DO PROJETO

```
ocr-saas/
├── Program.cs                    # API principal
├── OcrPipelineService.cs         # Serviço de processamento
├── OcrApi.csproj                 # Configuração do projeto
├── Dockerfile                    # Configuração Docker
├── .dockerignore                 # Arquivos ignorados no build
├── .gitignore                    # Git ignore
├── README.md                     # Documentação
└── frontend/
    └── index.html                # Frontend completo
```

## ⚡ DIA 1: BACKEND (6 horas)

### Hora 1-2: Setup Inicial

1. **Criar projeto:**
```bash
mkdir ocr-saas && cd ocr-saas
dotnet new web -n OcrApi
cd OcrApi
```

2. **Copiar arquivos:**
   - `Program.cs` → Do artifact "complete_api_program"
   - `OcrPipelineService.cs` → Do artifact "ocr_service_complete"
   - `OcrApi.csproj` → Do artifact "csproj_file"

3. **Instalar dependências:**
```bash
dotnet restore
```

### Hora 3-4: Testar Localmente

1. **Rodar aplicação:**
```bash
dotnet run
```

2. **Testar endpoint:**
```bash
# Acessar: http://localhost:5000/swagger
# Testar upload de PDF no endpoint /api/process-sync
```

3. **Se der erro de dependências:**
```bash
# Instalar Tesseract no seu sistema:
# Windows: choco install tesseract
# Mac: brew install tesseract tesseract-lang
# Linux: sudo apt install tesseract-ocr tesseract-ocr-por
```

### Hora 5-6: Deploy Railway

1. **Criar conta:**
   - Acessar: https://railway.app
   - Fazer login com GitHub

2. **Preparar arquivos:**

Criar `.dockerignore`:
```
bin/
obj/
*.user
.vs/
.vscode/
```

Criar `Dockerfile` (copiar do artifact "dockerfile_prod")

3. **Criar repositório GitHub:**
```bash
git init
git add .
git commit -m "Initial commit"
gh repo create ocr-saas --public --source=. --remote=origin
git push -u origin main
```

4. **Deploy no Railway:**
   - Clicar em "New Project"
   - Escolher "Deploy from GitHub"
   - Selecionar repositório `ocr-saas`
   - Railway detecta Dockerfile automaticamente
   - Clicar em "Deploy"
   - Aguardar ~5-10 minutos

5. **Obter URL:**
   - No dashboard, clicar em "Settings"
   - Copiar URL: `https://ocr-saas-production.up.railway.app`

6. **Testar API online:**
```bash
curl https://sua-url.railway.app/
# Deve retornar: {"status":"online","service":"TextLayer OCR API"...}
```

### ✅ CHECKLIST DIA 1
- [ ] API roda localmente
- [ ] Teste de upload/download funciona
- [ ] Deploy no Railway concluído
- [ ] API responde online
- [ ] Endpoint `/swagger` acessível

---

## ⚡ DIA 2: FRONTEND + LAUNCH (6 horas)

### Hora 1-2: Frontend

1. **Criar pasta frontend:**
```bash
mkdir ../frontend
cd ../frontend
```

2. **Copiar `index.html`:**
   - Copiar do artifact "frontend_production"

3. **Atualizar API_BASE_URL no HTML:**
```javascript
// Linha ~350 do index.html
const API_BASE_URL = 'https://sua-url.railway.app';
```

### Hora 3: Deploy Frontend (Vercel)

1. **Criar repositório separado:**
```bash
git init
git add index.html
git commit -m "Frontend"
gh repo create ocr-frontend --public --source=. --remote=origin
git push -u origin main
```

2. **Deploy Vercel:**
   - Acessar: https://vercel.com
   - "New Project" → Importar GitHub
   - Selecionar `ocr-frontend`
   - Framework: Other (HTML estático)
   - Deploy!

3. **URL final:**
   - `https://ocr-frontend.vercel.app`

### Hora 4: Testes Finais

**Checklist de testes:**
```
[ ] Upload de PDF pequeno (1 página) funciona
[ ] Modo síncrono retorna PDF
[ ] Modo assíncrono mostra progresso
[ ] Download funciona
[ ] Erro para arquivo > 10MB
[ ] Erro para arquivo não-PDF
[ ] Testar em celular (Chrome + Safari)
[ ] Testar com PDF de 5 páginas
```

### Hora 5-6: Launch + Marketing

1. **SEO Rápido (5 min):**
   - Adicionar Google Analytics no `<head>`
   - Gerar `sitemap.xml` (https://www.xml-sitemaps.com)
   - Adicionar no Google Search Console

2. **Product Hunt (30 min):**
   - Criar conta: https://producthunt.com
   - Agendar launch para segunda 00:01 PST
   - Screenshot da interface
   - GIF do processo (ScreenToGif)
   - Título: "Turn scanned PDFs into editable docs - preserves forms"

3. **Reddit (30 min):**

Postar em (usar template abaixo):
- r/SideProject
- r/SaaS  
- r/entrepreneur

Template:
```
[Lancei em 48h] OCR para PDFs que preserva formulários

Oi! Passei o fim de semana criando uma ferramenta gratuita de OCR
que resolve um problema específico: PDFs escaneados com formulários.

🔧 Stack: ASP.NET Core + Tesseract + iText + Railway
🎯 Features:
  - Converte PDF escaneado em editável
  - Preserva campos de formulário
  - Grátis, sem cadastro
  - Processa em ~1 min

Link: [seu-site.vercel.app]

Feedback muito bem-vindo! 🙏
```

4. **Twitter/X (20 min):**
```
🚀 Acabei de lançar em 48h: OCR gratuito para PDFs

✨ Diferencial: Preserva formulários e campos preenchíveis

🛠️ Stack: C# + Tesseract + Railway
🔗 [seu-site.com]

#buildinpublic #indiehacker #csharp
```

5. **IndieHackers (20 min):**
   - Postar em: https://indiehackers.com/products
   - Categoria: Productivity Tools

### ✅ CHECKLIST DIA 2
- [ ] Frontend deployed no Vercel
- [ ] Todos os testes passam
- [ ] Product Hunt agendado
- [ ] 3 posts no Reddit
- [ ] Tweet publicado
- [ ] 10 amigos testaram

---

## 💰 MONETIZAÇÃO (Semana 1-2)

### Opção 1: Donate Button (Implementar em 5 min)

Adicionar no HTML após resultado:
```html

    ☕ Gostou? Me pague um café!

```

### Opção 2: Gumroad (Setup em 15 min)

1. Criar conta: https://gumroad.com
2. Criar produto: "100 Páginas OCR Premium" - $9.99
3. Link no site: "Precisa de mais páginas?"

### Opção 3: Lemonsqueezy (Semana 2)

Planos de assinatura:
```
Starter:  500 páginas/mês    - $19/mês
Pro:      2.500 páginas/mês  - $49/mês
Business: 10.000 páginas/mês - $149/mês
```

---

## 🐛 TROUBLESHOOTING

### Problema: "Out of Memory" no Railway
**Solução:**
```csharp
// No Program.cs, reduzir limite:
if (file.Length > 5_000_000) // 5MB em vez de 10MB
    return Results.BadRequest(new { error = "Max 5MB" });
```

### Problema: Tesseract não encontrado
**Solução:** Verificar no Dockerfile se instalou corretamente:
```dockerfile
RUN tesseract --version  # Adicionar esta linha
```

### Problema: CORS error no frontend
**Solução:** Verificar se `UseCors()` está antes dos endpoints no Program.cs

### Problema: API muito lenta
**Solução temporária:** Desabilitar etapas pesadas:
```csharp
// No OcrPipelineService.cs, comentar temporariamente:
// RenderPdfToImages(...);  // Skip this for testing
```

---

## 📊 MÉTRICAS PARA ACOMPANHAR

### Dia 1:
- [ ] Site no ar
- [ ] 1 PDF processado com sucesso

### Semana 1:
- [ ] 50 visitantes únicos
- [ ] 20 PDFs processados
- [ ] 1 compartilhamento social

### Mês 1:
- [ ] 500 visitantes
- [ ] 200 PDFs processados
- [ ] $50 de receita

---

## 🚀 PRÓXIMOS PASSOS (Pós-launch)

### Semana 3: Melhorias Técnicas
- [ ] Implementar rate limiting (10 req/hora por IP)
- [ ] Adicionar Redis para cache
- [ ] Email notification quando concluir
- [ ] Cleanup automático de arquivos (cron job)

### Semana 4: Features
- [ ] Dashboard de usuário (login)
- [ ] Histórico de PDFs processados
- [ ] Planos pagos (Stripe)
- [ ] API key para desenvolvedores

### Mês 2: Crescimento
- [ ] Blog posts (SEO)
- [ ] Integração Zapier
- [ ] Chrome Extension
- [ ] Afiliados (20% comissão)

---

## 💡 DICAS FINAIS

1. **Mantenha simples:** Não adicione features até ter 100 usuários
2. **Monitore logs:** Railway tem logs em tempo real
3. **Backup:** Railway faz snapshot automático
4. **Custos:** Free tier Railway = 500h/mês (suficiente para 100 usuários)
5. **Suporte:** Responda rápido no Reddit/Twitter

---

## 📞 SUPORTE

Se algo der errado:
1. Verificar logs no Railway: Dashboard → View Logs
2. Testar API com Postman: Importar endpoints do Swagger
3. GitHub Issues: Abrir issue no repositório

**Boa sorte! 🚀**