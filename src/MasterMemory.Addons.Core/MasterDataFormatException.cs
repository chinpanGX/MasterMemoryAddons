using System;

namespace MasterMemory.Addons
{
    /// <summary><see cref="MasterDataBinary.Read"/> で、バイナリを読めないときの例外。理由は <see cref="Reason"/>。</summary>
    public sealed class MasterDataFormatException : Exception
    {
        public MasterDataFormatException(MasterDataFormatErrorReason reason, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Reason = reason;
        }

        public MasterDataFormatErrorReason Reason { get; }
    }
}
