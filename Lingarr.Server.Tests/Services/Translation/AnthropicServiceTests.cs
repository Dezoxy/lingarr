using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class AnthropicServiceTests
{
    private const string Translations =
        "[{\"position\":1,\"line\":\"Hola\"},{\"position\":2,\"line\":\"Mundo\"}]";

    private readonly Mock<ISettingService> _settingsMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly AnthropicService _service;
    private readonly List<string> _requestBodies = new();

    public AnthropicServiceTests()
    {
        _settingsMock = new Mock<ISettingService>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        _settingsMock.Setup(settingService => settingService.GetEncryptedSetting(It.IsAny<string>()))
            .ReturnsAsync("test-key");
        _settingsMock.Setup(settingService => settingService.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Translation.Anthropic.Model, "claude-sonnet-5-5" },
                { SettingKeys.Translation.Anthropic.Version, "2023-06-01" },
                { SettingKeys.Translation.Anthropic.RequestTemplate, "" },
                { SettingKeys.Translation.AiPrompt, "Translate from {sourceLanguage} to {targetLanguage}" },
                { SettingKeys.Translation.AiUserPrompt, "{lineToTranslate}" },
                { SettingKeys.Translation.ProofreadPrompt, "" },
                { SettingKeys.Translation.ProofreadUserPrompt, "" },
                { SettingKeys.Translation.RequestTimeout, "5" },
                { SettingKeys.Translation.MaxRetries, "3" },
                { SettingKeys.Translation.RetryDelay, "0" },
                { SettingKeys.Translation.RetryDelayMultiplier, "1" },
                { SettingKeys.Translation.LanguageCodeFormat, "false" }
            });

        _service = new AnthropicService(
            _settingsMock.Object,
            new HttpClient(_httpMessageHandlerMock.Object),
            new Mock<ILogger<AnthropicService>>().Object,
            new LanguageCodeService(),
            new RequestTemplateService());
    }

    [Fact]
    public async Task TranslateBatchAsync_ShouldSendStrictToolWithAutoToolChoice()
    {
        // Arrange
        SetupResponse(ToolUseResponse(JsonNode.Parse(Translations)!));

        // Act
        await _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None);

        // Assert
        var body = JsonNode.Parse(Assert.Single(_requestBodies))!;
        Assert.Equal("auto", body["tool_choice"]!["type"]!.GetValue<string>());
        Assert.True(body["tool_choice"]!["disable_parallel_tool_use"]!.GetValue<bool>());
        Assert.Equal(16000, body["max_tokens"]!.GetValue<int>());
        Assert.Contains("record_translation_batch", body["system"]!.GetValue<string>());

        var tool = body["tools"]![0]!;
        Assert.True(tool["strict"]!.GetValue<bool>());
        Assert.False(tool["input_schema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["input_schema"]!["properties"]!["translations"]!["items"]!["additionalProperties"]!
            .GetValue<bool>());
    }

    [Fact]
    public async Task TranslateBatchAsync_ShouldParseTranslationsArray()
    {
        // Arrange
        SetupResponse(ToolUseResponse(JsonNode.Parse(Translations)!));

        // Act
        var result = await _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
    }

    [Fact]
    public async Task TranslateBatchAsync_ShouldParseTranslationsSentAsJsonString()
    {
        // Arrange: claude-haiku-4-5 returned the array serialized as a string when the tool was not strict
        SetupResponse(ToolUseResponse(JsonValue.Create(Translations)));

        // Act
        var result = await _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
    }

    private static List<BatchSubtitleItem> Batch() =>
    [
        new() { Position = 1, Line = "Hello" },
        new() { Position = 2, Line = "World" }
    ];

    private static HttpResponseMessage ToolUseResponse(JsonNode translations)
    {
        var body = new JsonObject
        {
            ["stop_reason"] = "tool_use",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_use",
                ["name"] = "record_translation_batch",
                ["input"] = new JsonObject { ["translations"] = translations }
            })
        };
        return new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }

    private void SetupResponse(HttpResponseMessage response)
    {
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                _requestBodies.Add(request.Content!.ReadAsStringAsync().Result))
            .ReturnsAsync(response);
    }
}
