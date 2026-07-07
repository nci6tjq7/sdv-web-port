using System;
using System.Collections.Generic;
using System.Linq;

namespace SdvWebPort.Vfs.StubHelpers;

/// <summary>
/// Stub helper methods that replace KNI/MonoGame API calls which throw
/// NotImplementedException in Blazor.GL. The Cecil rewriter redirects calls
/// like GraphicsAdapter.get_SupportedDisplayModes to these stubs, which
/// return empty/default values instead of throwing.
/// </summary>
public static class GraphicsStubs
{
    /// <summary>
    /// Returns an empty array instead of GraphicsAdapter.SupportedDisplayModes
    /// (which throws NotImplementedException in KNI Blazor.GL).
    /// SDV uses this to enumerate display modes for fullscreen setup.
    /// An empty array means "no modes available" — SDV will fall back to defaults.
    /// </summary>
    public static object[] GetEmptyDisplayModes()
    {
        return Array.Empty<object>();
    }

    /// <summary>
    /// Returns null for GraphicsAdapter.CurrentDisplayMode
    /// (SDV checks for null and handles it)
    /// </summary>
    public static object? GetCurrentDisplayMode()
    {
        return null;
    }

    /// <summary>
    /// Returns null for GraphicsAdapter.DefaultAdapter
    /// SDV creates its own GraphicsDevice via GraphicsDeviceManager, so
    /// the adapter isn't strictly needed.
    /// </summary>
    public static object? GetDefaultAdapter()
    {
        return null;
    }

    /// <summary>
    /// Generic null returner for any method that returns a reference type.
    /// Used as a catch-all for methods we don't have a specific stub for.
    /// </summary>
    public static object? GetNull()
    {
        return null;
    }

    /// <summary>
    /// Returns 0 for any method that returns a numeric type.
    /// </summary>
    public static int GetZero()
    {
        return 0;
    }

    /// <summary>
    /// Returns false for any method that returns bool.
    /// </summary>
    public static bool GetFalse()
    {
        return false;
    }
}
