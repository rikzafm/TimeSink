using System.Security.Principal;
using System.Text.Json.Serialization;

namespace TimeSink.Core.Ipc;

/// <summary>Messages on the named pipe (implementation.md section 7). Only what exists so far; later tickets add the rest.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GetStateMessage), "getState")]
[JsonDerivedType(typeof(StateMessage), "state")]
public abstract record IpcMessage;

/// <summary>UI to Agent: send me the current state.</summary>
public sealed record GetStateMessage : IpcMessage;

public sealed record RunningApp(long AppId, long SessionStartUtc);

/// <summary>Agent to UI: pushed on change, and the reply to <see cref="GetStateMessage"/>.</summary>
public sealed record StateMessage(bool Paused, IReadOnlyList<RunningApp> Running) : IpcMessage;

[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(StateMessage))]
[JsonSerializable(typeof(GetStateMessage))]
internal sealed partial class IpcJson : JsonSerializerContext;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class PipeNames
{
    /// <summary><c>TimeSink.&lt;userSid&gt;</c> (the pipe itself is also created current-user-only).</summary>
    public static string ForCurrentUser() => "TimeSink." + (WindowsIdentity.GetCurrent().User?.Value ?? "user");
}
