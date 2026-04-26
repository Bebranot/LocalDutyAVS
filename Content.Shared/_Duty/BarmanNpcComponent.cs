// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Duty;

/// <summary>
///     Marks a humanoid as an LLM-driven bartender NPC.
///     Stores configuration for the external API and persona prompt.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BarmanNpcComponent : Component
{
    /// <summary>
    /// Optional explicit name to use as a trigger in chat.
    /// If empty, the entity's displayed name is used.
    /// </summary>
    [DataField("triggerName")]
    public string? TriggerName;

    /// <summary>
    /// OpenRouter / OpenAI-compatible model identifier.
    /// Например: "openai/gpt-4o-mini".
    /// </summary>
    [DataField("model")]
    public string Model = "openai/gpt-4o-mini";

    /// <summary>
    /// Temperature for the LLM sampling (0–2).
    /// </summary>
    [DataField("temperature")]
    public float Temperature = 0.7f;

    /// <summary>
    /// Maximum tokens to ask from the model (OpenRouter/OpenAI sense).
    /// </summary>
    [DataField("maxTokens")]
    public int MaxTokens = 220;

    /// <summary>
    /// Number of most recent dialogue lines loaded from memory.
    /// </summary>
    [DataField("memoryRecentLimit")]
    public int MemoryRecentLimit = 8;

    /// <summary>
    /// Number of top facts to load for the current speaker.
    /// </summary>
    [DataField("memoryTopK")]
    public int MemoryTopK = 4;

    /// <summary>
    /// Number of top global facts to load regardless of speaker.
    /// </summary>
    [DataField("memoryGlobalTopK")]
    public int MemoryGlobalTopK = 2;

    /// <summary>
    /// Base system prompt describing the bartender's personality and role.
    /// </summary>
    [DataField("systemPrompt")]
    public string SystemPrompt =
        "Ты бармен на космической станции Space Station 14. " +
        "Отвечай от первого лица, в IC, кратко и по делу. " +
        "Ты дружелюбен, но немного циничен, знаешь бар и станцию, как свои пять пальцев.";

    /// <summary>
    /// HTTP endpoint of the LLM API.
    /// </summary>
    [DataField("apiEndpoint")]
    public string? ApiEndpoint;

    /// <summary>
    /// Optional API key that will be sent in the Authorization header as a bearer token.
    /// </summary>
    [DataField("apiKey")]
    public string? ApiKey;

    /// <summary>
    /// Maximum response length in characters. The system may still truncate longer replies.
    /// </summary>
    [DataField("maxResponseChars")]
    public int MaxResponseChars = 220;

    /// <summary>
    /// Cooldown between replies from this NPC, in seconds.
    /// </summary>
    [DataField("cooldownSeconds")]
    public float CooldownSeconds = 4f;

    /// <summary>
    /// Next time at which the NPC is allowed to answer.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextAllowedResponse;
}

