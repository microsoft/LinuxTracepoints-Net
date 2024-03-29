// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Text;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using CultureInfo = System.Globalization.CultureInfo;
    using Debug = System.Diagnostics.Debug;
    using IPAddress = System.Net.IPAddress;
    using Text = System.Text;

    /// <summary>
    /// Event item attributes (attributes of a value, array, or structure within the event)
    /// returned by the GetItemInfo() method of EventHeaderEnumerator.
    /// </summary>
    public ref struct EventHeaderItemInfo
    {
        /// <summary>
        /// The Span corresponding to the eventData parameter passed to
        /// EventHeaderEnumerator.StartEvent(). For example, if you called
        /// enumerator.StartEvent(name, myData), this will be the same as myData.Span.
        /// The NameStart and ValueStart fields are relative to this span.
        /// </summary>
        public ReadOnlySpan<byte> EventData;

        /// <summary>
        /// Offset into EventData where NameBytes begins.
        /// </summary>
        public int NameStart;

        /// <summary>
        /// Length of NameBytes.
        /// </summary>
        public int NameLength;

        /// <summary>
        /// Offset into EventData where ValueBytes begins.
        /// </summary>
        public int ValueStart;

        /// <summary>
        /// Length of ValueBytes.
        /// </summary>
        public int ValueLength;

        /// <summary>
        /// Array element index.
        /// For non-array, this is 0.
        /// For ArrayBegin, this is 0.
        /// For ArrayEnd, this is the same as ArrayCount.
        /// </summary>
        public ushort ArrayIndex;

        /// <summary>
        /// Array element count. For non-array, this is 1.
        /// This may be 0 in the case of variable-length array.
        /// </summary>
        public ushort ArrayCount;

        /// <summary>
        /// Nonzero for simple items (fixed-size non-struct).
        /// Zero for complex items (variable-size or struct).
        /// </summary>
        public byte ElementSize;

        /// <summary>
        /// Field's underlying encoding. The encoding indicates how to determine the field's
        /// size and the semantic type to use when Format = Default.
        /// </summary>
        public EventHeaderFieldEncoding Encoding;

        /// <summary>
        /// Field's semantic type. May be Default, in which case the semantic type should be
        /// determined based on the default format for the field's encoding.
        /// For StructBegin/StructEnd, this contains the struct field count.
        /// </summary>
        public EventHeaderFieldFormat Format;

        /// <summary>
        /// 0 if item is not an ArrayBegin, ArrayEnd, or array Value.
        /// FlagCArray or FlagVArray if item is an ArrayBegin, ArrayEnd, or array Value.
        /// </summary>
        public EventHeaderFieldEncoding ArrayFlags;

        /// <summary>
        /// Field tag, or 0 if none.
        /// </summary>
        public ushort FieldTag;

        /// <summary>
        /// True if this item's event had the big-endian flag set.
        /// </summary>
        public bool EventBigEndian;

        /// <summary>
        /// Initializes a new instance of the EventHeaderItemInfo struct.
        /// </summary>
        public EventHeaderItemInfo(
            ReadOnlySpan<byte> eventData,
            int nameStart,
            int nameLength,
            int valueStart,
            int valueLength,
            ushort arrayIndex,
            ushort arrayCount,
            byte elementSize,
            EventHeaderFieldEncoding encoding,
            EventHeaderFieldFormat format,
            EventHeaderFieldEncoding arrayFlags,
            ushort fieldTag,
            bool eventBigEndian)
        {
            this.EventData = eventData;
            this.NameStart = nameStart;
            this.NameLength = nameLength;
            this.ValueStart = valueStart;
            this.ValueLength = valueLength;
            this.ArrayIndex = arrayIndex;
            this.ArrayCount = arrayCount;
            this.ElementSize = elementSize;
            this.Encoding = encoding;
            this.Format = format;
            this.ArrayFlags = arrayFlags;
            this.FieldTag = fieldTag;
            this.EventBigEndian = eventBigEndian;
        }

        /// <summary>
        /// Raw field value bytes.
        /// ValueLength is nonzero for Value items and for ArrayBegin of array of simple values.
        /// ValueLength is zero for everything else, including ArrayBegin of array of complex items.
        /// For strings, ValueBytes does not include length prefix or NUL termination.
        /// </summary>
        public readonly ReadOnlySpan<byte> ValueBytes =>
            this.EventData.Slice(this.ValueStart, this.ValueLength);

        /// <summary>
        /// UTF-8 encoded field name followed by 0 or more field attributes,
        /// e.g. "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public readonly ReadOnlySpan<byte> NameBytes =>
            this.EventData.Slice(this.NameStart, this.NameLength);

        /// <summary>
        /// Gets a new string (decoded from NameBytes) containing
        /// field name followed by 0 or more field attributes, e.g.
        /// "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public readonly string Name =>
            Text.Encoding.UTF8.GetString(this.EventData.Slice(this.NameStart, this.NameLength));

        /// <summary>
        /// Attempts to convert a Unix time_t (signed seconds since 1970) to a DateTime.
        /// Returns false if the result is less than DateTime.MinValue or greater than
        /// DateTime.MaxValue.
        /// </summary>
        public static bool TryUnixTimeToDateTime(long secondsSince1970, out DateTime value)
        {
            const long UnixEpochSeconds = 62135596800;
            const long DaysToYear10000 = 3652059;
            const long SecondsPerDay = 60 * 60 * 24;
            const long MaxSeconds = DaysToYear10000 * SecondsPerDay - UnixEpochSeconds;

            if (secondsSince1970 < -UnixEpochSeconds ||
                secondsSince1970 >= MaxSeconds)
            {
                value = DateTime.MinValue;
                return false;
            }
            else
            {
                value = new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
                return true;
            }
        }

        /// <summary>
        /// Converts a Unix time_t (signed seconds since 1970) to a string.
        /// If year is in range 0001..9999, returns a new string like "2020-02-02T02:02:02".
        /// If year is outside of 0001..9999, returns a new string like "TIME(-1234567890)".
        /// </summary>
        public static string UnixTimeToString(long secondsSince1970)
        {
            return TryUnixTimeToDateTime(secondsSince1970, out var value)
                ? value.ToString("s", CultureInfo.InvariantCulture)
                : "TIME(" + secondsSince1970.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "OK(0)" or "EPERM(1)". Otherwise returns null.
        /// </summary>
        public static string? ErrnoLookup(int linuxErrno)
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
        /// string like "OK(0)" or "EPERM(1)". Otherwise returns a new string like
        /// "ERRNO(404)".
        /// </summary>
        public static string ErrnoToString(int linuxErrno)
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

        /// <summary>
        /// Gets ValueBytes interpreted as a signed integer.
        /// Returns false if ValueLength is not 1, 2, or 4.
        /// </summary>
        public readonly bool TryGetInt32(out Int32 value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case 1: value = unchecked((SByte)this.EventData[this.ValueStart]); return true;
                case 2: value = byteReader.ReadI16(this.EventData.Slice(this.ValueStart)); return true;
                case 4: value = byteReader.ReadI32(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as an unsigned integer.
        /// Returns false if ValueLength is not 1, 2, or 4.
        /// </summary>
        public readonly bool TryGetUInt32(out UInt32 value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case 1: value = this.EventData[this.ValueStart]; return true;
                case 2: value = byteReader.ReadU16(this.EventData.Slice(this.ValueStart)); return true;
                case 4: value = byteReader.ReadU32(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a signed integer.
        /// Returns false if ValueLength is not 1, 2, 4, or 8.
        /// </summary>
        public readonly bool TryGetInt64(out Int64 value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case 1: value = unchecked((SByte)this.EventData[this.ValueStart]); return true;
                case 2: value = byteReader.ReadI16(this.EventData.Slice(this.ValueStart)); return true;
                case 4: value = byteReader.ReadI32(this.EventData.Slice(this.ValueStart)); return true;
                case 8: value = byteReader.ReadI64(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as an unsigned integer.
        /// Returns false if ValueLength is not 1, 2, 4, or 8.
        /// </summary>
        public readonly bool TryGetUInt64(out UInt64 value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case 1: value = this.EventData[this.ValueStart]; return true;
                case 2: value = byteReader.ReadU16(this.EventData.Slice(this.ValueStart)); return true;
                case 4: value = byteReader.ReadU32(this.EventData.Slice(this.ValueStart)); return true;
                case 8: value = byteReader.ReadU64(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a 32-bit float.
        /// Returns false if ValueLength is not 4.
        /// </summary>
        public readonly bool TryGetFloat(out Single value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case sizeof(Single): value = byteReader.ReadF32(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a 32-bit or 64-bit float.
        /// Returns false if ValueLength is not 4 or 8.
        /// </summary>
        public readonly bool TryGetFloat(out Double value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case sizeof(Single): value = byteReader.ReadF32(this.EventData.Slice(this.ValueStart)); return true;
                case sizeof(Double): value = byteReader.ReadF64(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a 32-bit or 64-bit float.
        /// Returns false if ValueLength is not 4 or 8.
        /// </summary>
        public readonly bool TryGetFloat(out string value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case sizeof(Single):
                    value = byteReader.ReadF32(this.EventData.Slice(this.ValueStart)).ToString(CultureInfo.InvariantCulture);
                    return true;
                case sizeof(Double):
                    value = byteReader.ReadF64(this.EventData.Slice(this.ValueStart)).ToString(CultureInfo.InvariantCulture);
                    return true;
                default:
                    value = "";
                    return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a big-endian Guid.
        /// Returns false if ValueLength is not 16.
        /// </summary>
        public readonly bool TryGetGuid(out Guid value)
        {
            switch (this.ValueLength)
            {
                case 16: value = Utility.ReadGuidBigEndian(this.EventData.Slice(this.ValueStart)); return true;
                default: value = new Guid(); return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a big-endian 16-bit integer.
        /// Returns false if ValueLength is not 2.
        /// </summary>
        public readonly bool TryGetPort(out int value)
        {
            switch (this.ValueLength)
            {
                case sizeof(UInt16): value = BinaryPrimitives.ReadUInt16BigEndian(this.EventData.Slice(this.ValueStart)); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Returns ValueBytes interpreted as an IPAddress.
        /// Returns false if ValueLength is not 4 (IPv4) or 8 (IPv6).
        /// </summary>
        public readonly bool TryGetIPAddress(out IPAddress value)
        {
            var c = this.ValueLength;
            if (c != 4 && c != 16)
            {
                value = IPAddress.None;
                return false;
            }
            else
            {
                value = new IPAddress(this.EventData.Slice(this.ValueStart, c));
                return true;
            }
        }

        /// <summary>
        /// Returns ValueBytes interpreted as a Unix time_t (signed seconds since 1970).
        /// Returns false if ValueLength is not 4 or 8, if time is less than
        /// DateTime.MinValue (0001), or if time is greater than DateTime.MaxValue (9999).
        /// </summary>
        public readonly bool TryGetUnixTime(out DateTime value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            Int64 seconds;
            switch (this.ValueLength)
            {
                case 4: seconds = byteReader.ReadI32(this.EventData.Slice(this.ValueStart)); break;
                case 8: seconds = byteReader.ReadI64(this.EventData.Slice(this.ValueStart)); break;
                default: value = new DateTime(); return false;
            }

            return TryUnixTimeToDateTime(seconds, out value);
        }

        /// <summary>
        /// Returns ValueBytes interpreted as a Unix time_t (signed seconds since 1970)
        /// and formatted as a string. If the year is in the range 0001..9999, the string
        /// will be formatted like "2020-02-02T02:02:02"; otherwise, the string will be
        /// formatted like "TIME(-1234567890)".
        /// Returns false if ValueLength is not 4 or 8.
        /// </summary>
        public readonly bool TryGetUnixTime(out string value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            Int64 seconds;
            switch (this.ValueLength)
            {
                case 4: seconds = byteReader.ReadI32(this.EventData.Slice(this.ValueStart)); break;
                case 8: seconds = byteReader.ReadI64(this.EventData.Slice(this.ValueStart)); break;
                default: value = ""; return false;
            }

            value = UnixTimeToString(seconds);
            return true;
        }

        /// <summary>
        /// Returns ValueBytes interpreted as a Linux error number.
        /// Returns false if ValueLength is not 4.
        /// </summary>
        public readonly bool TryGetErrno(out int value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case 4: value = byteReader.ReadI32(this.EventData.Slice(this.ValueStart)); return true;
                default: value = -1; return false;
            }
        }

        /// <summary>
        /// Returns ValueBytes interpreted as a Linux error number and formatted as a string.
        /// If the error number is recognized, the string will be formatted like "OK(0)" or "EPERM(1)";
        /// otherwise the string will be formatted like "ERRNO(404)".
        /// Returns false if ValueLength is not 4.
        /// </summary>
        public readonly bool TryGetErrno(out string value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            Int32 errno;
            switch (this.ValueLength)
            {
                case 4: errno = byteReader.ReadI32(this.EventData.Slice(this.ValueStart)); break;
                default: value = ""; return false;
            }

            value = ErrnoToString(errno);
            return true;
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a bool8, bool16, or bool32.
        /// Returns false if ValueLength is not 1, 2, or 4.
        /// </summary>
        public readonly bool TryGetBoolean(out Int32 value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            switch (this.ValueLength)
            {
                case 1: value = this.EventData[this.ValueStart]; return true; // Don't sign-extend bool8.
                case 2: value = byteReader.ReadU16(this.EventData.Slice(this.ValueStart)); return true; // Don't sign-extend bool16.
                case 4: value = unchecked((int)byteReader.ReadU32(this.EventData.Slice(this.ValueStart))); return true;
                default: value = 0; return false;
            }
        }

        /// <summary>
        /// Gets ValueBytes interpreted as a bool8, bool16, or bool32 and formatted as a
        /// string. If the value is 0 or 1, the string will be "false" or "true"; otherwise
        /// the string will be the value formatted as a signed integer.
        /// Returns false if ValueLength is not 1, 2, or 4.
        /// </summary>
        public readonly bool TryGetBoolean(out string value)
        {
            var byteReader = new PerfByteReader(this.EventBigEndian);
            UInt32 intVal;
            switch (this.ValueLength)
            {
                case 1: intVal = this.EventData[this.ValueStart]; break;
                case 2: intVal = byteReader.ReadU16(this.EventData.Slice(this.ValueStart)); break;
                case 4: intVal = byteReader.ReadU32(this.EventData.Slice(this.ValueStart)); break;
                default: value = ""; return false;
            }

            value = BooleanToString((UInt32)intVal);
            return true;
        }

        /// <summary>
        /// Gets ValueBytes decoded as a new string. Character encoding (e.g. UTF-8, Latin1,
        /// etc.) is determined from Encoding, Format, and possibly from a BOM at the
        /// beginning of ValueBytes.
        /// </summary>
        public readonly String GetString()
        {
            Encoding encoding;
            var bytes = this.GetStringBytes(out encoding);
            return encoding.GetString(bytes);
        }

        /// <summary>
        /// Determines the character encoding for ValueBytes based on Encoding, format,
        /// and possibly from a BOM at the beginning of ValueBytes. Returns the
        /// character bytes, not including the BOM (if any).
        /// </summary>
        /// <param name="encoding">Receives the value's character encoding.</param>
        /// <returns>Character bytes, not including the BOM (if any).</returns>
        public readonly ReadOnlySpan<byte> GetStringBytes(out Encoding encoding)
        {
            var v = this.EventData.Slice(this.ValueStart, this.ValueLength);
            switch (this.Format)
            {
                case EventHeaderFieldFormat.String8:
                    encoding = Utility.EncodingLatin1;
                    break;
                case EventHeaderFieldFormat.StringUtfBom:
                case EventHeaderFieldFormat.StringXml:
                case EventHeaderFieldFormat.StringJson:
                    if (v.Length >= 4 &&
                        v[0] == 0xFF &&
                        v[1] == 0xFE &&
                        v[2] == 0x00 &&
                        v[3] == 0x00)
                    {
                        v = v.Slice(4);
                        encoding = Text.Encoding.UTF32;
                    }
                    else if (v.Length >= 4 &&
                        v[0] == 0x00 &&
                        v[1] == 0x00 &&
                        v[2] == 0xFE &&
                        v[3] == 0xFF)
                    {
                        v = v.Slice(4);
                        encoding = Utility.EncodingUTF32BE;
                    }
                    else if (v.Length >= 3 &&
                        v[0] == 0xEF &&
                        v[1] == 0xBB &&
                        v[2] == 0xBF)
                    {
                        v = v.Slice(3);
                        encoding = Text.Encoding.UTF8;
                    }
                    else if (v.Length >= 2 &&
                        v[0] == 0xFF &&
                        v[1] == 0xFE)
                    {
                        v = v.Slice(2);
                        encoding = Text.Encoding.Unicode;
                    }
                    else if (v.Length >= 2 &&
                        v[0] == 0xFE &&
                        v[1] == 0xFF)
                    {
                        v = v.Slice(2);
                        encoding = Text.Encoding.BigEndianUnicode;
                    }
                    else
                    {
                        goto StringUtf;
                    }
                    break;
                case EventHeaderFieldFormat.StringUtf:
                default:
                StringUtf:
                    switch (this.Encoding)
                    {
                        default:
                            encoding = Utility.EncodingLatin1;
                            break;
                        case EventHeaderFieldEncoding.Value8:
                        case EventHeaderFieldEncoding.ZStringChar8:
                        case EventHeaderFieldEncoding.StringLength16Char8:
                            encoding = Text.Encoding.UTF8;
                            break;
                        case EventHeaderFieldEncoding.Value16:
                        case EventHeaderFieldEncoding.ZStringChar16:
                        case EventHeaderFieldEncoding.StringLength16Char16:
                            encoding = Text.Encoding.Unicode;
                            break;
                        case EventHeaderFieldEncoding.Value32:
                        case EventHeaderFieldEncoding.ZStringChar32:
                        case EventHeaderFieldEncoding.StringLength16Char32:
                            encoding = Text.Encoding.UTF32;
                            break;
                    }
                    break;
            }

            return v;
        }

        /// <summary>
        /// Formats ValueBytes as a string based on Encoding and Format.
        /// </summary>
        public string FormatValue()
        {
            Debug.Assert(this.Encoding > EventHeaderFieldEncoding.Struct);
            Debug.Assert(this.Encoding < EventHeaderFieldEncoding.Max);

            switch (this.Format)
            {
                default:
                    switch (this.Encoding)
                    {
                        case EventHeaderFieldEncoding.Value8:
                        case EventHeaderFieldEncoding.Value16:
                        case EventHeaderFieldEncoding.Value32:
                        case EventHeaderFieldEncoding.Value64:
                            goto UnsignedInt;
                        case EventHeaderFieldEncoding.ZStringChar8:
                        case EventHeaderFieldEncoding.ZStringChar16:
                        case EventHeaderFieldEncoding.ZStringChar32:
                        case EventHeaderFieldEncoding.StringLength16Char8:
                        case EventHeaderFieldEncoding.StringLength16Char16:
                        case EventHeaderFieldEncoding.StringLength16Char32:
                            goto StringUtf;
                    }
                    break;

                case EventHeaderFieldFormat.UnsignedInt:
                UnsignedInt:
                    {
                        if (this.TryGetUInt64(out var value))
                        {
                            return value.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    break;

                case EventHeaderFieldFormat.SignedInt:
                    {
                        if (this.TryGetInt64(out var value))
                        {
                            return value.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    break;

                case EventHeaderFieldFormat.HexInt:
                    {
                        if (this.TryGetUInt64(out var value))
                        {
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", value);
                        }
                    }
                    break;

                case EventHeaderFieldFormat.Errno:
                    {
                        if (this.TryGetErrno(out string value))
                        {
                            return value;
                        }
                    }
                    break;

                case EventHeaderFieldFormat.Pid:
                    {
                        if (this.TryGetInt32(out var value))
                        {
                            return value.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    break;

                case EventHeaderFieldFormat.Time:
                    {
                        if (this.TryGetUnixTime(out string value))
                        {
                            return value;
                        }
                    }
                    break;

                case EventHeaderFieldFormat.Boolean:
                    {
                        if (this.TryGetBoolean(out string value))
                        {
                            return value;
                        }
                    }
                    break;

                case EventHeaderFieldFormat.Float:
                    {
                        if (this.TryGetFloat(out string value))
                        {
                            return value;
                        }
                    }
                    break;

                case EventHeaderFieldFormat.HexBinary:
                    break;

                case EventHeaderFieldFormat.String8:
                case EventHeaderFieldFormat.StringUtf:
                case EventHeaderFieldFormat.StringUtfBom:
                case EventHeaderFieldFormat.StringXml:
                case EventHeaderFieldFormat.StringJson:
                StringUtf:
                    return this.GetString();

                case EventHeaderFieldFormat.Uuid:
                    {
                        if (this.TryGetGuid(out var value))
                        {
                            return value.ToString("D", CultureInfo.InvariantCulture);
                        }
                    }
                    break;

                case EventHeaderFieldFormat.Port:
                    {
                        if (this.TryGetPort(out var value))
                        {
                            return value.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    break;

                case EventHeaderFieldFormat.IPv4:
                case EventHeaderFieldFormat.IPv6:
                    {
                        if (this.TryGetIPAddress(out var value))
                        {
                            return value.ToString();
                        }
                    }
                    break;
            }

            return Utility.ToHexString(this.EventData.Slice(this.ValueStart, this.ValueLength));
        }
    }
}
