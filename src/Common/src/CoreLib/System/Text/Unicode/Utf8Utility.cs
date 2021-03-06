// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace System.Text.Unicode
{
    internal static class Utf8Utility
    {
        /// <summary>
        /// The maximum number of bytes that can result from UTF-8 transcoding
        /// any Unicode scalar value.
        /// </summary>
        internal const int MaxBytesPerScalar = 4;

        /// <summary>
        /// The UTF-8 representation of <see cref="UnicodeUtility.ReplacementChar"/>.
        /// </summary>
        private static ReadOnlySpan<byte> ReplacementCharSequence => new byte[] { 0xEF, 0xBF, 0xBD };

        /// <summary>
        /// Returns the byte index in <paramref name="utf8Data"/> where the first invalid UTF-8 sequence begins,
        /// or -1 if the buffer contains no invalid sequences. Also outs the <paramref name="isAscii"/> parameter
        /// stating whether all data observed (up to the first invalid sequence or the end of the buffer, whichever
        /// comes first) is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIndexOfFirstInvalidUtf8Sequence(ReadOnlySpan<byte> utf8Data, out bool isAscii)
        {
            // TODO_UTF8STRING: Replace this with the faster drop-in replacement when it's available (coreclr #21948).

            bool tempIsAscii = true;
            int originalDataLength = utf8Data.Length;

            while (!utf8Data.IsEmpty)
            {
                if (Rune.DecodeFromUtf8(utf8Data, out Rune result, out int bytesConsumed) != OperationStatus.Done)
                {
                    break;
                }

                tempIsAscii &= result.IsAscii;
                utf8Data = utf8Data.Slice(bytesConsumed);
            }

            isAscii = tempIsAscii;
            return (utf8Data.IsEmpty) ? -1 : (originalDataLength - utf8Data.Length);
        }

#if FEATURE_UTF8STRING
        /// <summary>
        /// Returns <paramref name="value"/> if it is null or contains only well-formed UTF-8 data;
        /// otherwises allocates a new <see cref="Utf8String"/> instance containing the same data as
        /// <paramref name="value"/> but where all invalid UTF-8 sequences have been replaced
        /// with U+FFD.
        /// </summary>
        public static Utf8String ValidateAndFixupUtf8String(Utf8String value)
        {
            if (Utf8String.IsNullOrEmpty(value))
            {
                return value;
            }

            ReadOnlySpan<byte> valueAsBytes = value.AsBytes();

            int idxOfFirstInvalidData = GetIndexOfFirstInvalidUtf8Sequence(valueAsBytes, out _);
            if (idxOfFirstInvalidData < 0)
            {
                return value;
            }

            // TODO_UTF8STRING: Replace this with the faster implementation once it's available.
            // (The faster implementation is in the dev/utf8string_bak branch currently.)

            MemoryStream memStream = new MemoryStream();
            memStream.Write(valueAsBytes.Slice(0, idxOfFirstInvalidData));

            valueAsBytes = valueAsBytes.Slice(idxOfFirstInvalidData);
            do
            {
                if (Rune.DecodeFromUtf8(valueAsBytes, out _, out int bytesConsumed) == OperationStatus.Done)
                {
                    //  Valid scalar value - copy data as-is to MemoryStream
                    memStream.Write(valueAsBytes.Slice(0, bytesConsumed));
                }
                else
                {
                    // Invalid scalar value - copy U+FFFD to MemoryStream
                    memStream.Write(ReplacementCharSequence);
                }

                valueAsBytes = valueAsBytes.Slice(bytesConsumed);
            } while (!valueAsBytes.IsEmpty);

            bool success = memStream.TryGetBuffer(out ArraySegment<byte> memStreamBuffer);
            Debug.Assert(success, "Couldn't get underlying MemoryStream buffer.");

            return Utf8String.DangerousCreateWithoutValidation(memStreamBuffer, assumeWellFormed: true);
        }
#endif // FEATURE_UTF8STRING
    }
}
