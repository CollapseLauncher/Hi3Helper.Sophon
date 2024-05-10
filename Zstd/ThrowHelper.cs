using System;
using System.Runtime.InteropServices;

namespace ZstdNet
{
    internal static class ReturnValueExtensions
    {
        public static nuint EnsureZdictSuccess(this nuint returnValue)
        {
            if (ExternMethods.ZDICT_isError(returnValue) != 0)
                ThrowException(returnValue, Marshal.PtrToStringAnsi(ExternMethods.ZDICT_getErrorName(returnValue)) ?? $"Error is unknown: {returnValue}");
            return returnValue;
        }

        public static nuint EnsureZstdSuccess(this nuint returnValue)
        {
            if (ExternMethods.ZSTD_isError(returnValue) != 0)
                ThrowException(returnValue, Marshal.PtrToStringAnsi(ExternMethods.ZSTD_getErrorName(returnValue)) ?? $"Error is unknown: {returnValue}");
            return returnValue;
        }

        private static void ThrowException(nuint returnValue, string message)
        {
            var code = unchecked(0 - (uint)(ulong)returnValue); // Negate returnValue (UIntPtr)
            throw new ZstdException(unchecked((ZSTD_ErrorCode)code), message);
        }

        public static IntPtr EnsureZstdSuccess(this IntPtr returnValue)
        {
            if (returnValue == IntPtr.Zero)
                throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_GENERIC, "Failed to create a structure");
            return returnValue;
        }
    }

    public class ZstdException : Exception
    {
        public ZstdException(ZSTD_ErrorCode code, string message) : base(message)
            => Code = code;

        public ZSTD_ErrorCode Code { get; }
    }
}
