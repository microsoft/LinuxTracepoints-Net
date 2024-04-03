namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Globalization;
    using System.Text;
    using static System.Net.Mime.MediaTypeNames;

    public static class PerfConvert
    {
        private const long UnixEpochSeconds = 62135596800;
        private const long DaysToYear10000 = 3652059;
        private const long SecondsPerDay = 60 * 60 * 24;
        private const long MaxSeconds = DaysToYear10000 * SecondsPerDay - UnixEpochSeconds;

        private static Encoding? encodingLatin1; // ISO-8859-1
        private static Encoding? encodingUTF32BE;

        /// <summary>
        /// Gets an encoding for ISO-8859-1 (Latin-1) characters.
        /// </summary>
        public static Encoding EncodingLatin1
        {
            get
            {
                var encoding = encodingLatin1; // Get the cached encoding, if available.
                if (encoding == null)
                {
                    encoding = Encoding.GetEncoding(28591); // Create a new encoding.
                    encodingLatin1 = encoding; // Cache the encoding.
                }

                return encoding;
            }
        }

        /// <summary>
        /// Gets an encoding for UTF-32 big-endian characters.
        /// </summary>
        public static Encoding EncodingUTF32BE
        {
            get
            {
                var encoding = encodingUTF32BE; // Get the cached encoding, if available.
                if (encoding == null)
                {
                    encoding = new UTF32Encoding(true, true); // Create a new encoding.
                    encodingUTF32BE = encoding; // Cache the encoding.
                }

                return encoding;
            }
        }

        public static char ToHexChar(int nibble)
        {
            const string HexChars = "0123456789ABCDEF";
            return HexChars[nibble & 0xF];
        }

        public static string ToHexString(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > 0)
            {
                var sb = new StringBuilder(bytes.Length * 3 - 1);
                Formatting.PerfFormattingExtensions.AppendHexBytes(sb, bytes);
                return sb.ToString();
            }

            return "";
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a DateTime.
        /// </summary>
        public static DateTime UnixTime32ToDateTime(Int32 secondsSince1970)
        {
            return new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a new string
        /// like "2020-02-02T02:02:02".
        /// </summary>
        public static string UnixTime32ToString(Int32 secondsSince1970)
        {
            return PerfConvert.UnixTime32ToDateTime(secondsSince1970)
                .ToString("s", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Attempts to convert a 64-bit Unix time_t (signed seconds since 1970) to a DateTime.
        /// Returns null if the result is less than DateTime.MinValue or greater than
        /// DateTime.MaxValue.
        /// </summary>
        public static DateTime? UnixTime64ToDateTime(Int64 secondsSince1970)
        {
            if (secondsSince1970 < -UnixEpochSeconds ||
                secondsSince1970 >= MaxSeconds)
            {
                return null;
            }
            else
            {
                return new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// Converts a 64-bit Unix time_t (signed seconds since 1970) to a new string.
        /// If year is in range 0001..9999, returns a value like "2020-02-02T02:02:02".
        /// If year is outside of 0001..9999, returns a value like "TIME(-1234567890)".
        /// </summary>
        public static string UnixTime64ToString(Int64 secondsSince1970)
        {
            var maybe = PerfConvert.UnixTime64ToDateTime(secondsSince1970);
            if (maybe is DateTime value)
            {
                return value.ToString("s", CultureInfo.InvariantCulture);
            }
            else
            {
                return "TIME(" + secondsSince1970.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "OK(0)" or "ENOENT(2)". Otherwise returns null.
        /// </summary>
        public static string? ErrnoLookup(Int32 linuxErrno)
        {
            switch (linuxErrno)
            {
                default: return null;
                case 0: return "OK(0)";
                case 1: return "EPERM(1)";
                case 2: return "ENOENT(2)";
                case 3: return "ESRCH(3)";
                case 4: return "EINTR(4)";
                case 5: return "EIO(5)";
                case 6: return "ENXIO(6)";
                case 7: return "E2BIG(7)";
                case 8: return "ENOEXEC(8)";
                case 9: return "EBADF(9)";
                case 10: return "ECHILD(10)";
                case 11: return "EAGAIN(11)";
                case 12: return "ENOMEM(12)";
                case 13: return "EACCES(13)";
                case 14: return "EFAULT(14)";
                case 15: return "ENOTBLK(15)";
                case 16: return "EBUSY(16)";
                case 17: return "EEXIST(17)";
                case 18: return "EXDEV(18)";
                case 19: return "ENODEV(19)";
                case 20: return "ENOTDIR(20)";
                case 21: return "EISDIR(21)";
                case 22: return "EINVAL(22)";
                case 23: return "ENFILE(23)";
                case 24: return "EMFILE(24)";
                case 25: return "ENOTTY(25)";
                case 26: return "ETXTBSY(26)";
                case 27: return "EFBIG(27)";
                case 28: return "ENOSPC(28)";
                case 29: return "ESPIPE(29)";
                case 30: return "EROFS(30)";
                case 31: return "EMLINK(31)";
                case 32: return "EPIPE(32)";
                case 33: return "EDOM(33)";
                case 34: return "ERANGE(34)";
                case 35: return "EDEADLK(35)";
                case 36: return "ENAMETOOLONG(36)";
                case 37: return "ENOLCK(37)";
                case 38: return "ENOSYS(38)";
                case 39: return "ENOTEMPTY(39)";
                case 40: return "ELOOP(40)";
                case 42: return "ENOMSG(42)";
                case 43: return "EIDRM(43)";
                case 44: return "ECHRNG(44)";
                case 45: return "EL2NSYNC(45)";
                case 46: return "EL3HLT(46)";
                case 47: return "EL3RST(47)";
                case 48: return "ELNRNG(48)";
                case 49: return "EUNATCH(49)";
                case 50: return "ENOCSI(50)";
                case 51: return "EL2HLT(51)";
                case 52: return "EBADE(52)";
                case 53: return "EBADR(53)";
                case 54: return "EXFULL(54)";
                case 55: return "ENOANO(55)";
                case 56: return "EBADRQC(56)";
                case 57: return "EBADSLT(57)";
                case 59: return "EBFONT(59)";
                case 60: return "ENOSTR(60)";
                case 61: return "ENODATA(61)";
                case 62: return "ETIME(62)";
                case 63: return "ENOSR(63)";
                case 64: return "ENONET(64)";
                case 65: return "ENOPKG(65)";
                case 66: return "EREMOTE(66)";
                case 67: return "ENOLINK(67)";
                case 68: return "EADV(68)";
                case 69: return "ESRMNT(69)";
                case 70: return "ECOMM(70)";
                case 71: return "EPROTO(71)";
                case 72: return "EMULTIHOP(72)";
                case 73: return "EDOTDOT(73)";
                case 74: return "EBADMSG(74)";
                case 75: return "EOVERFLOW(75)";
                case 76: return "ENOTUNIQ(76)";
                case 77: return "EBADFD(77)";
                case 78: return "EREMCHG(78)";
                case 79: return "ELIBACC(79)";
                case 80: return "ELIBBAD(80)";
                case 81: return "ELIBSCN(81)";
                case 82: return "ELIBMAX(82)";
                case 83: return "ELIBEXEC(83)";
                case 84: return "EILSEQ(84)";
                case 85: return "ERESTART(85)";
                case 86: return "ESTRPIPE(86)";
                case 87: return "EUSERS(87)";
                case 88: return "ENOTSOCK(88)";
                case 89: return "EDESTADDRREQ(89)";
                case 90: return "EMSGSIZE(90)";
                case 91: return "EPROTOTYPE(91)";
                case 92: return "ENOPROTOOPT(92)";
                case 93: return "EPROTONOSUPPORT(93)";
                case 94: return "ESOCKTNOSUPPORT(94)";
                case 95: return "EOPNOTSUPP(95)";
                case 96: return "EPFNOSUPPORT(96)";
                case 97: return "EAFNOSUPPORT(97)";
                case 98: return "EADDRINUSE(98)";
                case 99: return "EADDRNOTAVAIL(99)";
                case 100: return "ENETDOWN(100)";
                case 101: return "ENETUNREACH(101)";
                case 102: return "ENETRESET(102)";
                case 103: return "ECONNABORTED(103)";
                case 104: return "ECONNRESET(104)";
                case 105: return "ENOBUFS(105)";
                case 106: return "EISCONN(106)";
                case 107: return "ENOTCONN(107)";
                case 108: return "ESHUTDOWN(108)";
                case 109: return "ETOOMANYREFS(109)";
                case 110: return "ETIMEDOUT(110)";
                case 111: return "ECONNREFUSED(111)";
                case 112: return "EHOSTDOWN(112)";
                case 113: return "EHOSTUNREACH(113)";
                case 114: return "EALREADY(114)";
                case 115: return "EINPROGRESS(115)";
                case 116: return "ESTALE(116)";
                case 117: return "EUCLEAN(117)";
                case 118: return "ENOTNAM(118)";
                case 119: return "ENAVAIL(119)";
                case 120: return "EISNAM(120)";
                case 121: return "EREMOTEIO(121)";
                case 122: return "EDQUOT(122)";
                case 123: return "ENOMEDIUM(123)";
                case 124: return "EMEDIUMTYPE(124)";
                case 125: return "ECANCELED(125)";
                case 126: return "ENOKEY(126)";
                case 127: return "EKEYEXPIRED(127)";
                case 128: return "EKEYREVOKED(128)";
                case 129: return "EKEYREJECTED(129)";
                case 130: return "EOWNERDEAD(130)";
                case 131: return "ENOTRECOVERABLE(131)";
                case 132: return "ERFKILL(132)";
                case 133: return "EHWPOISON(133)";
            }
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "OK(0)" or "ENOENT(2)". Otherwise returns a new string like
        /// "ERRNO(404)".
        /// </summary>
        public static string ErrnoToString(Int32 linuxErrno)
        {
            var value = ErrnoLookup(linuxErrno);
            return value != null
                ? value
                : "ERRNO(" + linuxErrno.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// Converts an integer value from a boolean field into a string.
        /// If value is 0/1, returns "false"/"true".
        /// Otherwise, returns value formatted as a signed integer.
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to TryGetUInt32,
        /// not a call to TryGetInt32.
        /// </summary>
        public static string BooleanToString(UInt32 value)
        {
            switch (value)
            {
                case 0: return "false";
                case 1: return "true";
                default: return unchecked((int)value).ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
