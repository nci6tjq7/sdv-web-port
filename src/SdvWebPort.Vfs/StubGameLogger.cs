using System;

namespace SdvWebPort.Vfs.StubHelpers;

/// <summary>
/// Stub IGameLogger that does nothing. Used to initialize Game1.log
/// when the static constructor is patched to no-op.
/// </summary>
public class StubGameLogger
{
    public void Verbose(string message) { }
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
    public void Critical(string message) { }
}
