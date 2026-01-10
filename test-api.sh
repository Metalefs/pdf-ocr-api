#!/bin/bash

# ============================================
# Script de Testes da API OCR
# ============================================

set -e

# Cores
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuração
API_URL="${API_URL:-http://localhost:5000}"
TEST_PDF="${TEST_PDF:-test.pdf}"

echo -e "${BLUE}=================================="
echo "🧪 TESTES DA API OCR"
echo "=================================="
echo -e "API: ${API_URL}${NC}"
echo ""

# ============================================
# FUNÇÃO: Criar PDF de teste
# ============================================
create_test_pdf() {
    if [ ! -f "$TEST_PDF" ]; then
        echo -e "${YELLOW}⚠️  Arquivo de teste não encontrado.${NC}"
        echo "Criando PDF de teste..."
        
        # Criar PDF simples usando echo e ps2pdf (se disponível)
        cat > test.ps << 'EOF'
%!PS-Adobe-3.0
%%BoundingBox: 0 0 612 792
/Helvetica findfont 24 scalefont setfont
100 700 moveto
(Documento de Teste OCR) show
100 650 moveto
(Este é um PDF para testar a API) show
showpage
EOF
        
        if command -v ps2pdf &> /dev/null; then
            ps2pdf test.ps $TEST_PDF
            rm test.ps
            echo -e "${GREEN}✓ PDF de teste criado${NC}"
        else
            echo -e "${RED}❌ ps2pdf não encontrado. Por favor, forneça um PDF de teste.${NC}"
            exit 1
        fi
    fi
}

# ============================================
# TESTE 1: Health Check
# ============================================
test_health() {
    echo -e "${BLUE}[TESTE 1] Health Check...${NC}"
    
    RESPONSE=$(curl -s -w "\n%{http_code}" ${API_URL}/)
    HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
    BODY=$(echo "$RESPONSE" | head -n-1)
    
    if [ "$HTTP_CODE" = "200" ]; then
        echo -e "${GREEN}✓ API está online${NC}"
        echo "   Resposta: $(echo $BODY | jq -c '.')"
    else
        echo -e "${RED}✗ API não responde (HTTP $HTTP_CODE)${NC}"
        return 1
    fi
    echo ""
}

# ============================================
# TESTE 2: Swagger UI
# ============================================
test_swagger() {
    echo -e "${BLUE}[TESTE 2] Verificando Swagger...${NC}"
    
    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" ${API_URL}/swagger/index.html)
    
    if [ "$HTTP_CODE" = "200" ]; then
        echo -e "${GREEN}✓ Swagger disponível em ${API_URL}/swagger${NC}"
    else
        echo -e "${RED}✗ Swagger não encontrado (HTTP $HTTP_CODE)${NC}"
    fi
    echo ""
}

# ============================================
# TESTE 3: API Info
# ============================================
test_info() {
    echo -e "${BLUE}[TESTE 3] Informações da API...${NC}"
    
    RESPONSE=$(curl -s ${API_URL}/api/info)
    
    if echo "$RESPONSE" | jq . > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Informações obtidas${NC}"
        echo "$RESPONSE" | jq '.'
    else
        echo -e "${RED}✗ Erro ao obter informações${NC}"
    fi
    echo ""
}

# ============================================
# TESTE 4: Processar PDF (Assíncrono)
# ============================================
test_process_async() {
    echo -e "${BLUE}[TESTE 4] Processamento Assíncrono...${NC}"
    
    create_test_pdf
    
    echo "   Enviando arquivo: $TEST_PDF"
    RESPONSE=$(curl -s -X POST ${API_URL}/api/pdf/process \
        -F "file=@${TEST_PDF}")
    
    if echo "$RESPONSE" | jq . > /dev/null 2>&1; then
        JOB_ID=$(echo "$RESPONSE" | jq -r '.jobId')
        
        if [ "$JOB_ID" != "null" ] && [ -n "$JOB_ID" ]; then
            echo -e "${GREEN}✓ Job criado: ${JOB_ID}${NC}"
            echo "$RESPONSE" | jq '.'
            
            # Monitorar status
            echo ""
            echo "   Monitorando status..."
            MAX_ATTEMPTS=60  # 2 minutos (60 x 2s)
            ATTEMPT=0
            
            while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
                STATUS_RESPONSE=$(curl -s ${API_URL}/api/jobs/${JOB_ID}/status)
                STATUS=$(echo "$STATUS_RESPONSE" | jq -r '.status')
                
                echo -ne "\r   Status: ${STATUS}... (${ATTEMPT}s)"
                
                if [ "$STATUS" = "completed" ]; then
                    echo ""
                    echo -e "${GREEN}✓ Processamento concluído!${NC}"
                    echo "$STATUS_RESPONSE" | jq '.'
                    
                    # Testar download
                    echo ""
                    echo "   Testando download..."
                    curl -s ${API_URL}/api/jobs/${JOB_ID}/download \
                        -o output_${JOB_ID}.pdf
                    
                    if [ -f "output_${JOB_ID}.pdf" ]; then
                        SIZE=$(stat -f%z "output_${JOB_ID}.pdf" 2>/dev/null || stat -c%s "output_${JOB_ID}.pdf")
                        echo -e "${GREEN}✓ Download concluído (${SIZE} bytes)${NC}"
                        rm "output_${JOB_ID}.pdf"
                    else
                        echo -e "${RED}✗ Erro no download${NC}"
                    fi
                    
                    return 0
                elif [ "$STATUS" = "failed" ]; then
                    echo ""
                    echo -e "${RED}✗ Processamento falhou${NC}"
                    echo "$STATUS_RESPONSE" | jq '.'
                    return 1
                fi
                
                sleep 2
                ATTEMPT=$((ATTEMPT + 2))
            done
            
            echo ""
            echo -e "${RED}✗ Timeout: Processamento demorou muito${NC}"
            return 1
        else
            echo -e "${RED}✗ Job ID não retornado${NC}"
            echo "$RESPONSE"
            return 1
        fi
    else
        echo -e "${RED}✗ Erro na requisição${NC}"
        echo "$RESPONSE"
        return 1
    fi
    echo ""
}

