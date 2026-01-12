using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;
using System.Security.Claims;

namespace pdf_ocr.Controllers
{
    /// <summary>
    /// Gerenciamento de chaves de API
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requer autenticação JWT (usuário web)
    public class ApiKeysController : ControllerBase
    {
        private readonly IApiKeyService _apiKeyService;
        private readonly ILogger<ApiKeysController> _logger;

        public ApiKeysController(IApiKeyService apiKeyService, ILogger<ApiKeysController> logger)
        {
            _apiKeyService = apiKeyService;
            _logger = logger;
        }

        /// <summary>
        /// Cria uma nova chave de API
        /// </summary>
        /// <remarks>
        /// ⚠️ IMPORTANTE: A chave em plain-text é retornada APENAS nesta resposta.
        /// Salve-a imediatamente, pois não será possível recuperá-la depois.
        /// </remarks>
        [HttpPost]
        [ProducesResponseType(typeof(ApiKeyResponse), 201)]
        public async Task<IActionResult> CreateKey([FromBody] CreateApiKeyRequest request)
        {
            try
            {
                var userId = GetUserId();

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new { error = "Nome da chave é obrigatório" });
                }

                var apiKey = await _apiKeyService.CreateKeyAsync(userId, request);

                return CreatedAtAction(
                    nameof(GetKeys),
                    new { id = apiKey.Id },
                    apiKey
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar API Key");
                return StatusCode(500, new { error = "Erro ao criar chave" });
            }
        }

        /// <summary>
        /// Lista todas as chaves do usuário
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<ApiKeyResponse>), 200)]
        public async Task<IActionResult> GetKeys()
        {
            try
            {
                var userId = GetUserId();
                var keys = await _apiKeyService.GetUserKeysAsync(userId);

                return Ok(keys);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao listar chaves");
                return StatusCode(500, new { error = "Erro ao listar chaves" });
            }
        }

        /// <summary>
        /// Revoga (desativa) uma chave
        /// </summary>
        [HttpDelete("{keyId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RevokeKey(Guid keyId)
        {
            try
            {
                var userId = GetUserId();
                var success = await _apiKeyService.RevokeKeyAsync(userId, keyId);

                if (!success)
                {
                    return NotFound(new { error = "Chave não encontrada" });
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao revogar chave: {KeyId}", keyId);
                return StatusCode(500, new { error = "Erro ao revogar chave" });
            }
        }

        /// <summary>
        /// Testa se uma chave é válida (útil para debug)
        /// </summary>
        [HttpPost("validate")]
        [AllowAnonymous]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> ValidateKey([FromBody] ValidateKeyRequest request)
        {
            try
            {
                var userId = await _apiKeyService.ValidateKeyAsync(request.ApiKey);

                if (userId == null)
                {
                    return Unauthorized(new { error = "Chave inválida ou expirada" });
                }

                return Ok(new { valid = true, userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar chave");
                return StatusCode(500, new { error = "Erro ao validar" });
            }
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException("Usuário não autenticado");
        }
    }

    /// <summary>
    /// DTO para validação de chave
    /// </summary>
    public class ValidateKeyRequest
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}