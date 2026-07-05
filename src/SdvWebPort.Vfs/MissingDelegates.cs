using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SdvWebPort.Vfs.DelegateReplacements;

/// <summary>
/// Replacement delegate types for high-arity Action/Func that the
/// BlazorWebAssembly trimmer strips from System.Private.CoreLib.
///
/// SDV's Game1 type has fields like `Action<T1..T7>` and `Func<T1..T6,TResult>`
/// that the trimmer removes. We define equivalent delegate types here, and the
/// Cecil rewriter rewrites SDV's typerefs to point at these instead.
///
/// Only the arities that are actually missing from the WASM CoreLib are
/// defined here (Action 7-16, Func 6+1 to 16+1). Lower arities are kept by
/// the trimmer.
/// </summary>
public static class MissingDelegates
{
    // Marker — Cecil rewriter identifies replacement delegates via namespace
}

// Action delegates (arity 7-16) — these are stripped from WASM CoreLib
public delegate void Action<T1, T2, T3, T4, T5, T6, T7>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14, T15 a15);
public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14, T15 a15, T16 a16);

// Func delegates (arity 6-17 args, last is TResult) — these are stripped from WASM CoreLib
public delegate TResult Func<T1, T2, T3, T4, T5, T6, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14, T15 a15);
public delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14, T15 a15, T16 a16);