# ============================================
# TESTE 5: Listar Jobs
# ============================================
test_list_jobs() {
    echo -e "${BLUE}[TESTE 5] Listar Jobs...${NC}"
    
    RESPONSE=$(curl -s "${API_URL}/api/jobs?page=1&pageSize=5")
    
    if echo "$RESPONSE" | jq . > /dev/null 2>&1; then
        TOTAL=$(echo "$RESPONSE" | jq -r '.totalJobs')
        echo -e "${GREEN}✓ Total de jobs: ${TOTAL}${NC}"
        echo "$RESPONSE" | jq '.jobs[] | {jobId, fileName, status}'
    else
        echo -e "${RED}✗ Erro ao listar jobs${NC}"
    fi
    echo ""
}

# ============================================
# TESTE 6: Estatísticas
# ============================================
test_stats() {
    echo -e "${BLUE}[TESTE 6] Estatísticas...${NC}"
    
    RESPONSE=$(curl -s ${API_URL}/api/jobs/stats)
    
    if echo "$RESPONSE" | jq . > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Estatísticas obtidas${NC}"
        echo "$RESPONSE" | jq '.'
    else
        echo -e "${RED}✗ Erro ao obter estatísticas${NC}"
    fi
    echo ""
}

# ============================================
# TESTE 7: Erro - Arquivo muito grande
# ============================================
test_error_large_file() {
    echo -e "${BLUE}[TESTE 7] Validação - Arquivo muito grande...${NC}"
    
    # Criar arquivo de 11MB (excede limite de 10MB)
    dd if=/dev/zero of=large.pdf bs=1M count=11 2>/dev/null
    
    RESPONSE=$(curl -s -X POST ${API_URL}/api/pdf/process \
        -F "file=@large.pdf")
    
    ERROR=$(echo "$RESPONSE" | jq -r '.error')
    
    if [ "$ERROR" = "Arquivo muito grande" ]; then
        echo -e "${GREEN}✓ Validação funcionando corretamente${NC}"
    else
        echo -e "${YELLOW}⚠️  Validação não detectou arquivo grande${NC}"
    fi
    
    rm large.pdf
    echo ""
}

# ============================================
# TESTE 8: Erro - Tipo inválido
# ============================================
test_error_invalid_type() {
    echo -e "${BLUE}[TESTE 8] Validação - Tipo de arquivo inválido...${NC}"
    
    # Criar arquivo TXT
    echo "Teste" > test.txt
    
    RESPONSE=$(curl -s -X POST ${API_URL}/api/pdf/process \
        -F "file=@test.txt")
    
    ERROR=$(echo "$RESPONSE" | jq -r '.error')
    
    if [[ "$ERROR" == *"inválido"* ]]; then
        echo -e "${GREEN}✓ Validação de tipo funcionando${NC}"
    else
        echo -e "${YELLOW}⚠️  Validação não detectou tipo inválido${NC}"
    fi
    
    rm test.txt
    echo ""
}

# ============================================
# EXECUTAR TODOS OS TESTES
# ============================================
main() {
    FAILED=0
    
    test_health || FAILED=$((FAILED + 1))
    test_swagger || FAILED=$((FAILED + 1))
    test_info || FAILED=$((FAILED + 1))
    test_process_async || FAILED=$((FAILED + 1))
    test_list_jobs || FAILED=$((FAILED + 1))
    test_stats || FAILED=$((FAILED + 1))
    test_error_large_file || FAILED=$((FAILED + 1))
    test_error_invalid_type || FAILED=$((FAILED + 1))
    
    echo "=================================="
    if [ $FAILED -eq 0 ]; then
        echo -e "${GREEN}✅ TODOS OS TESTES PASSARAM!${NC}"
    else
        echo -e "${RED}❌ ${FAILED} TESTE(S) FALHARAM${NC}"
    fi
    echo "=================================="
    
    # Limpar arquivos temporários
    rm -f test.pdf
    
    exit $FAILED
}

# Executar
main