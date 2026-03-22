using System.Diagnostics;
using System.Text.Json.Serialization;
using OllamaSharp.Models.Chat;

namespace AiApiEndpoints.Models;

public class AiResponse
{
    public string model { get; set; }
    public string created_at { get; set; }
    public Message message { get; set; }
    public bool done { get; set; }
}


/// <summary>
/// Represents a message in a chat.
/// </summary>
[DebuggerDisplay("{Role}: {Content}")]
public class Message
{
	/// <summary>
	/// Initializes a new instance of the <see cref="Message"/> class with the specified role, content, and images.
	/// </summary>
	/// <param name="role">The role of the message, either system, user, or assistant.</param>
	/// <param name="content">The content of the message.</param>
	/// <param name="images">An array of base64-encoded images.</param>
	public Message(Role role, string content, string[]? images)
	{
		Role = role;
		Content = content;
		Images = images;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Message"/> class with the specified role and images.
	/// </summary>
	/// <param name="role">The role of the message, either system, user, or assistant.</param>
	/// <param name="images">An array of base64-encoded images.</param>
	public Message(Role role, string[] images)
	{
		Role = role;
		Images = images;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Message"/> class with the specified role and content.
	/// </summary>
	/// <param name="role">The role of the message, either system, user, or assistant.</param>
	/// <param name="content">The content of the message.</param>
	public Message(Role? role, string content)
	{
		Role = role;
		Content = content;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Message"/> class.
	/// Required for JSON deserialization.
	/// </summary>
	public Message()
	{
	}

	/// <summary>
	/// Gets or sets the role of the message, either system, user, or assistant.
	/// </summary>
	[JsonPropertyName("role")]
	public Role? Role { get; set; }

	/// <summary>
	/// Gets or sets the content of the message.
	/// </summary>
	[JsonPropertyName("content")]
	public string? Content { get; set; }

	/// <summary>
	/// Gets or sets an array of base64-encoded images (for multimodal models such as llava).
	/// </summary>
	[JsonPropertyName("images")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? Images { get; set; }

	/// <summary>
	/// Gets or sets the parsed content of the thinking and reasoning. To make this work, enable <see cref="ChatRequest.Think"/>.
	/// </summary>
	[JsonPropertyName("thinking")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Thinking { get; set; }

	/// <summary>
	/// Gets or sets the name of the tool that was executed to inform the model of the result.
	/// </summary>
	[JsonPropertyName("tool_name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ToolName { get; set; }

	/// <summary>
	/// Gets or sets a list of tools the model wants to use (for models that support function calls, such as qwen3.5).
	/// </summary>
	[JsonPropertyName("tool_calls")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IEnumerable<ToolCall>? ToolCalls { get; set; }

	/// <summary>
	/// Represents a tool call within a message.
	/// </summary>
	public class ToolCall
	{
		/// <summary>
		/// Gets or sets the function to be called by the tool.
		/// </summary>
		[JsonPropertyName("function")]
		public Function? Function { get; set; }

		/// <summary>
		/// Gets or sets the ID of the ToolCall.
		/// </summary>
		[JsonPropertyName("id")]
		public string? Id { get; set; }
	}

	/// <summary>
	/// Represents a function that can be called by a tool.
	/// </summary>
	public class Function
	{
		/// <summary>
		/// Gets or sets the index of the function.
		/// </summary>
		[JsonPropertyName("index")]
		public int? Index { get; set; }

		/// <summary>
		/// Gets or sets the name of the function.
		/// </summary>
		[JsonPropertyName("name")]
		public string? Name { get; set; }

		/// <summary>
		/// Gets or sets the arguments for the function, represented as a dictionary of argument names and values.
		/// </summary>
		[JsonPropertyName("arguments")]
		public IDictionary<string, object?>? Arguments { get; set; }
	}
}

public enum Role
{
    system, user, assistant, tool
}

public class ChatResponseStream
{
    private DateTimeOffset? _createdAt = null!;
    private string? _createdAtString = null!;

    /// <summary>
    /// Gets or sets the model that generated the response.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = null!;

    /// <summary>
    /// Gets or sets the time the response was generated. 
    /// </summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAtString
    {
        get => _createdAtString;
        set
        {
            _createdAtString = value;
            _createdAt = DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var createdAt) ? createdAt : null;
        }
    }

    /// <summary>
    /// Gets or sets the time the response was generated.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? CreatedAt
    {
        get => _createdAt;
        set
        {
            _createdAt = value;
            _createdAtString = value?.ToString("o");
        }
    }

    /// <summary>
    /// Gets or sets the message returned by the model.
    /// </summary>
    [JsonPropertyName("message")]
    public Message Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the response is complete.
    /// </summary>
    [JsonPropertyName("done")]
    public bool Done { get; set; }
    
    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }
}