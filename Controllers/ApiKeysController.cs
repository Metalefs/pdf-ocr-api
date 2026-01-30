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
    [ApiExplorerSettings(IgnoreApi = true)] // Oculta todos os endpoints deste controller no Swagger
    public class ApiKeysController : ControllerBase
    {
        private readonly IApiKeyService _apiKeyService;
        private readonly IUserService _userService;
        private readonly ILogger<ApiKeysController> _logger;

        public ApiKeysController(IApiKeyService apiKeyService, IUserService userService, ILogger<ApiKeysController> logger)
        {
            _apiKeyService = apiKeyService;
            _userService = userService;
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
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(UserNotAuthenticatedResponse());
                }

                // Check if user's plan allows API access
                var user = await _userService.GetUserAsync(userId);
                if (user == null || user.Plan.ToLower() == "free")
                {
                    var msg = ApiMessages.ApiAccessNotAvailable(HttpContext);
                    return BadRequest(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    var msg = ApiMessages.ApiKeyNameRequired(HttpContext);
                    return BadRequest(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
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
                var msg = ApiMessages.ApiKeysCreateFailed(HttpContext);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
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
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(UserNotAuthenticatedResponse());
                }

                // Check if user's plan allows API access
                var user = await _userService.GetUserAsync(userId);
                if (user == null || user.Plan == "free")
                {
                    var msg = ApiMessages.ApiAccessNotAvailable(HttpContext);
                    return BadRequest(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                var keys = await _apiKeyService.GetUserKeysAsync(userId);

                return Ok(keys);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao listar chaves");
                var msg = ApiMessages.ApiKeysListFailed(HttpContext);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
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
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(UserNotAuthenticatedResponse());
                }

                // Check if user's plan allows API access
                var user = await _userService.GetUserAsync(userId);
                if (user == null || user.Plan == "free")
                {
                    var msg = ApiMessages.ApiAccessNotAvailable(HttpContext);
                    return BadRequest(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                var success = await _apiKeyService.RevokeKeyAsync(userId, keyId);

                if (!success)
                {
                    var msg = ApiMessages.ApiKeyNotFound(HttpContext);
                    return NotFound(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao revogar chave: {KeyId}", keyId);
                var msg = ApiMessages.ApiKeyRevokeFailed(HttpContext);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
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
                    var msg = ApiMessages.ApiKeyInvalidOrExpired(HttpContext);
                    return Unauthorized(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                return Ok(new { valid = true, userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar chave");
                var msg = ApiMessages.ApiKeyValidateFailed(HttpContext);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }
        }

        private string? GetUserIdOrNull()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private ErrorResponse UserNotAuthenticatedResponse()
        {
            var msg = ApiMessages.UserNotAuthenticated(HttpContext);
            return new ErrorResponse { Error = msg.Error, Details = msg.Details };
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