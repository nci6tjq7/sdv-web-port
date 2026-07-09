using System;

namespace SdvWebPort.Vfs
{
    /// <summary>
    /// Runtime helper for boxing generic parameter values.
    /// Used to avoid the Mono WASM JIT transform.c:1146 assertion that occurs
    /// when the interpreter tries to JIT-compile a `box T` instruction where T
    /// is a generic parameter.
    ///
    /// The IL rewriter replaces `box T` with `call BoxHelper.Box<T>(T)`.
    /// This method uses RuntimeHelpers.GetObjectValue which does NOT emit
    /// `box T` in the IL — it emits a `call` to a runtime helper instead.
    /// </summary>
    public static class BoxHelper
    {
        /// <summary>
        /// Box a value of generic type T into an object.
        /// For reference types, returns the value as-is (no boxing needed).
        /// For value types, boxes the value into an object.
        ///
        /// RuntimeHelpers.GetObjectValue is a JIT intrinsic that boxes value types
        /// and passes reference types through, without emitting `box T` in the IL.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static object? Box<T>(T value)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(value);
        }
    }
}
