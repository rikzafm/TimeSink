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

    /// <summary>
    /// Per-user name, or (when a scratch database is in use via <c>--db</c> / <c>TIMESINK_DB</c>) a name derived from
    /// that path, so a test Agent and the real one never talk to each other's UI.
    /// </summary>
    public static string For(string? dbOverride)
    {
        if (string.IsNullOrEmpty(dbOverride)) return ForCurrentUser();
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dbOverride.ToLowerInvariant()));
        return ForCurrentUser() + "." + Convert.ToHexString(hash, 0, 4);
    }
}
